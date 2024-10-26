using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Cards;
using SteamKit2;

namespace AchievementsBooster;

internal sealed class Booster : IDisposable {
  private enum EBoostingState : byte {
    None,
    ArchiFarming,
    ArchiPlayedWhileIdle,
    BoosterPlayed
  }

  private static BoosterGlobalConfig GlobalConfig => AchievementsBooster.GlobalConfig;

  internal BoosterHandler BoosterHandler => AppHandler.BoosterHandler;

  private AppHandler AppHandler { get; }

  private readonly Bot Bot;
  private readonly BotCache Cache;
  private readonly Logger Logger;

  internal volatile bool IsBoostingStarted;

  private EBoostingState BoostingState { get; set; } = EBoostingState.None;

  private Dictionary<uint, BoostableApp> BoostingApps { get; } = [];
  private Queue<uint> ArchiBoostableAppsPlayedWhileIdle { get; }

  private Timer BoosterHeartBeatTimer { get; }
  private DateTime LastBoosterHeartBeatTime { get; set; }
  private DateTime LastUpdateOwnedGamesTime { get; set; }
  private SemaphoreSlim BoosterHeartBeatSemaphore { get; } = new SemaphoreSlim(1);

  internal Booster(Bot bot) {
    Bot = bot;
    Logger = new Logger(bot.ArchiLogger);
    Cache = LoadOrCreateCacheForBot(bot);
    AppHandler = new AppHandler(new BoosterHandler(bot, Logger), Cache, Logger);
    BoosterHeartBeatTimer = new Timer(Boosting, null, Timeout.Infinite, Timeout.Infinite);

    // Since GamesPlayedWhileIdle may never change
    ArchiBoostableAppsPlayedWhileIdle = new Queue<uint>(bot.BotConfig?.GamesPlayedWhileIdle ?? []);
  }

  public void Dispose() => BoosterHeartBeatTimer.Dispose();

  internal Task OnSteamCallbacksInit(CallbackManager callbackManager) {
    ArgumentNullException.ThrowIfNull(callbackManager);
    BoosterHandler.Init();
    return Task.CompletedTask;
  }

  [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
  internal Task OnInitModules(IReadOnlyDictionary<string, JsonElement> additionalConfigProperties) {
    if (additionalConfigProperties != null) {
    }
    return Task.CompletedTask;
  }

  internal string Start(bool command = false) {
    if (IsBoostingStarted) {
      Logger.Trace(Messages.BoostingStarted);
      return Messages.BoostingStarted;
    }
    IsBoostingStarted = true;

    TimeSpan dueTime = TimeSpan.Zero;
    if (command) {
      Logger.Info("The boosting process is starting");
    }
    else {
      dueTime = TimeSpan.FromMinutes(Constants.AutoStartDelayTime);
      Logger.Info($"The boosting process will begin in {Constants.AutoStartDelayTime} minutes");
    }
    _ = BoosterHeartBeatTimer.Change(dueTime, Timeout.InfiniteTimeSpan);

    return Strings.Done;
  }

  internal string Stop() {
    if (!IsBoostingStarted) {
      Logger.Trace(Messages.BoostingNotStart);
      return Messages.BoostingNotStart;
    }

    IsBoostingStarted = false;

    if (BoostingState == EBoostingState.BoosterPlayed) {
      // Update playtime and put app to sleep
      DateTime currentTime = DateTime.Now;
      double deltaTime = (currentTime - LastBoosterHeartBeatTime).TotalHours;
      SendBoostingAppsToSleep((app) => {
        app.LastPlayedTime = currentTime;
        app.ContinuousBoostingHours += deltaTime;
      });

      _ = Bot.Actions.Resume();
    }

    _ = BoosterHeartBeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
    BoostingState = EBoostingState.None;

    Logger.Info("The boosting process has been stopped!");
    return Strings.Done;
  }

  private async void Boosting(object? state) {
    if (!BoosterHeartBeatSemaphore.Wait(0)) {
      Logger.Warning("The boosting process is currently running !!!");
      return;
    }

    Logger.Trace("Boosting heartbeating ...");
    DateTime currentTime = DateTime.Now;

    try {
      BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);

      if (IsSleepingTime(currentTime)) {
        throw new BoostingImpossibleException(Messages.SleepingTime);
      }

      await UpdateAppHandler(currentTime).ConfigureAwait(false);
      if (AppHandler.OwnedGames.Count == 0) {
        BoostingApps.Clear();
        throw new BoostingImpossibleException(string.Format(CultureInfo.CurrentCulture, Messages.NoGamesBoosting));
      }

      EBoostingState newBoostingState = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count > 0
        ? EBoostingState.ArchiFarming
        : Bot.BotConfig?.GamesPlayedWhileIdle.Count > 0
          ? EBoostingState.ArchiPlayedWhileIdle
          : EBoostingState.BoosterPlayed;

      if (newBoostingState == BoostingState) {
        await AchieveAchievements(currentTime).ConfigureAwait(false);

        if (GlobalConfig.MaxBoostingHours > 0) {
          CheckAndSleepBoostingApps();
        }
      }
      else {
        BoostingState = newBoostingState;
        SendBoostingAppsToSleep();
      }

      // Add new apps for boosting if need
      if (BoostingApps.Count < GlobalConfig.MaxBoostingApps) {
        List<BoostableApp> newApps = await FindNewAppsForBoosting(GlobalConfig.MaxBoostingApps - BoostingApps.Count).ConfigureAwait(false);
        newApps.ForEach(app => BoostingApps.TryAdd(app.ID, app));
      }

      if (BoostingApps.Count > 0) {
        if (BoostingState is EBoostingState.BoosterPlayed) {
          BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
          (bool success, string message) = await Bot.Actions.Play(BoostingApps.Keys.ToList()).ConfigureAwait(false);
          if (!success) {
            throw new BoostingImpossibleException(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
          }
        }

        foreach (BoostableApp app in BoostingApps.Values) {
          Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApp, app.FullName, app.RemainingAchievementsCount));
        }
      }
      else {
        if (BoostingState is EBoostingState.ArchiFarming) {
          Logger.Info(Messages.NoBoostingAppsInArchiFarming);
        }
        else if (BoostingState is EBoostingState.ArchiPlayedWhileIdle) {
          Logger.Info(Messages.NoBoostingAppsInArchiPlayedWhileIdle);
        }
        else {
          // BoostingState is EBoostingState.BoosterPlayed
          throw new BoostingImpossibleException(Messages.NoBoostingApps);
        }
      }
    }
    catch (Exception exception) {
      if (exception is BoostingImpossibleException) {
        Logger.Info(exception.Message);
      }
      else {
        Logger.Exception(exception);
      }

      SendBoostingAppsToSleep();
      BoostingState = EBoostingState.None;
      _ = Bot.Actions.Resume();
    }
    finally {
      LastBoosterHeartBeatTime = currentTime;

      if (IsBoostingStarted) {
        // Due time for the next boosting
        TimeSpan dueTime = TimeSpan.FromMinutes(GlobalConfig.BoostTimeInterval) + TimeSpanUtils.RandomInMinutesRange(0, GlobalConfig.ExpandBoostTimeInterval);
        _ = BoosterHeartBeatTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
        Logger.Trace($"The next heartbeat will occur in {dueTime.Minutes} minutes{(dueTime.Seconds > 0 ? $" and {dueTime.Seconds} seconds" : "")}!");
      }
      _ = BoosterHeartBeatSemaphore.Release();
    }
  }

  private void SendBoostingAppsToSleep(Action<BoostableApp>? action = null) {
    if (BoostingApps.Count > 0 && BoostingState is EBoostingState.BoosterPlayed) {
      foreach (BoostableApp app in BoostingApps.Values) {
        action?.Invoke(app);
        AppHandler.SetAppToSleep(app);
      }
    }

    BoostingApps.Clear();
  }

  private async Task UpdateAppHandler(DateTime currentTime) {
    if (AppHandler.OwnedGames.Count == 0 || (currentTime - LastUpdateOwnedGamesTime).TotalHours > 6.0) {
      Dictionary<uint, string>? ownedGames = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false);
      if (ownedGames != null) {
        AppHandler.UpdateOwnedGames(ownedGames.Keys.ToHashSet());
        LastUpdateOwnedGamesTime = currentTime;
      }
    }

    AppHandler.Update();
  }

  private async Task AchieveAchievements(DateTime currentTime) {
    if (BoostingApps.Count == 0) {
      return;
    }

    if (BoostingState is EBoostingState.ArchiFarming) {
      // Intersect between BoostingApps and CurrentGamesFarming
      List<uint> boostingAppIDs = [];
      foreach (Game game in Bot.CardsFarmer.CurrentGamesFarmingReadOnly) {
        if (BoostingApps.ContainsKey(game.AppID)) {
          boostingAppIDs.Add(game.AppID);
        }
      }

      if (boostingAppIDs.Count > 0) {
        List<uint> exceptAppIDs = BoostingApps.Keys.Except(boostingAppIDs).ToList();
        exceptAppIDs.ForEach(appID => BoostingApps.Remove(appID));
      }
      else {
        BoostingApps.Clear();
      }
    }

    double deltaTime = (currentTime - LastBoosterHeartBeatTime).TotalHours;
    foreach (BoostableApp app in BoostingApps.Values) {
      BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
      app.LastPlayedTime = currentTime;
      app.ContinuousBoostingHours += deltaTime;

      (bool success, string message) = await app.UnlockNextAchievement(BoosterHandler).ConfigureAwait(false);
      if (success) {
        Logger.Info(message);
        if (app.RemainingAchievementsCount == 0) {
          _ = BoostingApps.Remove(app.ID);
          _ = Cache.PerfectGames.Add(app.ID);
          Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostingAppComplete, app.FullName));
        }
      }
      else {
        Logger.Warning(message);
        if (app.FailedUnlockCount > Constants.MaxUnlockAchievementTries) {
          _ = BoostingApps.Remove(app.ID);
          AppHandler.PlaceAtLastStandQueue(app);
        }
      }
    }
  }

  private void CheckAndSleepBoostingApps() {
    if (BoostingApps.Count == 0) {
      return;
    }

    foreach (uint appID in BoostingApps.Keys.ToList()) {
      BoostableApp app = BoostingApps[appID];
      // Boosting over hours
      if (app.ContinuousBoostingHours >= GlobalConfig.MaxBoostingHours) {
        _ = BoostingApps.Remove(appID);
        AppHandler.SetAppToSleep(app);
      }
    }
  }

  private async Task<List<BoostableApp>> FindNewAppsForBoosting(int count) {
    List<BoostableApp> results = [];
    switch (BoostingState) {
      case EBoostingState.ArchiFarming:
        Game[] currentGamesFarming = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.ToArray();
        for (int index = 0; index < currentGamesFarming.Length && results.Count < count; index++) {
          uint appID = currentGamesFarming[index].AppID;
          if (!BoostingApps.ContainsKey(appID)) {
            BoostableApp? app = await AppHandler.GetBoostableApp(appID).ConfigureAwait(false);
            if (app != null) {
              results.Add(app);
            }
          }
        }
        break;
      case EBoostingState.ArchiPlayedWhileIdle:
        while (ArchiBoostableAppsPlayedWhileIdle.Count > 0 && results.Count < count) {
          uint appID = ArchiBoostableAppsPlayedWhileIdle.Dequeue();
          BoostableApp? app = await AppHandler.GetBoostableApp(appID).ConfigureAwait(false);
          if (app != null) {
            results.Add(app);
          }
        }
        break;
      case EBoostingState.BoosterPlayed:
        BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
        results = await AppHandler.NextBoosterApps(count).ConfigureAwait(false);
        break;
      case EBoostingState.None:
      // Not reachable
      default:
        break;
    }

    return results;
  }

  private BotCache LoadOrCreateCacheForBot(Bot bot) {
    if (bot.BotDatabase == null) {
      throw new InvalidOperationException(nameof(bot.BotDatabase));
    }

    BotCache? cache = null;
    JsonElement jsonElement = bot.BotDatabase.LoadFromJsonStorage(Constants.BotCacheKey);
    if (jsonElement.ValueKind == JsonValueKind.Object) {
      try {
        cache = jsonElement.ToJsonObject<BotCache>();
      }
      catch (Exception ex) {
        Logger.Exception(ex);
      }
    }

    cache ??= new BotCache();
    cache.Init(bot.BotDatabase);

    return cache;
  }

  private static bool IsSleepingTime(DateTime currentTime) {
    if (GlobalConfig.SleepingHours == 0) {
      return false;
    }

    DateTime weakUpTime = new(currentTime.Year, currentTime.Month, currentTime.Day, 6, 0, 0, 0);
    if (currentTime < weakUpTime) {
      DateTime sleepStartTime = weakUpTime.AddHours(-GlobalConfig.SleepingHours);
      if (currentTime > sleepStartTime) {
        return true;
      }
    }

    return false;
  }

}

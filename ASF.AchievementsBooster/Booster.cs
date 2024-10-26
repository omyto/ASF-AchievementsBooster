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
      SendBoostingAppsToSleep(true, DateTime.Now);
      _ = Bot.Actions.Resume();
    }

    _ = BoosterHeartBeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
    BoostingState = EBoostingState.None;

    Logger.Info("Achievements Booster Stopped!");
    return Strings.Done;
  }

  private void SendBoostingAppsToSleep(bool updateBoostingHours, DateTime? now = null) {
    if (BoostingApps.Count > 0 && BoostingState is EBoostingState.BoosterPlayed) {
      DateTime currentTime = now ?? DateTime.Now;
      double deltaTime = (currentTime - LastBoosterHeartBeatTime).TotalHours;

      foreach (BoostableApp app in BoostingApps.Values) {
        app.LastPlayedTime = currentTime;
        if (updateBoostingHours) {
          app.ContinuousBoostingHours += deltaTime;
        }
        AppHandler.SetAppToSleep(app);
      }
    }

    BoostingApps.Clear();
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
        SendBoostingAppsToSleep(false);
      }

      // Add new apps for boosting if need
      if (BoostingApps.Count < GlobalConfig.MaxBoostingApps) {
        await FindNewAppsForBoosting().ConfigureAwait(false);
      }
    }
    catch (Exception exception) {
      if (exception is BoostingImpossibleException) {
        Logger.Info(exception.Message);
      }
      else {
        Logger.Exception(exception);
      }

      SendBoostingAppsToSleep(false);
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

  private async Task FindNewAppsForBoosting() {
    switch (BoostingState) {
      case EBoostingState.ArchiFarming:
        foreach (Game game in Bot.CardsFarmer.CurrentGamesFarmingReadOnly) {
          if (!BoostingApps.ContainsKey(game.AppID)) {
            BoostableApp? app = await AppHandler.GetBoostableApp(game.AppID).ConfigureAwait(false);
            if (app != null) {
              _ = BoostingApps.TryAdd(game.AppID, app);
              if (BoostingApps.Count == GlobalConfig.MaxBoostingApps) {
                break;
              }
            }
          }
        }
        Logger.Info(BoostingApps.Count == 0 ? Messages.NoBoostingAppsInArchiFarming : BoostingAppsMessage());
        break;

      case EBoostingState.ArchiPlayedWhileIdle:
        while (ArchiBoostableAppsPlayedWhileIdle.Count > 0 && BoostingApps.Count < GlobalConfig.MaxBoostingApps) {
          uint appID = ArchiBoostableAppsPlayedWhileIdle.Dequeue();
          BoostableApp? app = await AppHandler.GetBoostableApp(appID).ConfigureAwait(false);
          if (app != null) {
            _ = BoostingApps.TryAdd(appID, app);
          }
        }
        Logger.Info(BoostingApps.Count == 0 ? Messages.NoBoostingAppsInArchiPlayedWhileIdle : BoostingAppsMessage());
        break;

      case EBoostingState.BoosterPlayed:
        BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
        List<BoostableApp> apps = await AppHandler.NextBoosterApps(GlobalConfig.MaxBoostingApps - BoostingApps.Count).ConfigureAwait(false);
        apps.ForEach(app => BoostingApps.TryAdd(app.ID, app));

        if (BoostingApps.Count == 0) {
          _ = Bot.Actions.Resume();
          BoostingState = EBoostingState.None;
          Logger.Info(Messages.NoBoostingApps);
          break;
        }

        BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
        (bool success, string message) = await Bot.Actions.Play(BoostingApps.Keys.ToList()).ConfigureAwait(false);
        if (success) {
          Logger.Info(BoostingAppsMessage());
        }
        else {
          SendBoostingAppsToSleep(false);
          BoostingState = EBoostingState.None;
          Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
        }
        break;

      case EBoostingState.None:
      default:
        break;
    }

  }

  private string BoostingAppsMessage() => BoostingApps.Count switch {
    0 => throw new ArgumentException($"{nameof(BoostingApps)} is empty"),
    1 => string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, BoostingApps.First().Value.FullName),
    _ => string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, string.Join(",", BoostingApps.Keys)),
  };

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

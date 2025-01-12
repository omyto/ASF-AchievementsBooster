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
using EBoostingMode = AchievementsBooster.Storage.BoosterGlobalConfig.EBoostingMode;

namespace AchievementsBooster;

internal sealed class Booster {
  private enum EBoostingState : byte {
    None,
    ArchiFarming,
    ArchiPlayedWhileIdle,
    BoosterPlayed
  }

  private static BoosterGlobalConfig GlobalConfig => AchievementsBooster.GlobalConfig;

  internal BoosterHandler BoosterHandler => AppManager.BoosterHandler;

  private AppManager AppManager { get; }

  private readonly Bot Bot;
  private readonly BotCache Cache;
  private readonly Logger Logger;

  private EBoostingState BoostingState { get; set; } = EBoostingState.None;

  private Dictionary<uint, AppBoostInfo> BoostingApps { get; } = [];
  private Queue<uint> ArchiBoostableAppsPlayedWhileIdle { get; }

  private Timer? BoosterHeartBeatTimer { get; set; }
  private uint LastSessionNo { get; set; }
  private uint CurrentSessionNo { get; set; }
  private DateTime LastBoosterHeartBeatTime { get; set; }
  private DateTime LastUpdateOwnedGamesTime { get; set; }
  private SemaphoreSlim BoosterHeartBeatSemaphore { get; } = new SemaphoreSlim(1);

  internal Booster(Bot bot) {
    Bot = bot;
    Logger = new Logger(bot.ArchiLogger);
    Cache = LoadOrCreateCacheForBot(bot);
    AppManager = new AppManager(new BoosterHandler(bot, Logger), Cache, Logger);

    // Since GamesPlayedWhileIdle may never change
    ArchiBoostableAppsPlayedWhileIdle = new Queue<uint>(bot.BotConfig?.GamesPlayedWhileIdle ?? []);
  }

  ~Booster() => StopTimer();

  private void StopTimer() {
    BoosterHeartBeatTimer?.Dispose();
    BoosterHeartBeatTimer = null;
  }

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
    if (BoosterHeartBeatTimer != null) {
      Logger.Trace(Messages.BoostingStarted);
      return Messages.BoostingStarted;
    }

    TimeSpan dueTime = TimeSpan.Zero;
    if (command) {
      Logger.Info("The boosting process is starting");
    }
    else {
      dueTime = TimeSpan.FromMinutes(Constants.AutoStartDelayTime);
      Logger.Info($"The boosting process will begin in {Constants.AutoStartDelayTime} minutes");
    }

    BoosterHeartBeatTimer = new Timer(Boosting, null, dueTime, Timeout.InfiniteTimeSpan);
    return Strings.Done;
  }

  internal string Stop() {
    if (BoosterHeartBeatTimer == null) {
      Logger.Trace(Messages.BoostingNotStart);
      return Messages.BoostingNotStart;
    }

    StopTimer();

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
    LastSessionNo = CurrentSessionNo++;
    DateTime currentTime = DateTime.Now;

    try {
      BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);

      if (IsSleepingTime(currentTime)) {
        throw new BoostingImpossibleException(Messages.SleepingTime);
      }

      await UpdateAppHandler(currentTime).ConfigureAwait(false);
      if (AppManager.OwnedGames.Count == 0) {
        BoostingApps.Clear();
        throw new BoostingImpossibleException(string.Format(CultureInfo.CurrentCulture, Messages.NoGamesBoosting));
      }

      EBoostingState lastState = BoostingState;
      HashSet<uint> lastPlaying = BoostingApps.Keys.ToHashSet();

      EBoostingState newBoostingState = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count > 0
        ? EBoostingState.ArchiFarming
        : Bot.BotConfig?.GamesPlayedWhileIdle.Count > 0
          ? EBoostingState.ArchiPlayedWhileIdle
          : EBoostingState.BoosterPlayed;

      if (newBoostingState == BoostingState) {
        await UnlockAchievements(currentTime).ConfigureAwait(false);
      }
      else {
        BoostingState = newBoostingState;
        SendBoostingAppsToSleep();
      }

      // Add new apps for boosting if need
      if (BoostingApps.Count < GlobalConfig.MaxAppBoostConcurrently) {
        List<AppBoostInfo> newApps = await FindNewAppsForBoosting(GlobalConfig.MaxAppBoostConcurrently - BoostingApps.Count).ConfigureAwait(false);
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

        foreach (AppBoostInfo app in BoostingApps.Values) {
          app.BoostSessionNo = CurrentSessionNo;
          app.LastPlayedTime = currentTime;
          Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApp, app.FullName, app.UnlockableAchievementsCount));
        }
      }
      else {
        if (BoostingState is EBoostingState.ArchiFarming) {
          if (lastState != EBoostingState.ArchiFarming || lastPlaying.Count > 0) {
            Logger.Info(Messages.NoBoostingAppsInArchiFarming);
          }
        }
        else if (BoostingState is EBoostingState.ArchiPlayedWhileIdle) {
          if (lastState != EBoostingState.ArchiPlayedWhileIdle || lastPlaying.Count > 0) {
            Logger.Info(Messages.NoBoostingAppsInArchiPlayedWhileIdle);
          }
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

      if (BoosterHeartBeatTimer != null) {
        // Due time for the next boosting
        TimeSpan dueTime = TimeSpanUtils.RandomInMinutesRange(GlobalConfig.MinBoostInterval, GlobalConfig.MaxBoostInterval);
        _ = BoosterHeartBeatTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
        Logger.Trace($"The next heartbeat will occur in {dueTime.Minutes} minutes{(dueTime.Seconds > 0 ? $" and {dueTime.Seconds} seconds" : "")}!");
      }
      _ = BoosterHeartBeatSemaphore.Release();
    }
  }

  private void SendBoostingAppsToSleep(Action<AppBoostInfo>? action = null) {
    if (BoostingApps.Count > 0 && BoostingState is EBoostingState.BoosterPlayed) {
      foreach (AppBoostInfo app in BoostingApps.Values) {
        action?.Invoke(app);
        AppManager.SetAppToSleep(app);
      }
    }

    BoostingApps.Clear();
  }

  private async Task UpdateAppHandler(DateTime currentTime) {
    if (AppManager.OwnedGames.Count == 0 || (currentTime - LastUpdateOwnedGamesTime).TotalHours > 16.0) {
      Dictionary<uint, string>? ownedGames = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false);
      if (ownedGames != null) {
        await AppManager.UpdateOwnedGames(ownedGames.Keys.ToHashSet()).ConfigureAwait(false);
        LastUpdateOwnedGamesTime = currentTime;
      }
    }
  }

  private async Task UnlockAchievements(DateTime currentTime) {
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
    foreach (AppBoostInfo app in BoostingApps.Values.ToArray()) {
      BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
      app.LastPlayedTime = currentTime;
      app.ContinuousBoostingHours += deltaTime;

      (bool success, string message) = await app.UnlockNextAchievement(BoosterHandler).ConfigureAwait(false);
      if (success) {
        Logger.Info(message);
        if (app.UnlockableAchievementsCount == 0) {
          _ = BoostingApps.Remove(app.ID);
          _ = Cache.PerfectGames.Add(app.ID);
          Logger.Info(string.Format(CultureInfo.CurrentCulture, app.RemainingAchievementsCount == 0 ? Messages.FinishedBoost : Messages.FinishedBoostable, app.FullName));
          continue;
        }

        if (GlobalConfig.BoostingMode is EBoostingMode.SingleDailyAchievementPerGame or EBoostingMode.UniqueGamesPerSession) {
          _ = BoostingApps.Remove(app.ID);
          AppManager.SetAppToSleep(app);
        }
      }
      else {
        Logger.Warning(message);
        if (app.UnlockableAchievementsCount == 0) {
          if (app.RemainingAchievementsCount == 0) {
            _ = Cache.PerfectGames.Add(app.ID);
          }
          _ = BoostingApps.Remove(app.ID);
          continue;
        }

        if (app.FailedUnlockCount > Constants.MaxUnlockAchievementTries) {
          _ = BoostingApps.Remove(app.ID);
          AppManager.PlaceAtLastStandQueue(app);
          continue;
        }
      }

      if (GlobalConfig.MaxContinuousBoostHours > 0 && GlobalConfig.BoostingMode is EBoostingMode.ContinuousBoosting) {
        if (app.ContinuousBoostingHours >= GlobalConfig.MaxContinuousBoostHours) {
          _ = BoostingApps.Remove(app.ID);
          AppManager.SetAppToSleep(app);
        }
      }
    }
  }

  private async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count) {
    List<AppBoostInfo> results = [];
    switch (BoostingState) {
      case EBoostingState.ArchiFarming:
        Game[] currentGamesFarming = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.ToArray();
        for (int index = 0; index < currentGamesFarming.Length && results.Count < count; index++) {
          uint appID = currentGamesFarming[index].AppID;
          if (!BoostingApps.ContainsKey(appID)) {
            AppBoostInfo? app = await AppManager.GetAppBoost(appID).ConfigureAwait(false);
            if (app != null) {
              results.Add(app);
            }
          }
        }
        break;
      case EBoostingState.ArchiPlayedWhileIdle:
        while (ArchiBoostableAppsPlayedWhileIdle.Count > 0 && results.Count < count) {
          uint appID = ArchiBoostableAppsPlayedWhileIdle.Dequeue();
          AppBoostInfo? app = await AppManager.GetAppBoost(appID).ConfigureAwait(false);
          if (app != null) {
            results.Add(app);
          }
        }
        break;
      case EBoostingState.BoosterPlayed:
        BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
        results = await AppManager.NextAppsForBoost(count, LastSessionNo).ConfigureAwait(false);
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

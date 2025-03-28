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

internal sealed class Booster {
  private enum EBoostingState : byte {
    None,
    ArchiFarming,
    ArchiPlayedWhileIdle,
    AutoBoosting
  }

  private static BoosterGlobalConfig GlobalConfig => AchievementsBoosterPlugin.GlobalConfig;

  internal BoosterHandler BoosterHandler => AppManager.BoosterHandler;

  private AppManager AppManager { get; }

  private readonly Bot Bot;
  private readonly BotCache Cache;
  private readonly Logger Logger;

  private EBoostingState BoostingState { get; set; } = EBoostingState.None;

  private Dictionary<uint, AppBoostInfo> BoostingApps { get; } = [];
  private Queue<uint> ArchiBoostableAppsPlayedWhileIdle { get; }

  private Timer? BoosterHeartBeatTimer { get; set; }
  private DateTime LastBoosterHeartBeatTime { get; set; }
  private DateTime LastUpdateOwnedGamesTime { get; set; }
  private SemaphoreSlim BoosterHeartBeatSemaphore { get; } = new SemaphoreSlim(1);

  private CancellationTokenSource CancellationTokenSource { get; set; } = new();

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
    CancellationTokenSource.Cancel();
    BoosterHeartBeatTimer?.Dispose();
    BoosterHeartBeatTimer = null;
  }

  internal Task OnSteamCallbacksInit(CallbackManager callbackManager) {
    ArgumentNullException.ThrowIfNull(callbackManager);
    BoosterHandler.Init();
    _ = callbackManager.Subscribe<SteamUser.PlayingSessionStateCallback>(OnPlayingSessionState);
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
    if (BoostingState == EBoostingState.AutoBoosting) {
      _ = Bot.Actions.Resume();
    }

    MarkBoostingAppsAsResting(); //TODO: Check, maybe just set BoostingState = None is DONE
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
    CancellationTokenSource = new CancellationTokenSource();

    try {
      BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);

      if (IsRestingTime(currentTime)) {
        throw new BoostingImpossibleException(Messages.RestTime);
      }

      await UpdateOwnedGames(currentTime, CancellationTokenSource.Token).ConfigureAwait(false);
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
          : EBoostingState.AutoBoosting;

      if (newBoostingState == BoostingState) {
        await UnlockAchievements(currentTime, CancellationTokenSource.Token).ConfigureAwait(false);
      }
      else {
        BoostingState = newBoostingState;
        MarkBoostingAppsAsResting();
      }

      // Add new apps for boosting if need
      if (BoostingApps.Count < GlobalConfig.MaxConcurrentlyBoostingApps) {
        List<AppBoostInfo> newApps = await FindNewAppsForBoosting(GlobalConfig.MaxConcurrentlyBoostingApps - BoostingApps.Count, CancellationTokenSource.Token).ConfigureAwait(false);
        newApps.ForEach(app => BoostingApps.TryAdd(app.ID, app));
      }

      if (BoostingApps.Count > 0) {
        if (BoostingState is EBoostingState.AutoBoosting) {
          BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
          (bool success, string message) = await Bot.Actions.Play(BoostingApps.Keys.ToList()).ConfigureAwait(false);
          if (!success) {
            throw new BoostingImpossibleException(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
          }
        }

        foreach (AppBoostInfo app in BoostingApps.Values) {
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
    catch (OperationCanceledException) {
      Logger.Warning($"The boosting process has been canceled: {CancellationTokenSource.Token.IsCancellationRequested}");
      MarkBoostingAppsAsResting();
      BoostingState = EBoostingState.None;
    }
    catch (Exception exception) {
      if (exception is BoostingImpossibleException) {
        Logger.Info(exception.Message);
      }
      else {
        Logger.Exception(exception);
      }

      MarkBoostingAppsAsResting();
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

  private void MarkBoostingAppsAsResting() {
    DateTime now = DateTime.Now;
    foreach (AppBoostInfo app in BoostingApps.Values) {
      AppManager.MarkAppAsResting(app, now);
    }

    BoostingApps.Clear();
  }

  private async Task UpdateOwnedGames(DateTime currentTime, CancellationToken cancellationToken) {
    if (AppManager.OwnedGames.Count == 0 || (currentTime - LastUpdateOwnedGamesTime).TotalHours > 12.0) {
      Dictionary<uint, string>? ownedGames = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false);
      if (ownedGames != null) {
        await AppManager.UpdateOwnedGames(ownedGames.Keys.ToHashSet(), cancellationToken).ConfigureAwait(false);
        LastUpdateOwnedGamesTime = currentTime;
      }
    }
  }

  private async Task UnlockAchievements(DateTime currentTime, CancellationToken cancellationToken) {
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

    int deltaTime = (int) (currentTime - LastBoosterHeartBeatTime).TotalMinutes;
    foreach (AppBoostInfo app in BoostingApps.Values.ToArray()) {
      cancellationToken.ThrowIfCancellationRequested();
      //BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
      app.BoostingDuration += deltaTime;

      (bool success, string message) = await app.UnlockNextAchievement(BoosterHandler, cancellationToken).ConfigureAwait(false);
      if (success) {
        Logger.Info(message);
        if (app.UnlockableAchievementsCount == 0) {
          _ = BoostingApps.Remove(app.ID);
          _ = Cache.PerfectGames.Add(app.ID);
          Logger.Info(string.Format(CultureInfo.CurrentCulture, app.RemainingAchievementsCount == 0 ? Messages.FinishedBoost : Messages.FinishedBoostable, app.FullName));
          continue;
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
          AppManager.MarkAppAsResting(app, DateTime.Now.AddHours(12));
          continue;
        }
      }

      if (GlobalConfig.BoostDurationPerApp > 0) {
        if (app.BoostingDuration >= GlobalConfig.BoostDurationPerApp) {
          _ = BoostingApps.Remove(app.ID);
          Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApp, app.FullName, app.BoostingDuration));
          AppManager.MarkAppAsResting(app);
        }
      }
    }
  }

  private async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = [];

    try {
      switch (BoostingState) {
        case EBoostingState.ArchiFarming:
          Game[] currentGamesFarming = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.ToArray();
          for (int index = 0; index < currentGamesFarming.Length && results.Count < count; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            uint appID = currentGamesFarming[index].AppID;
            if (!BoostingApps.ContainsKey(appID)) {
              AppBoostInfo? app = await AppManager.GetAppBoost(appID, cancellationToken).ConfigureAwait(false);
              if (app != null) {
                results.Add(app);
              }
            }
          }
          break;
        case EBoostingState.ArchiPlayedWhileIdle:
          while (ArchiBoostableAppsPlayedWhileIdle.Count > 0 && results.Count < count) {
            uint appID = ArchiBoostableAppsPlayedWhileIdle.Dequeue();
            try {
              cancellationToken.ThrowIfCancellationRequested();
              AppBoostInfo? app = await AppManager.GetAppBoost(appID, cancellationToken).ConfigureAwait(false);
              if (app != null) {
                results.Add(app);
              }
            }
            catch (OperationCanceledException) {
              ArchiBoostableAppsPlayedWhileIdle.Enqueue(appID);
              throw;
            }
          }
          break;
        case EBoostingState.AutoBoosting:
          //BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);//TODO:
          cancellationToken.ThrowIfCancellationRequested();
          results = await AppManager.NextAppsForBoost(count, cancellationToken).ConfigureAwait(false);
          break;
        case EBoostingState.None:
        // Not reachable
        default:
          break;
      }
    }
    catch (Exception) {
      if (results.Count > 0) {
        DateTime now = DateTime.Now;
        results.ForEach(app => AppManager.MarkAppAsResting(app, now));
      }
      throw;
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

  private static bool IsRestingTime(DateTime currentTime) {
    if (GlobalConfig.RestTimePerDay == 0) {
      return false;
    }

    DateTime weakUpTime = new(currentTime.Year, currentTime.Month, currentTime.Day, 6, 0, 0, 0);
    if (currentTime < weakUpTime) {
      DateTime restingStartTime = weakUpTime.AddMinutes(-GlobalConfig.RestTimePerDay);
      if (currentTime > restingStartTime) {
        return true;
      }
    }

    return false;
  }

  private void OnPlayingSessionState(SteamUser.PlayingSessionStateCallback callback) {
    ArgumentNullException.ThrowIfNull(callback);

    if (callback.PlayingBlocked) {
      CancellationTokenSource.Cancel();
    }
  }
}

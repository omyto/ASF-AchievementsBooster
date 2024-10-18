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
    BoosterHeartBeatTimer = new Timer(OnBoosterHeartBeat, null, Timeout.Infinite, Timeout.Infinite);

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

    Logger.Info("Achievements Booster Starting...");
    TimeSpan dueTime = command ? TimeSpan.Zero : TimeSpan.FromMinutes(Constants.AutoStartDelayTime);
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
      SetBoostingAppsToSleep();
      _ = Bot.Actions.Resume();
    }

    _ = BoosterHeartBeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
    BoostingState = EBoostingState.None;

    Logger.Info("Achievements Booster Stopped!");
    return Strings.Done;
  }

  private void SetBoostingAppsToSleep() {
    if (BoostingState is EBoostingState.BoosterPlayed) {
      foreach (BoostableApp app in BoostingApps.Values) {
        AppHandler.SetAppToSleep(app);
      }
    }
    BoostingApps.Clear();
  }

  private async void OnBoosterHeartBeat(object? state) {
    if (!BoosterHeartBeatSemaphore.Wait(0)) {
      Logger.Warning("OnBoosterHeartBeat already running !!!");
      return;
    }
    Logger.Trace("Booster heartbeating ...");

    try {
      DateTime currentTime = DateTime.Now;
      await Boostering(currentTime).ConfigureAwait(false);
      LastBoosterHeartBeatTime = currentTime;

      if (IsBoostingStarted) {
        // Due time for the next boosting
        TimeSpan dueTime = TimeSpan.FromMinutes(GlobalConfig.BoostTimeInterval) + TimeSpanUtils.RandomInMinutesRange(0, GlobalConfig.ExpandBoostTimeInterval);
        _ = BoosterHeartBeatTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
      }
    }
    finally {
      _ = BoosterHeartBeatSemaphore.Release();
    }
  }

  private async Task Boostering(DateTime currentTime) {
    if (!Bot.IsPlayingPossible || IsSleepingTime(currentTime)) {
      Logger.Info($"Bot not ready for play: {(!Bot.IsPlayingPossible ? "blocked" : "sleeping")}");
      if (BoostingApps.Count > 0) {
        SetBoostingAppsToSleep();
        BoostingState = EBoostingState.None;
        _ = Bot.Actions.Resume();
      }
      return;
    }

    await UpdateOwnedGames(currentTime).ConfigureAwait(false);
    if (AppHandler.OwnedGames.Count == 0) {
      Logger.Info("Bot don't have any games for boosting");
      return;
    }

    await BoostingAchievements(currentTime).ConfigureAwait(false);
  }

  private async Task UpdateOwnedGames(DateTime currentTime) {
    Dictionary<uint, string>? ownedGames = null;
    if (AppHandler.OwnedGames.Count == 0 || (currentTime - LastUpdateOwnedGamesTime).TotalHours > 6.0) {
      ownedGames = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false);
    }

    if (ownedGames != null) {
      LastUpdateOwnedGamesTime = currentTime;
    }
    AppHandler.Update(ownedGames);
  }

  private async Task BoostingAchievements(DateTime currentTime) {
    EBoostingState newBoostingState = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count > 0
      ? EBoostingState.ArchiFarming
      : Bot.BotConfig?.GamesPlayedWhileIdle.Count > 0
        ? EBoostingState.ArchiPlayedWhileIdle
        : EBoostingState.BoosterPlayed;

    if (newBoostingState == BoostingState) {
      if (BoostingState is EBoostingState.ArchiFarming) {
        IEnumerable<uint> boostingAppIDs = BoostingApps.Keys.Intersect(Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Select(e => e.AppID));
        List<uint> exceptAppIDs = BoostingApps.Keys.Except(boostingAppIDs).ToList();
        exceptAppIDs.ForEach(appID => BoostingApps.Remove(appID));
      }

      foreach (BoostableApp app in BoostingApps.Values) {
        (bool success, string message) = await app.UnlockNextAchievement(BoosterHandler).ConfigureAwait(false);
        if (success) {
          Logger.Info(message);
          if (app.RemainingAchievementsCount == 0) {
            _ = BoostingApps.Remove(app.ID);
            _ = Cache.PerfectGames.Add(app.ID);
            Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostingAppComplete, app.ID, app.Name));
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

      // Update boosting over hours
      if (GlobalConfig.MaxBoostingHours > 0) {
        TimeSpan deltaTime = currentTime - LastBoosterHeartBeatTime;

        foreach (uint appID in BoostingApps.Keys.ToList()) {
          BoostableApp app = BoostingApps[appID];
          app.LastPlayedTime = currentTime;
          app.ContinuousBoostingHours += deltaTime.TotalHours;

          if (app.ContinuousBoostingHours >= GlobalConfig.MaxBoostingHours) {
            _ = BoostingApps.Remove(appID);
            AppHandler.SetAppToSleep(app);
          }
        }
      }
    }
    else {
      BoostingState = newBoostingState;
      BoostingApps.Clear();
    }

    // Add new apps for boosting if need
    if (BoostingApps.Count < GlobalConfig.MaxBoostingApps) {
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
          Logger.Info(BoostingApps.Count == 0 ? Messages.NoBoostingAppsInArchiFarming : string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, string.Join(",", BoostingApps.Keys)));
          break;

        case EBoostingState.ArchiPlayedWhileIdle:
          while (ArchiBoostableAppsPlayedWhileIdle.Count > 0 && BoostingApps.Count < GlobalConfig.MaxBoostingApps) {
            uint appID = ArchiBoostableAppsPlayedWhileIdle.Dequeue();
            BoostableApp? app = await AppHandler.GetBoostableApp(appID).ConfigureAwait(false);
            if (app != null) {
              _ = BoostingApps.TryAdd(appID, app);
            }
          }
          Logger.Info(BoostingApps.Count == 0 ? Messages.NoBoostingAppsInArchiPlayedWhileIdle : string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, string.Join(",", BoostingApps.Keys)));
          break;

        case EBoostingState.BoosterPlayed:
          List<BoostableApp> apps = await AppHandler.NextBoosterApps(GlobalConfig.MaxBoostingApps - BoostingApps.Count).ConfigureAwait(false);
          apps.ForEach(app => BoostingApps.TryAdd(app.ID, app));
          if (BoostingApps.Count == 0) {
            _ = Bot.Actions.Resume();
            BoostingState = EBoostingState.None;
            Logger.Info(Messages.NoBoostingApps);
            break;
          }

          List<uint> playAppIDs = [.. BoostingApps.Keys];
          Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, string.Join(",", playAppIDs)));
          (bool success, string message) = await Bot.Actions.Play(playAppIDs).ConfigureAwait(false);
          if (!success) {
            SetBoostingAppsToSleep();
            BoostingState = EBoostingState.None;
            Logger.Warning($"Boosting apps failed! Reason: {message}");
          }
          break;

        case EBoostingState.None:
        default:
          break;
      }
    }
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

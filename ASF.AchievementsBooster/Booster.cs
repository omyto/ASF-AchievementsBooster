using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Stats;
using AchievementsBooster.Base;
using AchievementsBooster.Config;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;
using System.Globalization;
using AchievementsBooster.Extensions;

namespace AchievementsBooster;

internal sealed class Booster : IDisposable {
  internal readonly Bot Bot;
  internal readonly BoosterHandler BoosterHandler;
  internal readonly BoosterBotConfig Config = new();

  private bool IsBoostingStarted;
  private bool IsBoostingInProgress;
  private Timer? BoosterTimer;

  private readonly BotCache Cache;
  private Dictionary<uint, string> OwnedGames;
  private HashSet<uint> BoostableGames;

  // Boosting status
  private EBoostingState BoostingState = EBoostingState.None;
  private readonly Dictionary<uint, AppBooster> BoostingApps = [];

  internal Booster(Bot bot) {
    Bot = bot;
    BoosterHandler = new BoosterHandler(bot);
    IsBoostingStarted = false;
    IsBoostingInProgress = false;
    OwnedGames = [];
    BoostableGames = [];
    Cache = BotCache.LoadFromDatabase(bot) ?? new BotCache(bot);
    Cache.Init();
  }

  public void Dispose() => Stop();

  internal void Destroy() {
    Cache.Destroy();
    Dispose();
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

  internal async Task<string> Start(bool command = false) {
    if (IsBoostingStarted) {
      Bot.ArchiLogger.LogGenericWarning(Messages.BoostingStarted, Caller.Name());
      return Messages.BoostingStarted;
    }
    IsBoostingStarted = true;

    if (OwnedGames.Count == 0) {
      OwnedGames = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false) ?? [];
      BoostableGames = [.. OwnedGames.Keys];
      Bot.ArchiLogger.LogGenericTrace($"OwnedGames: {string.Join(",", OwnedGames.Keys)}", Caller.Name());
    }

    Bot.ArchiLogger.LogGenericInfo("Achievements Booster Starting...", Caller.Name());
    TimeSpan dueTime = command ? TimeSpan.Zero : TimeSpan.FromMinutes(Constants.AutoStartDelayTime);
    BoosterTimer = new Timer(OnBoosterTimer, null, dueTime, Timeout.InfiniteTimeSpan);

    return Strings.Done;
  }

  internal string Stop() {
    if (!IsBoostingStarted) {
      Bot.ArchiLogger.LogGenericWarning(Messages.BoostingNotStart, Caller.Name());
      return Messages.BoostingNotStart;
    }
    IsBoostingStarted = false;

    if (BoostingState == EBoostingState.BoosterPlayed) {
      _ = Bot.Actions.Play([]).ConfigureAwait(false);
    }
    BoostingState = EBoostingState.None;
    BoostingApps.Clear();
    BoosterTimer?.Dispose();
    Bot.ArchiLogger.LogGenericInfo("Achievements Booster Stopped!", Caller.Name());

    return Strings.Done;
  }

  private bool Ready() => OwnedGames.Count > 0 && Bot.IsConnectedAndLoggedOn && Bot.IsPlayingPossible;

  private async void OnBoosterTimer(object? state) {
    if (IsBoostingInProgress) {
      return;
    }

    IsBoostingInProgress = true;
    bool status = await BoostingAchievements().ConfigureAwait(false);
    if (status) {
      if (BoostingState is EBoostingState.ArchiFarming or EBoostingState.ArchiPlayedWhileIdle) {
        if (BoostingApps.Count > 0) {
          Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, string.Join(",", BoostingApps.Keys)), Caller.Name());
        } else {
          Bot.ArchiLogger.LogGenericInfo(BoostingState is EBoostingState.ArchiFarming ? Messages.NoBoostingAppsInArchiFarming : Messages.NoBoostingAppsInArchiPlayedWhileIdle, Caller.Name());
        }
      }
    } else {
      BoostingState = EBoostingState.None;
    }
    IsBoostingInProgress = false;

    // Calculate the delay time for the next boosting
    TimeSpan dueTime = TimeSpan.FromMinutes(AchievementsBooster.Config.BoostTimeInterval);
    if (AchievementsBooster.Config.ExpandBoostTimeInterval > 0) {
      dueTime += TimeSpanUtils.InMinutesRange(0, AchievementsBooster.Config.ExpandBoostTimeInterval);
    }
    _ = BoosterTimer?.Change(dueTime, Timeout.InfiniteTimeSpan);
  }

  private async Task<bool> BoostingAchievements() {
    Bot.ArchiLogger.LogGenericDebug("Boosting is in progress...", Caller.Name());
    if (!Ready()) {
      Bot.ArchiLogger.LogGenericWarning("Bot not ready!", Caller.Name());
      return false;
    }

    // Boosting achievements while farming cards
    FrozenSet<uint> farmingAppIDs = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Select(e => e.AppID).ToFrozenSet();
    if (farmingAppIDs.Count > 0) {
      if (BoostingState == EBoostingState.ArchiFarming) {
        List<uint> appsToRemove = [];
        // Check games and unlock next stat for each games
        foreach (uint appID in farmingAppIDs) {
          if (BoostingApps.TryGetValue(appID, out AppBooster? app)) {
            // Achieved next achievement
            (bool success, bool completed) = await BoosterHandler.UnlockNextStat(app).ConfigureAwait(false);
            if (success) {
              if (completed) {
                CompleteBoostingApp(app);
                _ = BoostingApps.Remove(appID);
              }
            } else {
              if (app.ShouldSkipBoosting()) {
                _ = BoostingApps.Remove(appID);
              }
            }
          } else {
            // Card farming scenario on a new app
            (bool boostable, app) = await GetAppForBoosting(appID).ConfigureAwait(false);
            if (boostable) {
              ArgumentNullException.ThrowIfNull(app);
              BoostingApps.Add(appID, app);
            } else {
              appsToRemove.Add(appID);
              _ = BoostableGames.Remove(appID);
            }
          }
        }

        // Remove games that are not being used to farm cards
        if (appsToRemove.Count > 0) {
          foreach (uint key in appsToRemove) {
            _ = BoostingApps.Remove(key);
          }
        }

        return true;
      }

      // Switch the state and initialize new games for boosting
      BoostingState = EBoostingState.ArchiFarming;
      bool hasAppForBoosting = await InitializeNewBoostingApps(farmingAppIDs).ConfigureAwait(false);
      if (!hasAppForBoosting) {
        Bot.ArchiLogger.LogGenericInfo(Messages.NoBoostingAppsInArchiFarming, Caller.Name());
      }
      return hasAppForBoosting;
    }

    // Boosting achievements while ilde
    FrozenSet<uint>? gamesPlayedWhileIdle = Bot.BotConfig?.GamesPlayedWhileIdle.Where(OwnedGames.ContainsKey).ToFrozenSet();
    if (gamesPlayedWhileIdle != null && gamesPlayedWhileIdle.Count > 0) {
      if (BoostingState == EBoostingState.ArchiPlayedWhileIdle) {
        // Since GamesPlayedWhileIdle may never change, just boost all apps in BoostingApps.
        List<uint> appsToRemove = [];
        foreach (AppBooster app in BoostingApps.Values) {
          (bool success, bool completed) = await BoosterHandler.UnlockNextStat(app).ConfigureAwait(false);
          if (success) {
            if (completed) {
              CompleteBoostingApp(app);
              appsToRemove.Add(app.ID);
            }
          } else {
            if (app.ShouldSkipBoosting()) {
              appsToRemove.Add(app.ID);
            }
          }
        }

        if (appsToRemove.Count > 0) {
          foreach (uint key in appsToRemove) {
            _ = BoostingApps.Remove(key);
          }
        }

        return true;
      }

      // Switch the state and initialize new games for boosting
      BoostingState = EBoostingState.ArchiPlayedWhileIdle;
      bool hasAppForBoosting = await InitializeNewBoostingApps(gamesPlayedWhileIdle).ConfigureAwait(false);
      if (!hasAppForBoosting) {
        Bot.ArchiLogger.LogGenericInfo(Messages.NoBoostingAppsInArchiPlayedWhileIdle, Caller.Name());
      }
      return hasAppForBoosting;
    }

    // Automatically play games and boost achievements
    if (BoostingState == EBoostingState.BoosterPlayed) {
      List<uint> appsToRemove = [];
      foreach (AppBooster app in BoostingApps.Values) {
        (bool success, bool completed) = await BoosterHandler.UnlockNextStat(app).ConfigureAwait(false);
        if (success) {
          if (completed) {
            CompleteBoostingApp(app);
            appsToRemove.Add(app.ID);
          }
        } else {
          if (app.ShouldSkipBoosting()) {
            appsToRemove.Add(app.ID);
          }
        }
      }

      if (appsToRemove.Count > 0) {
        foreach (uint key in appsToRemove) {
          _ = BoostingApps.Remove(key);
        }
      }

      if (BoostingApps.Count > 0) {
        return await PlayBoostingApps().ConfigureAwait(false);
      }
    }

    // Find new game to play and boost achievements
    BoostingApps.Clear();
    List<uint> gamesToRemove = [];
    foreach (uint appID in BoostableGames) {
      (bool boostable, AppBooster? app) = await GetAppForBoosting(appID).ConfigureAwait(false);
      if (boostable) {
        ArgumentNullException.ThrowIfNull(app);
        BoostingApps.Add(appID, app);
        break;
      }

      gamesToRemove.Add(appID);
    }

    if (gamesToRemove.Count > 0) {
      foreach (uint appID in gamesToRemove) {
        _ = BoostableGames.Remove(appID);
      }
    }

    if (BoostingApps.Count > 0) {
      BoostingState = EBoostingState.BoosterPlayed;
      return await PlayBoostingApps().ConfigureAwait(false);
    }

    BoostingState = EBoostingState.None;
    _ = await Bot.Actions.Play([]).ConfigureAwait(false);
    if (BoostableGames.Count == 0) {
      Bot.ArchiLogger.LogGenericInfo(Messages.NoBoostingApps, Caller.Name());
      _ = Stop();
    }

    return false;
  }

  private void CompleteBoostingApp(AppBooster app) {
    _ = Cache.PerfectGames.Add(app.ID);
    _ = BoostableGames.Remove(app.ID);
    Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.BoostingAppComplete, app.ID, app.Name), Caller.Name());
  }

  private async Task<bool> PlayBoostingApps() {
    List<uint> appIDs = [.. BoostingApps.Keys];
    Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, string.Join(",", appIDs)), Caller.Name());
    (bool success, string message) = await Bot.Actions.Play(appIDs).ConfigureAwait(false);
    if (!success) {
      Bot.ArchiLogger.LogGenericWarning($"Boosting apps failed! Reason: {message}", Caller.Name());
    }
    return success;
  }

  private async Task<bool> InitializeNewBoostingApps(FrozenSet<uint> appIDs) {
    BoostingApps.Clear();
    foreach (uint appID in appIDs) {
      (bool boostable, AppBooster? app) = await GetAppForBoosting(appID).ConfigureAwait(false);
      if (boostable) {
        ArgumentNullException.ThrowIfNull(app);
        BoostingApps.Add(appID, app);
      } else {
        _ = BoostableGames.Remove(appID);
      }
    }
    return BoostingApps.Count > 0;
  }

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  private bool IsBoostable(uint appID) {
    if (Cache.PerfectGames.Contains(appID)) {
      return false;
    }
    if (AchievementsBooster.GlobalCache.NonAchievementApps.Contains(appID)) {
      return false;
    }
    if (AchievementsBooster.GlobalCache.VACApps.Contains(appID)) {
      return false;
    }
    return true;
  }

  private async Task<(bool boostable, AppBooster? app)> GetAppForBoosting(uint appID) {
    if (!OwnedGames.ContainsKey(appID)) {
      // Oh God! Why?
      Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.NotOwnedGame, appID), Caller.Name());
      return (false, null);
    }

    if (ASF.GlobalConfig?.Blacklist.Contains(appID) == true) {
      Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Messages.AppInASFBlacklist, appID), Caller.Name());
      return (false, null);
    }

    if (!IsBoostable(appID)) {
      return (false, null);
    }

    ProductInfo? productInfo = await BoosterHandler.GetProductInfo(appID).ConfigureAwait(false);
    if (productInfo == null) {
      Bot.ArchiLogger.LogGenericWarning($"Can't get product info for app {appID}", Caller.Name());
      return (false, null);
    }

    if (!productInfo.IsPlayable()) {
      Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Messages.InvalidApp, appID), Caller.Name());
      return (false, null);
    }

    if (productInfo.IsVACEnabled) {
      _ = AchievementsBooster.GlobalCache.VACApps.Add(appID);
      Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Messages.VACEnabled, appID), Caller.Name());
      return (false, null);
    }

    if (productInfo.IsAchievementsEnabled.HasValue && !productInfo.IsAchievementsEnabled.Value) {
      _ = AchievementsBooster.GlobalCache.NonAchievementApps.Add(appID);
      Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Messages.AchievementsNotAvailable, appID), Caller.Name());
      return (false, null);
    }

    List<StatData>? statDatas = await BoosterHandler.GetStats(appID).ConfigureAwait(false);
    if (statDatas == null) {
      productInfo.IsAchievementsEnabled = false;
      _ = AchievementsBooster.GlobalCache.NonAchievementApps.Add(appID);
      return (false, null);
    }

    IEnumerable<StatData> unlockableStats = statDatas.Where(e => e.Unlockable());
    if (!unlockableStats.Any()) {
      _ = Cache.PerfectGames.Add(appID);
      Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, appID), Caller.Name());
      return (false, null);
    }

    FrozenDictionary<string, double>? percentages = await BoosterHandler.GetAppAchievementPercentages(appID).ConfigureAwait(false);
    if (percentages == null) {
      return (false, null);
    }

    AppBooster app = new(appID, productInfo, percentages);
    return (true, app);
  }
}

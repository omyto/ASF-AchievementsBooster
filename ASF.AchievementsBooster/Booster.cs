using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using AchievementsBooster.Extensions;
using AchievementsBooster.Stats;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

internal sealed class Booster : IDisposable {
  internal BoosterHandler BoosterHandler { get; }

  private readonly Bot Bot;
  private readonly BotCache Cache;

  private bool IsBoostingStarted;
  private bool IsBoostingInProgress;
  private Timer? BoosterTimer;

  private Dictionary<uint, string> OwnedGames;
  private HashSet<uint> BoostableGames;

  // Boosting status
  private EBoostingState BoostingState = EBoostingState.None;
  private Dictionary<uint, AppBooster> BoostingApps = [];

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

    _ = await BoostingAchievements().ConfigureAwait(false);

    IsBoostingInProgress = false;

    // Calculate the delay time for the next boosting
    TimeSpan dueTime = TimeSpan.FromMinutes(AchievementsBooster.Config.BoostTimeInterval);
    if (AchievementsBooster.Config.ExpandBoostTimeInterval > 0) {
      dueTime += TimeSpanUtils.InMinutesRange(0, AchievementsBooster.Config.ExpandBoostTimeInterval);
    }
    _ = BoosterTimer?.Change(dueTime, Timeout.InfiniteTimeSpan);
  }

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  private (EBoostingState newState, ImmutableList<uint> appsForBoosting) GetNewStateAndAppsForBoosting() {
    if (Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count > 0) {
      return (EBoostingState.ArchiFarming, Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Select(e => e.AppID).ToImmutableList());
    }

    if (Bot.BotConfig?.GamesPlayedWhileIdle.Count > 0) {
      return (EBoostingState.ArchiPlayedWhileIdle, Bot.BotConfig.GamesPlayedWhileIdle.Where(OwnedGames.ContainsKey).ToImmutableList());
    }

    return (EBoostingState.BoosterPlayed, BoostableGames.ToImmutableList());
  }

  private async Task<bool> BoostingAchievements() {
    Bot.ArchiLogger.LogGenericDebug("Boosting is in progress...", Caller.Name());
    if (!Ready()) {
      Bot.ArchiLogger.LogGenericWarning("Bot not ready!", Caller.Name());
      return false;
    }

    (EBoostingState newState, ImmutableList<uint> appsForBoosting) = GetNewStateAndAppsForBoosting();
    Dictionary<uint, AppBooster> keepBoostingApps = [];
    List<uint> toBeBoostedApps = [.. appsForBoosting];

    if (newState == BoostingState) {
      // Unlock achievement
      if (BoostingState is EBoostingState.ArchiPlayedWhileIdle or EBoostingState.BoosterPlayed) {
        // Since GamesPlayedWhileIdle may never change, just boost all apps in BoostingApps.
        foreach (AppBooster app in BoostingApps.Values) {
          bool keepBoosting = await UnlockAchievement(app).ConfigureAwait(false);
          if (keepBoosting) {
            keepBoostingApps.Add(app.ID, app);
          }
        }
      }
      else if (BoostingState is EBoostingState.ArchiFarming && BoostingApps.Count > 0) {
        toBeBoostedApps.Clear();
        foreach (uint appID in appsForBoosting) {
          if (BoostingApps.TryGetValue(appID, out AppBooster? app)) {
            bool keepBoosting = await UnlockAchievement(app).ConfigureAwait(false);
            if (keepBoosting) {
              keepBoostingApps.Add(app.ID, app);
            }
          }
          else {
            toBeBoostedApps.Add(appID);
          }
        }
      }
    }

    // Switch the state if difference
    BoostingState = newState;

    // Add new apps for boosting
    foreach (uint appID in toBeBoostedApps) {
      if (keepBoostingApps.Count >= AchievementsBooster.Config.MaxBoostingApps) {
        break;
      }

      if (keepBoostingApps.ContainsKey(appID)) {
        continue;
      }

      (bool boostable, AppBooster? app) = await GetAppForBoosting(appID).ConfigureAwait(false);
      if (boostable) {
        ArgumentNullException.ThrowIfNull(app);
        keepBoostingApps.Add(appID, app);
      }
      else {
        _ = BoostableGames.Remove(appID);
      }
    }

    // Assign new boosting apps list
    BoostingApps = keepBoostingApps;

    // Archi farming card or played games while idle
    if (BoostingState is EBoostingState.ArchiFarming or EBoostingState.ArchiPlayedWhileIdle) {
      bool isBoosting = BoostingApps.Count > 0;
      string message = isBoosting ? string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, string.Join(",", BoostingApps.Keys))
        : (BoostingState is EBoostingState.ArchiFarming ? Messages.NoBoostingAppsInArchiFarming : Messages.NoBoostingAppsInArchiPlayedWhileIdle);

      Bot.ArchiLogger.LogGenericInfo(message, Caller.Name());
      return isBoosting;
    }

    // Manual play & and boosting by AchievementsBooster
    if (BoostingApps.Count > 0) {
      List<uint> playAppIDs = [.. BoostingApps.Keys];
      Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApps, string.Join(",", playAppIDs)), Caller.Name());
      (bool success, string message) = await Bot.Actions.Play(playAppIDs).ConfigureAwait(false);
      if (!success) {
        BoostingState = EBoostingState.None;
        Bot.ArchiLogger.LogGenericWarning($"Boosting apps failed! Reason: {message}", Caller.Name());
        return false;
      }
      return true;
    }
    else {
      BoostingState = EBoostingState.None;
      _ = await Bot.Actions.Play([]).ConfigureAwait(false);
      return false;
    }
  }

  private async Task<bool> UnlockAchievement(AppBooster app) {
    _ = await BoosterHandler.UnlockNextStat(app).ConfigureAwait(false);

    if (app.IsCompletedBoosting) {
      _ = Cache.PerfectGames.Add(app.ID);
      _ = BoostableGames.Remove(app.ID);
      Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.BoostingAppComplete, app.ID, app.Name), Caller.Name());
      return false;
    }
    else if (!app.ShouldSkipBoosting()) {
      return true;
    }
    return false;
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

    if (AchievementsBooster.GlobalCache.NonAchievementApps.Contains(appID)) {
      return (false, null);
    }

    if (AchievementsBooster.Config.IgnoreAppWithVAC && AchievementsBooster.GlobalCache.VACApps.Contains(appID)) {
      return (false, null);
    }

    if (Cache.PerfectGames.Contains(appID)) {
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
      if (AchievementsBooster.Config.IgnoreAppWithVAC) {
        Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Messages.VACEnabled, appID), Caller.Name());
        return (false, null);
      }
    }

    if (AchievementsBooster.Config.IgnoreAppWithDLC && productInfo.DLCs.Count > 0) {
      //TODO: Cache ?
      return (false, null);
    }

    if (AchievementsBooster.Config.IgnoreDevelopers.Count > 0) {
      foreach (string developer in AchievementsBooster.Config.IgnoreDevelopers) {
        if (productInfo.Developers.Contains(developer)) {
          return (false, null);
        }
      }
    }

    if (AchievementsBooster.Config.IgnorePublishers.Count > 0) {
      foreach (string publisher in AchievementsBooster.Config.IgnorePublishers) {
        if (productInfo.Publishers.Contains(publisher)) {
          return (false, null);
        }
      }
    }

    if (productInfo.IsAchievementsEnabled.HasValue && !productInfo.IsAchievementsEnabled.Value) {
      _ = AchievementsBooster.GlobalCache.NonAchievementApps.Add(appID);
      return (false, null);
    }

    List<StatData>? statDatas = await BoosterHandler.GetStats(appID).ConfigureAwait(false);
    if (statDatas == null || statDatas.Count == 0) {
      productInfo.IsAchievementsEnabled = false;
      _ = AchievementsBooster.GlobalCache.NonAchievementApps.Add(appID);
      Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Messages.AchievementsNotAvailable, appID), Caller.Name());
      return (false, null);
    }

    List<StatData> unlockableStats = statDatas.Where(e => e.Unlockable()).ToList();
    if (unlockableStats.Count == 0) {
      _ = Cache.PerfectGames.Add(appID);
      Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, appID), Caller.Name());
      return (false, null);
    }

    FrozenDictionary<string, double>? percentages = await BoosterHandler.GetAppAchievementPercentages(appID).ConfigureAwait(false);
    if (percentages == null) {
      return (false, null);
    }

    AppBooster app = new(appID, productInfo, percentages, unlockableStats);
    return (true, app);
  }
}

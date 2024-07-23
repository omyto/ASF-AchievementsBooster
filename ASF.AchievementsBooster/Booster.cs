using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Stats;
using AchievementsBooster.App;
using AchievementsBooster.Base;
using AchievementsBooster.Config;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

internal sealed class Booster : IDisposable {
  private readonly TimeSpan BoosterTimerDueTime = TimeSpan.FromSeconds(30);
  private readonly TimeSpan BoosterTimerPeriod = TimeSpan.FromMinutes(5);

  internal readonly Bot Bot;
  internal readonly UserStatsManager StatsManager;
  internal readonly BoosterBotConfig Config = new();

  private bool IsBoostingStarted;
  private bool IsBoostingInProgress;
  private Timer? BoosterTimer;

  private Dictionary<uint, string> OwnedGames;
  private HashSet<uint> BoostableGames;
  private readonly HashSet<uint> PerfectGames;
  private readonly HashSet<uint> NoStatsGames;

  // Boosting status
  private EBoostingState BoostingState = EBoostingState.None;
  private readonly Dictionary<uint, SteamApp> BoostingApps = [];

  internal Booster(Bot bot) {
    Bot = bot;
    StatsManager = new UserStatsManager(bot);
    IsBoostingStarted = false;
    IsBoostingInProgress = false;
    OwnedGames = [];
    BoostableGames = [];
    PerfectGames = [];
    NoStatsGames = [];
  }

  public void Dispose() => Stop();

  internal void OnSteamCallbacksInit(CallbackManager callbackManager) {
    ArgumentNullException.ThrowIfNull(callbackManager);
    _ = callbackManager.Subscribe<SteamUser.PlayingSessionStateCallback>(OnPlayingSessionStateCallback);
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
  internal Task OnInitModules(IReadOnlyDictionary<string, JsonElement> additionalConfigProperties) {
    if (additionalConfigProperties != null) {
    }
    return Task.CompletedTask;
  }

  internal async Task<string> Start() {
    if (IsBoostingStarted) {
      string message = "This booster has already started!";
      Bot.ArchiLogger.LogGenericWarning(message);
      return message;
    }
    IsBoostingStarted = true;

    if (OwnedGames.Count == 0) {
      OwnedGames = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false) ?? [];
      BoostableGames = [.. OwnedGames.Keys];
      Bot.ArchiLogger.LogGenericTrace($"OwnedGames: {string.Join(",", OwnedGames.Keys)}");
    }

    Bot.ArchiLogger.LogGenericInfo("Achievements Booster Starting...");
    BoosterTimer = new Timer(OnBoosterTimer, null, BoosterTimerDueTime, BoosterTimerPeriod);

    return Strings.Done;
  }

  internal string Stop() {
    if (!IsBoostingStarted) {
      string message = "This booster is currently not running!";
      Bot.ArchiLogger.LogGenericWarning(message);
      return message;
    }
    IsBoostingStarted = false;

    if (BoostingState == EBoostingState.BoosterPlayed) {
      _ = Bot.Actions.Play([]).ConfigureAwait(false);
    }
    BoostingState = EBoostingState.None;
    BoostingApps.Clear();
    BoosterTimer?.Dispose();
    Bot.ArchiLogger.LogGenericInfo("Achievements Booster Stopped!");

    return Strings.Done;
  }

  internal async Task<string> Log(ulong appID) {
    (TaskResult result, _) = await StatsManager.GetStats(appID).ConfigureAwait(false);
    return result.Success ? Strings.Done : result.Message;
  }

  internal async Task<string> UnlockNext(ulong appID) {
    (TaskResult result, _) = await StatsManager.UnlockNextStat(appID).ConfigureAwait(false);
    return result.Success ? Strings.Done : result.Message;
  }

  private bool Ready() => OwnedGames.Count > 0 && Bot.IsConnectedAndLoggedOn && Bot.IsPlayingPossible;

  private async void OnBoosterTimer(object? state) {
    if (IsBoostingInProgress) {
      return;
    }

    IsBoostingInProgress = true;
    bool status = await BoostingAchievements().ConfigureAwait(false);
    if (!status) {
      BoostingState = EBoostingState.None;
    }
    IsBoostingInProgress = false;
  }

  private async Task<bool> BoostingAchievements() {
    Bot.ArchiLogger.LogGenericDebug("Boosting ...");
    if (!Ready()) {
      Bot.ArchiLogger.LogGenericWarning("Bot not ready!");
      return false;
    }

    // Boosting achievements while farming cards
    FrozenSet<uint> farmingAppIDs = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Select(e => e.AppID).ToFrozenSet();
    if (farmingAppIDs.Count > 0) {
      if (BoostingState == EBoostingState.ArchiFarming) {
        List<uint> appsToRemove = [];
        // Check games and unlock next stat for each games
        foreach (uint appID in farmingAppIDs) {
          if (BoostingApps.TryGetValue(appID, out SteamApp? app)) {
            // Achieved next achievement
            (TaskResult result, int count) = await StatsManager.UnlockNextStat(appID).ConfigureAwait(false);
            if (result.Success) {
              if (count == 0) {
                CompleteBoostingApp(app);
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
        Bot.ArchiLogger.LogGenericInfo("There are no apps available to boost achievements during the card farming process");
      }
      return hasAppForBoosting;
    }

    // Boosting achievements while ilde
    FrozenSet<uint>? gamesPlayedWhileIdle = Bot.BotConfig?.GamesPlayedWhileIdle.Where(OwnedGames.ContainsKey).ToFrozenSet();
    if (gamesPlayedWhileIdle != null && gamesPlayedWhileIdle.Count > 0) {
      if (BoostingState == EBoostingState.ArchiPlayedWhileIdle) {
        // Since GamesPlayedWhileIdle may never change, just boost all apps in BoostingApps.
        List<uint> appsToRemove = [];
        foreach (SteamApp app in BoostingApps.Values) {
          (TaskResult result, int count) = await StatsManager.UnlockNextStat(app.ID).ConfigureAwait(false);
          if (result.Success && count == 0) {
            CompleteBoostingApp(app);
            appsToRemove.Add(app.ID);
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
        Bot.ArchiLogger.LogGenericInfo("There are no apps available to boost achievements while playing in idle mode");
      }
      return hasAppForBoosting;
    }

    // Automatically play games and boost achievements
    if (BoostingState == EBoostingState.BoosterPlayed) {
      List<uint> appsToRemove = [];
      foreach (SteamApp app in BoostingApps.Values) {
        (TaskResult result, int count) = await StatsManager.UnlockNextStat(app.ID).ConfigureAwait(false);
        if (result.Success && count == 0) {
          CompleteBoostingApp(app);
          appsToRemove.Add(app.ID);
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
      (bool boostable, SteamApp? app) = await GetAppForBoosting(appID).ConfigureAwait(false);
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
      Bot.ArchiLogger.LogGenericInfo("There are no apps available to boost achievements");
      _ = Stop();
    }

    return false;
  }

  private void CompleteBoostingApp(SteamApp app) {
    _ = PerfectGames.Add(app.ID);
    _ = BoostableGames.Remove(app.ID);
    Bot.ArchiLogger.LogGenericInfo($"{app.Name}({app.ID}): Completed boosting achievements!");
  }

  private async Task<bool> PlayBoostingApps() {
    List<uint> appIDs = [.. BoostingApps.Keys];
    (bool success, string message) = await Bot.Actions.Play(appIDs).ConfigureAwait(false);
    if (success) {
      Bot.ArchiLogger.LogGenericInfo($"Boosting apps: {string.Join(",", appIDs)}");
    } else {
      Bot.ArchiLogger.LogGenericWarning($"Boosting apps: {string.Join(",", appIDs)} failed! Reason: {message}");
    }
    return success;
  }

  private async Task<bool> InitializeNewBoostingApps(FrozenSet<uint> appIDs) {
    BoostingApps.Clear();
    foreach (uint appID in appIDs) {
      (bool boostable, SteamApp? app) = await GetAppForBoosting(appID).ConfigureAwait(false);
      if (boostable) {
        ArgumentNullException.ThrowIfNull(app);
        BoostingApps.Add(appID, app);
      } else {
        _ = BoostableGames.Remove(appID);
      }
    }
    return BoostingApps.Count > 0;
  }

  private async Task<(bool boostable, SteamApp? app)> GetAppForBoosting(uint appID) {
    if (!OwnedGames.ContainsKey(appID)) {
      // Oh God! Why?
      Bot.ArchiLogger.LogGenericWarning($"Game {appID} is not owned");
      return (false, null);
    }

    if (ASF.GlobalConfig?.Blacklist.Contains(appID) == true) {
      Bot.ArchiLogger.LogGenericWarning($"The game {appID} is in the ASF blacklist configuration");
      return (false, null);
    }

    if (PerfectGames.Contains(appID) || NoStatsGames.Contains(appID)) {
      return (false, null);
    }

    SteamApp app = await AppUtils.GetApp(appID).ConfigureAwait(false);
    if (!app.IsValid) {
      Bot.ArchiLogger.LogGenericDebug($"{app.Name}({appID}): Wrong app type!");
      return (false, app);
    }

    if (!app.HasAchievements()) {
      _ = NoStatsGames.Add(appID);
      Bot.ArchiLogger.LogGenericDebug($"{app.Name}({appID}): Not including achievements!");
      return (false, app);
    }

    if (app.HasVAC()) {
      Bot.ArchiLogger.LogGenericDebug($"{app.Name}({appID}): Valve Anti-Cheat enabled!");
      return (false, app);
    }

    (TaskResult result, List<StatData> statDatas) = await StatsManager.GetStats(appID).ConfigureAwait(false);
    if (!result.Success) {
      // Unreachable
      _ = NoStatsGames.Add(appID);
      Bot.ArchiLogger.LogGenericWarning($"{app.Name}({appID}): No user stats found!");
      return (false, app);
    }

    IEnumerable<StatData> unlockableStats = statDatas.Where(e => e.Unlockable());
    if (!unlockableStats.Any()) {
      _ = PerfectGames.Add(appID);
      Bot.ArchiLogger.LogGenericDebug($"{app.Name}({appID}): Perfectly completed!");
      return (false, app);
    }

    //bool success = BoostingApps.TryAdd(appID, app);
    //if (success) {
    //  Bot.ArchiLogger.LogGenericInfo($"{app.Name}({appID}): Starting boosting ...");
    //} else {
    //  Bot.ArchiLogger.LogGenericWarning($"{app.Name}({appID}): Error happened when starting boosting!");
    //}

    return (true, app);
  }

  /* Callbacks */

  private void OnPlayingSessionStateCallback(SteamUser.PlayingSessionStateCallback callback) {
    ArgumentNullException.ThrowIfNull(callback);
    Bot.ArchiLogger.LogGenericDebug($"OnPlayingSessionState | PlayingBlocked: {callback.PlayingBlocked}, AppID: {callback.PlayingAppID}");
  }
}

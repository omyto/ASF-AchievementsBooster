using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Engine;

internal sealed class AutoBoostingEngine : BoostingEngineBase {

  private bool HasTriggeredPlay { get; set; }

  private Queue<uint> WaitingBoostApps { get; set; } = new();

  private Dictionary<uint, AppBoostInfo> RestingBoostApps { get; } = [];

  internal AutoBoostingEngine(Booster booster) : base(EBoostMode.AutoBoost, booster) {
  }

  internal override Task Update() {
    if (Booster.AppRepository.IsOwnedGamesUpdated || WaitingBoostApps.Count == 0) {
      WaitingBoostApps.Clear();

      // Remove apps that are no longer owned or filtered
      RestingBoostApps.Keys.Except(Booster.AppRepository.FilteredGames).ToList().ForEach(appID => RestingBoostApps.Remove(appID));
      CurrentBoostingApps.Keys.Except(Booster.AppRepository.FilteredGames).ToList().ForEach(appID => CurrentBoostingApps.Remove(appID));

      foreach (uint appID in Booster.AppRepository.FilteredGames) {
        if (CurrentBoostingApps.ContainsKey(appID) || RestingBoostApps.ContainsKey(appID)) {
          continue;
        }

        if (Booster.AppRepository.IsBoostableApp(appID, false)) {
          WaitingBoostApps.Enqueue(appID);
        }
      }

      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostableQueue,
        $"{string.Join(",", WaitingBoostApps.Take(50))}{(WaitingBoostApps.Count > 50 ? ", ..." : ".")}"));
    }

    return Task.CompletedTask;
  }

  internal override void Stop(bool resume = false) {
    base.Stop(resume);
    if (resume && HasTriggeredPlay) {
      _ = Booster.Bot.Actions.Resume();
      HasTriggeredPlay = false;
    }
  }

  protected override Task PostAchieve(AppBoostInfo app) => Resting(app);

  private Task Resting(AppBoostInfo app) {
    if (Booster.Config.BoostDurationPerApp > 0 && app.BoostingDuration >= Booster.Config.BoostDurationPerApp) {
      _ = CurrentBoostingApps.Remove(app.ID);
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApp, app.FullName, app.BoostingDuration));
      MarkAppAsResting(app);
    }
    return Task.CompletedTask;
  }

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = GetRestedAppsReadyForBoost(count);

    try {
      // Get from boostable queue
      while (WaitingBoostApps.Count > 0 && results.Count < count) {
        cancellationToken.ThrowIfCancellationRequested();
        uint appID = WaitingBoostApps.Dequeue();

        AppBoostInfo? app = await Booster.AppRepository.GetBoostableApp(appID, cancellationToken).ConfigureAwait(false);
        if (app != null) {
          results.Add(app);
        }
      }
    }
    catch (Exception) {
      if (results.Count > 0) {
        DateTime now = DateTime.Now;
        results.ForEach(app => MarkAppAsResting(app, now));
      }
      throw;
    }

    return results;
  }

  protected override async Task FinalizeFill(bool isBoostingAppsChanged, CancellationToken cancellationToken) {
    if (CurrentBoostingApps.Count > 0) {
      if (!await PlayBoostingApps(cancellationToken).ConfigureAwait(false)) {
        DateTime restEndTime = DateTime.Now.AddHours(1);
        foreach (AppBoostInfo app in CurrentBoostingApps.Values) {
          MarkAppAsResting(app, restEndTime);
        }
        CurrentBoostingApps.Clear();
      }
    }
    else {
      if (Booster.Config.BoostHoursWhenIdle) {
        await FallBackToIdleGaming(cancellationToken).ConfigureAwait(false);
      }
    }
  }

  private async Task<bool> PlayBoostingApps(CancellationToken cancellationToken) {
    cancellationToken.ThrowIfCancellationRequested();
    (bool success, string message) = await Booster.Bot.Actions.Play(CurrentBoostingApps.Keys.ToList()).ConfigureAwait(false);
    if (!success) {
      Booster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
    }

    return HasTriggeredPlay = success;
  }

  private async Task FallBackToIdleGaming(CancellationToken cancellationToken) {
    await HoursBooster.Instance.Update(Booster, cancellationToken).ConfigureAwait(false);

    if (HoursBooster.Instance.ReadyToBoostGames.Count > 0) {
      (bool success, string message) = await Booster.Bot.Actions.Play(HoursBooster.Instance.ReadyToBoostGames).ConfigureAwait(false);
      if (success) {
        HasTriggeredPlay = true;
        Booster.Logger.Info($"Boosting hours {HoursBooster.Instance.ReadyToBoostGames.Count} game(s): {string.Join(",", HoursBooster.Instance.ReadyToBoostGames)}");
      }
      else {
        Booster.Logger.Warning($"Boosting hours failed; reason: {message}");
      }
    }
  }

  protected override void Notify(TimeSpan timeRemaining) {
    if (CurrentBoostingApps.Count > 0) {
      base.Notify(timeRemaining);
    }
    else {
      Booster.Logger.Info($"No apps are available to boost achievements. Recheck after: {timeRemaining.ToHumanReadable()} ({NextAchieveTime.ToShortTimeString()})");
    }
  }

  private void MarkAppAsResting(AppBoostInfo app, DateTime? restingEndTime = null) {
    app.BoostingDuration = 0;
    app.RestingEndTime = restingEndTime ?? DateTime.Now.AddMinutes(Booster.Config.BoostRestTimePerApp);

    if (!RestingBoostApps.TryAdd(app.ID, app)) {
      Booster.Logger.Warning($"App {app.FullName} already resting");
    }
  }

  private List<AppBoostInfo> GetRestedAppsReadyForBoost(int max) {
    List<AppBoostInfo> results = [];
    DateTime now = DateTime.Now;

    foreach (AppBoostInfo app in RestingBoostApps.Values.ToList()) {
      if (now > app.RestingEndTime) {
        results.Add(app);
        _ = RestingBoostApps.Remove(app.ID);
        Booster.Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, app.FullName, app.UnlockableAchievementsCount));

        if (results.Count >= max) {
          break;
        }
      }
    }

    return results;
  }
}

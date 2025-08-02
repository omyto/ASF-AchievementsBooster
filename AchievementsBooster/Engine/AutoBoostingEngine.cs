using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Engine;

internal sealed class AutoBoostingEngine : BoostingEngineBase {

  private bool HasTriggeredPlay { get; set; }

  private Queue<uint> WaitingBoostApps { get; set; } = new();

  internal AutoBoostingEngine(Booster booster) : base(EBoostMode.AutoBoost, booster) {
  }

  internal override Task Update() {
    if (Booster.AppRepository.IsOwnedGamesUpdated || WaitingBoostApps.Count == 0) {
      WaitingBoostApps.Clear();

      foreach (uint appID in Booster.AppRepository.FilteredGames) {
        if (Booster.AppRepository.IsBoostableApp(appID, true)) {
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

  protected override bool ShouldRestingApp(AppBoostInfo app)
    => BoosterConfig.Global.BoostDurationPerApp > 0 && app.BoostingDuration >= BoosterConfig.Global.BoostDurationPerApp;

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = Booster.AppRepository.GetRestedAppsReadyForBoost(count);

    try {
      // Get from boostable queue
      while (WaitingBoostApps.Count > 0 && results.Count < count) {
        cancellationToken.ThrowIfCancellationRequested();
        uint appID = WaitingBoostApps.Dequeue();

        AppBoostInfo? app = await Booster.AppRepository.GetBoostableApp(appID, cancellationToken, false, false).ConfigureAwait(false);
        if (app != null) {
          results.Add(app);
        }
      }
    }
    catch (Exception) {
      if (results.Count > 0) {
        DateTime now = DateTime.Now;
        results.ForEach(app => Booster.AppRepository.MarkAppAsResting(app, now));
      }
      throw;
    }

    return results;
  }

  protected override async Task<bool> PlayBoostingApps(CancellationToken cancellationToken) {
    cancellationToken.ThrowIfCancellationRequested();
    (bool success, string message) = await Booster.Bot.Actions.Play(CurrentBoostingApps.Keys.ToList()).ConfigureAwait(false);
    if (!success) {
      Booster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
    }

    return HasTriggeredPlay = success;
  }

  protected override async Task FallBackToIdleGaming(CancellationToken cancellationToken) {
    await HoursBooster.Instance.Update(Booster.AppRepository, cancellationToken).ConfigureAwait(false);

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
}

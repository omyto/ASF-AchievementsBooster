using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using AchievementsBooster.Storage;

namespace AchievementsBooster.Engine;

internal sealed class AutoBoostingEngine : BoostEngine {

  private bool HasTriggeredPlay { get; set; }

  internal AutoBoostingEngine(Booster booster) : base(EBoostMode.AutoBoost, booster) {
  }

  protected override void ResumePlay() {
    if (HasTriggeredPlay) {
      _ = Booster.ResumePlay();
      HasTriggeredPlay = false;
    }
  }

  protected override AppBoostInfo[] GetReadyToUnlockApps()
    => CurrentBoostingApps.Values.ToArray();

  protected override bool ShouldRestingApp(AppBoostInfo app)
    => BoosterConfig.Global.BoostDurationPerApp > 0 && app.BoostingDuration >= BoosterConfig.Global.BoostDurationPerApp;

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    cancellationToken.ThrowIfCancellationRequested();
    return await Booster.AppManager.NextAppsForBoost(count, cancellationToken).ConfigureAwait(false);
  }

  protected override async Task<bool> PlayCurrentBoostingApps(CancellationToken cancellationToken) {
    cancellationToken.ThrowIfCancellationRequested();
    (bool success, string message) = await Booster.PlayGames(CurrentBoostingApps.Keys.ToList()).ConfigureAwait(false);
    if (!success) {
      Booster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
    }

    return HasTriggeredPlay = success;
  }

  protected override string GetNoBoostingAppsMessage() => Messages.NoBoostingApps;

  protected override async Task FallBackToIdleGaming(CancellationToken cancellationToken) {
    await HoursBooster.Instance.Update(Booster.AppManager, cancellationToken).ConfigureAwait(false);

    if (HoursBooster.Instance.ReadyToBoostGames.Count > 0) {
      (bool success, string message) = await Booster.PlayGames(HoursBooster.Instance.ReadyToBoostGames).ConfigureAwait(false);
      if (success) {
        HasTriggeredPlay = true;
        Booster.Logger.Info($"Boosting hours {HoursBooster.Instance.ReadyToBoostGames.Count} game(s): {string.Join(",", HoursBooster.Instance.ReadyToBoostGames)}");
      }
      else {
        Booster.Logger.Warning($"Boosting hours failed; reason: {message}");
      }
    }
  }
}

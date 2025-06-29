using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;

namespace AchievementsBooster.Booster;

internal sealed class AutoBooster : Booster {

  private bool HasTriggeredPlay { get; set; }

  internal AutoBooster(BoosterBot bot) : base(EBoostMode.AutoBoost, bot) {
  }

  protected override void ResumePlay() {
    if (HasTriggeredPlay) {
      _ = Bot.ResumePlay();
      HasTriggeredPlay = false;
    }
  }

  protected override AppBoostInfo[] GetReadyToUnlockApps() => CurrentBoostingApps.Values.ToArray();

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    cancellationToken.ThrowIfCancellationRequested();
    return await AppManager.NextAppsForBoost(count, cancellationToken).ConfigureAwait(false);
  }

  protected override async Task<bool> PlayCurrentBoostingApps(CancellationToken cancellationToken) {
    cancellationToken.ThrowIfCancellationRequested();
    (bool success, string message) = await Bot.PlayGames(CurrentBoostingApps.Keys.ToList()).ConfigureAwait(false);
    if (!success) {
      Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
    }

    return HasTriggeredPlay = success;
  }

  protected override string GetNoBoostingAppsMessage() => Messages.NoBoostingApps;

}

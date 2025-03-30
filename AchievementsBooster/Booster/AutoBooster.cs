using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;

namespace AchievementsBooster.Booster;

internal sealed class AutoBooster : BaseBooster {

  private bool HasTriggeredPlay { get; set; }

  internal AutoBooster(BoosterBot bot) : base(EBoostMode.AutoBoost, bot) {
  }

  internal override void ResumePlay() {
    if (HasTriggeredPlay) {
      _ = Bot.ResumePlay();
    }
  }

  protected override AppBoostInfo[] GetReadyToUnlockApps() => CurrentBoostingApps.Values.ToArray();

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    cancellationToken.ThrowIfCancellationRequested();
    return await AppManager.NextAppsForBoost(count, cancellationToken).ConfigureAwait(false);
  }

  protected override async Task PlayCurrentBoostingApps() {
    BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
    (bool success, string message) = await Bot.PlayGames(CurrentBoostingApps.Keys.ToList()).ConfigureAwait(false);
    if (!success) {
      throw new BoostingImpossibleException(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
    }
    HasTriggeredPlay = true;
  }

  protected override void LogNoneAppsForBoosting() =>
    // BoostingState is EBoostingState.BoosterPlayed
    throw new BoostingImpossibleException(Messages.NoBoostingApps);

}

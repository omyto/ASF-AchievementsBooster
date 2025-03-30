using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Steam;

namespace AchievementsBooster.Booster;

internal sealed class AutoBooster : BaseBooster {

  private bool HasTriggeredPlay { get; set; }

  [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "<Pending>")]
  public AutoBooster(Bot bot, BotCache cache, AppManager appManager) : base(EBoostMode.AutoBoost, bot, cache, appManager) {
  }

  internal override void ResumePlay() {
    if (HasTriggeredPlay) {
      _ = Bot.Actions.Resume();
    }
  }

  protected override AppBoostInfo[] GetReadyToUnlockApps() => CurrentBoostingApps.Values.ToArray();

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    cancellationToken.ThrowIfCancellationRequested();
    return await AppManager.NextAppsForBoost(count, cancellationToken).ConfigureAwait(false);
  }

  protected override async Task PlayCurrentBoostingApps() {
    BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
    (bool success, string message) = await Bot.Actions.Play(CurrentBoostingApps.Keys.ToList()).ConfigureAwait(false);
    if (!success) {
      throw new BoostingImpossibleException(string.Format(CultureInfo.CurrentCulture, Messages.BoostingFailed, message));
    }
    HasTriggeredPlay = true;
  }

  protected override void LogNoneAppsForBoosting() =>
    // BoostingState is EBoostingState.BoosterPlayed
    throw new BoostingImpossibleException(Messages.NoBoostingApps);

}

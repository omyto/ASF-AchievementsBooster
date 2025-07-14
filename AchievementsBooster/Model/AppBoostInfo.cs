
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Handler;
using AchievementsBooster.Handler.Callback;
using AchievementsBooster.Helper;

namespace AchievementsBooster.Model;

public sealed class AppBoostInfo {
  public uint ID { get; }

  public string Name => ProductInfo.Name;

  public string FullName => ProductInfo.FullName;

  internal DateTime RestingEndTime { get; set; }

  internal int BoostingDuration { get; set; }

  internal int RemainingAchievementsCount { get; private set; }

  internal int UnlockableAchievementsCount { get; private set; }

  internal int FailedUnlockCount { get; private set; }

  private ProductInfo ProductInfo { get; }

  private AchievementRates AchievementRates { get; }

  private StatData? FailedUnlockStat { get; set; }

  internal AppBoostInfo(uint id, ProductInfo product, AchievementRates rates, int remainingAchievementsCount, int unlockableAchievementsCount) {
    ID = id;
    ProductInfo = product;
    AchievementRates = rates;
    RemainingAchievementsCount = remainingAchievementsCount;
    UnlockableAchievementsCount = unlockableAchievementsCount;
  }

  internal async Task<(bool, string)> UnlockNextAchievement(SteamClientHandler clientHandler, CancellationToken cancellationToken) {
    UserStatsResponse? response = await clientHandler.GetStats(ID, cancellationToken).ConfigureAwait(false);
    if (response == null) {
      // Not reachable
      return (false, string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, FullName));
    }

    // Find next un-achieved achievement
    List<StatData> remainingAchievements = response.StatDatas.Where(e => !e.IsSet).ToList();
    RemainingAchievementsCount = remainingAchievements.Count;

    List<StatData> unlockableAchievements = remainingAchievements.Where(e => !e.Restricted).ToList();
    UnlockableAchievementsCount = unlockableAchievements.Count;

    if (UnlockableAchievementsCount == 0) {
      return (true, string.Format(CultureInfo.CurrentCulture, RemainingAchievementsCount == 0 ? Messages.PerfectApp : Messages.NoUnlockableStats, FullName));
    }

    foreach (StatData statData in unlockableAchievements) {
      statData.Percentage = AchievementRates.GetAchievementRate(statData.APIName, 0);
    }
    unlockableAchievements.Sort((x, y) => y.Percentage.CompareTo(x.Percentage));

    StatData nextStat = unlockableAchievements.First();

    // Achieve next achievement
    if (await clientHandler.UnlockStat(ID, nextStat, response.CrcStats).ConfigureAwait(false)) {
      RemainingAchievementsCount--;
      UnlockableAchievementsCount--;
      return (true, string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementSuccess, FullName, nextStat.Name));
    }
    else {
      if (nextStat.APIName.Equals(FailedUnlockStat?.APIName, StringComparison.Ordinal)) {
        FailedUnlockCount++;
      }
      else {
        FailedUnlockStat = nextStat;
        FailedUnlockCount = 1;
      }
      return (false, string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementFailed, FullName, nextStat.Name));
    }
  }
}

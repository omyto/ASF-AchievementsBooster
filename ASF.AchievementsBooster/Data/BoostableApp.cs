
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AchievementsBooster.Handler;
using AchievementsBooster.Handler.Callback;
using AchievementsBooster.Helpers;

namespace AchievementsBooster.Data;

public sealed class BoostableApp {
  public uint ID { get; }

  public string Name => ProductInfo.Name;

  public bool IsReady => ProductInfo.IsBoostable;

  internal double ContinuousBoostingHours { get; set; }

  internal DateTime LastPlayedTime { get; set; }

  internal int RemainingAchievementsCount { get; private set; }

  private ProductInfo ProductInfo { get; }

  private AchievementPercentages AchievementPercentages { get; }

  internal int FailedUnlockCount { get; private set; }

  private StatData? FailedUnlockStat { get; set; }

  internal BoostableApp(uint id, ProductInfo product, AchievementPercentages percentages) {
    ID = id;
    ProductInfo = product;
    AchievementPercentages = percentages;
    ContinuousBoostingHours = 0;
  }

  internal async Task<(bool, string)> UnlockNextAchievement(BoosterHandler boosterHandler) {
    UserStatsResponse? response = await boosterHandler.GetStats(ID).ConfigureAwait(false);
    if (response == null) {
      // Not reachable
      return (false, string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, ID));
    }

    StatData? nextStat = GetUpcomingUnlockableStat(response.StatDatas);
    if (nextStat == null) {
      return (false, string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, ID));//TODO: change message to already perfect
    }

    // Unlock next achievement
    if (await boosterHandler.UnlockStat(ID, nextStat, response.CrcStats).ConfigureAwait(false)) {
      RemainingAchievementsCount--;
      return (true, string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementSuccess, nextStat.Name, ID));
    }
    else {
      if (nextStat.APIName.Equals(FailedUnlockStat?.APIName, StringComparison.Ordinal)) {
        FailedUnlockCount++;
      }
      else {
        FailedUnlockStat = nextStat;
        FailedUnlockCount = 0;
      }
      return (false, string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementFailed, nextStat.Name, ID));
    }
  }

  private StatData? GetUpcomingUnlockableStat(List<StatData> statDatas) {
    ArgumentNullException.ThrowIfNull(AchievementPercentages);

    List<StatData> unlockableStats = statDatas.Where(e => e.Unlockable()).ToList();
    RemainingAchievementsCount = unlockableStats.Count;

    if (RemainingAchievementsCount == 0) {
      return null;
    }

    foreach (StatData statData in unlockableStats) {
      statData.Percentage = AchievementPercentages.GetPercentage(statData.APIName, 0);
    }
    unlockableStats.Sort((x, y) => y.Percentage.CompareTo(x.Percentage));

    return unlockableStats.First();
  }
}

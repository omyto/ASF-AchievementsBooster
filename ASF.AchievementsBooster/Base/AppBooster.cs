
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using AchievementsBooster.Stats;

namespace AchievementsBooster.Base;

internal sealed class AppBooster {
  private const byte MaxUnlockTries = 3;

  internal uint ID { get; }

  internal string Name => ProductInfo.Name;

  internal double ContinuousBoostingHours { get; set; }

  internal DateTime LastPlayedTime { get; set; }

  internal bool HasRemainingAchievements => UnlockableStats.Count > 0;

  private readonly ProductInfo ProductInfo;

  private readonly FrozenDictionary<string, double> AchievementPercentages;

  private readonly Dictionary<string, byte> FailedUnlockStats = [];

  private List<StatData> UnlockableStats { get; set; }

  internal AppBooster(uint id, ProductInfo info, FrozenDictionary<string, double> achievementPercentages, List<StatData> unlockableStats) {
    ID = id;
    ProductInfo = info;
    AchievementPercentages = achievementPercentages;
    UnlockableStats = unlockableStats;
    ContinuousBoostingHours = 0;
  }

  internal StatData? GetUpcomingUnlockableStat(List<StatData> statDatas) {
    UnlockableStats = statDatas.Where(e => e.Unlockable()).ToList();
    if (UnlockableStats.Count == 0) {
      return null;
    }

    foreach (StatData statData in UnlockableStats) {
      if (AchievementPercentages.TryGetValue(statData.APIName, out double percentage)) {
        statData.Percentage = percentage;
      }
    }
    UnlockableStats.Sort((x, y) => y.Percentage.CompareTo(x.Percentage));

    return UnlockableStats.First();
  }

  internal void UpdateUnlockStatus(StatData stat, bool success) {
    if (success) {
      for (int i = 0; i < UnlockableStats.Count; i++) {
        if (stat.APIName.Equals(UnlockableStats[i].APIName, StringComparison.Ordinal)) {
          UnlockableStats.RemoveAt(i);
          break;
        }
      }
    }
    else {
      if (FailedUnlockStats.TryGetValue(stat.APIName, out byte count)) {
        FailedUnlockStats[stat.APIName] = ++count;
      }
      else {
        FailedUnlockStats.Clear();
        _ = FailedUnlockStats.TryAdd(stat.APIName, 1);
      }
    }
  }

  internal bool ShouldSkipBoosting() => FailedUnlockStats.Count > 0 && FailedUnlockStats.First().Value > MaxUnlockTries;
}

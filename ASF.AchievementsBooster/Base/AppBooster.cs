
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

  internal ProductInfo ProductInfo { get; }

  internal FrozenDictionary<string, double> AchievementPercentages { get; }

  internal List<StatData> UnlockableStats { get; private set; }

  private readonly Dictionary<string, byte> FailedUnlockStats = [];

  internal AppBooster(uint id, ProductInfo info, FrozenDictionary<string, double> achievementPercentages, List<StatData> unlockableStats) {
    ID = id;
    ProductInfo = info;
    AchievementPercentages = achievementPercentages;
    UnlockableStats = unlockableStats;
    ContinuousBoostingHours = 0;
  }

  internal void AddAndSortUnlockableStats(List<StatData> unlockableStats) {
    UnlockableStats = unlockableStats;
    if (UnlockableStats.Count > 1) {
      foreach (StatData statData in UnlockableStats) {
        if (AchievementPercentages.TryGetValue(statData.APIName, out double percentage)) {
          statData.Percentage = percentage;
        }
      }
      UnlockableStats.Sort((x, y) => y.Percentage.CompareTo(x.Percentage));
    }
  }

  internal void UnlockFailed(StatData statData) {
    if (FailedUnlockStats.TryGetValue(statData.APIName, out byte count)) {
      FailedUnlockStats[statData.APIName] = ++count;
    }
    else {
      FailedUnlockStats.Clear();
      _ = FailedUnlockStats.TryAdd(statData.APIName, 1);
    }
  }

  internal bool ShouldSkipBoosting() => FailedUnlockStats.Count > 0 && FailedUnlockStats.First().Value > MaxUnlockTries;

  internal bool IsCompletedBoosting => UnlockableStats.Count == 0;
}

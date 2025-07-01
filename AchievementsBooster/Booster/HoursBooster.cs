using System;
using System.Collections.Generic;
using System.Linq;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Booster;

internal sealed class HoursBooster {
  private static readonly Lazy<HoursBooster> InstanceHolder = new(() => new HoursBooster());
  internal static HoursBooster Instance => InstanceHolder.Value;

  private List<uint> BoostedGames { get; set; } = [];
  internal List<uint> ReadyToBoostGames { get; private set; } = [];

  internal void Update(HashSet<uint> ownedGames) {
    BoostedGames.AddRange(ReadyToBoostGames);
    ReadyToBoostGames.Clear();

    HashSet<uint> validGames = ownedGames.ToHashSet();

    if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Count > 0) {
      validGames.ExceptWith(ASF.GlobalConfig.Blacklist);
    }

    if (AchievementsBoosterPlugin.GlobalConfig.Blacklist.Count > 0) {
      validGames.ExceptWith(AchievementsBoosterPlugin.GlobalConfig.Blacklist);
    }

    List<uint> waitingGames = validGames.Except(BoostedGames).ToList();

    if (waitingGames.Count > 0) {
      ReadyToBoostGames = waitingGames.Take(AchievementsBoosterPlugin.GlobalConfig.MaxConcurrentlyBoostingApps).ToList();
    }
    else {
      BoostedGames.Clear();
      ReadyToBoostGames = validGames.Take(AchievementsBoosterPlugin.GlobalConfig.MaxConcurrentlyBoostingApps).ToList();
    }
  }
}

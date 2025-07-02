using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Booster;

internal sealed class HoursBooster {
  private static readonly Lazy<HoursBooster> InstanceHolder = new(() => new HoursBooster());
  internal static HoursBooster Instance => InstanceHolder.Value;

  private List<uint> BoostedGames { get; set; } = [];
  internal List<uint> ReadyToBoostGames { get; private set; } = [];

  internal async Task Update(AppManager appManager, CancellationToken cancellationToken) {
    BoostedGames.AddRange(ReadyToBoostGames);
    ReadyToBoostGames.Clear();

    HashSet<uint> validGames = appManager.OwnedGames.ToHashSet();

    if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Count > 0) {
      validGames.ExceptWith(ASF.GlobalConfig.Blacklist);
    }

    if (AchievementsBoosterPlugin.GlobalConfig.Blacklist.Count > 0) {
      validGames.ExceptWith(AchievementsBoosterPlugin.GlobalConfig.Blacklist);
    }

    List<uint> waitingGames = validGames.Except(BoostedGames).ToList();

    if (waitingGames.Count > 0) {
      ReadyToBoostGames = await FindReadyToBoostGames(waitingGames, appManager, cancellationToken).ConfigureAwait(false);
    }

    if (ReadyToBoostGames.Count == 0) {
      BoostedGames.Clear();
      ReadyToBoostGames = await FindReadyToBoostGames(validGames, appManager, cancellationToken).ConfigureAwait(false);
    }
  }

  private static async Task<List<uint>> FindReadyToBoostGames(ICollection<uint> appIDs, AppManager appManager, CancellationToken cancellationToken) {
    List<uint> readyToBoostGames = [];

    foreach (uint appID in appIDs) {
      if (AchievementsBoosterPlugin.GlobalConfig.RestrictAppWithVAC) {
        if (AchievementsBoosterPlugin.GlobalCache.VACApps.Contains(appID)) {
          continue;
        }

        ProductInfo? productInfo = await appManager.GetProductInfo(appID, cancellationToken).ConfigureAwait(false);
        if (productInfo != null && productInfo.IsVACEnabled) {
          _ = AchievementsBoosterPlugin.GlobalCache.VACApps.Add(appID);
          continue;
        }
      }

      readyToBoostGames.Add(appID);
      if (readyToBoostGames.Count >= AchievementsBoosterPlugin.GlobalConfig.MaxConcurrentlyBoostingApps) {
        break;
      }
    }

    return readyToBoostGames;
  }
}

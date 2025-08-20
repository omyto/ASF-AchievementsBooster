using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Model;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Engine;

internal sealed class HoursBooster {
  private static readonly Lazy<HoursBooster> InstanceHolder = new(() => new HoursBooster());
  internal static HoursBooster Instance => InstanceHolder.Value;

  private List<uint> BoostedGames { get; set; } = [];
  internal List<uint> ReadyToBoostGames { get; private set; } = [];

  internal async Task Update(Booster booster, CancellationToken cancellationToken) {
    BoostedGames.AddRange(ReadyToBoostGames);
    ReadyToBoostGames.Clear();

    HashSet<uint> validGames = booster.AppRepository.OwnedGames.ToHashSet();

    if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Count > 0) {
      validGames.ExceptWith(ASF.GlobalConfig.Blacklist);
    }

    if (booster.Config.BlacklistReadOnly.Count > 0) {
      validGames.ExceptWith(booster.Config.BlacklistReadOnly);
    }

    List<uint> waitingGames = validGames.Except(BoostedGames).ToList();

    if (waitingGames.Count > 0) {
      ReadyToBoostGames = await FindReadyToBoostGames(waitingGames, booster, cancellationToken).ConfigureAwait(false);
    }

    if (ReadyToBoostGames.Count == 0) {
      BoostedGames.Clear();
      ReadyToBoostGames = await FindReadyToBoostGames(validGames, booster, cancellationToken).ConfigureAwait(false);
    }
  }

  private static async Task<List<uint>> FindReadyToBoostGames(ICollection<uint> appIDs, Booster booster, CancellationToken cancellationToken) {
    List<uint> readyToBoostGames = [];

    foreach (uint appID in appIDs) {
      if (booster.Config.RestrictAppWithVAC) {
        if (AchievementsBoosterPlugin.GlobalCache.VACApps.Contains(appID)) {
          continue;
        }

        ProductInfo? productInfo = await booster.AppRepository.GetProductInfo(appID, cancellationToken).ConfigureAwait(false);
        if (productInfo != null && productInfo.IsVACEnabled) {
          _ = AchievementsBoosterPlugin.GlobalCache.VACApps.Add(appID);
          continue;
        }
      }

      readyToBoostGames.Add(appID);
      if (readyToBoostGames.Count >= booster.Config.MaxConcurrentlyBoostingApps) {
        break;
      }
    }

    return readyToBoostGames;
  }
}

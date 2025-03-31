using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using ArchiSteamFarm.Steam.Cards;

namespace AchievementsBooster.Booster;

internal sealed class CardFarmingBooster : Booster {
  internal CardFarmingBooster(BoosterBot bot) : base(EBoostMode.CardFarming, bot) {
  }

  protected override void ResumePlay() { }

  protected override AppBoostInfo[] GetReadyToUnlockApps() {
    // Intersect between BoostingApps and CurrentGamesFarming
    List<uint> boostingAppIDs = [];
    foreach (Game game in Bot.CurrentGamesFarming) {
      if (CurrentBoostingApps.ContainsKey(game.AppID)) {
        boostingAppIDs.Add(game.AppID);
      }
    }

    if (boostingAppIDs.Count > 0) {
      List<uint> exceptAppIDs = CurrentBoostingApps.Keys.Except(boostingAppIDs).ToList();
      exceptAppIDs.ForEach(appID => CurrentBoostingApps.Remove(appID));
    }
    else {
      CurrentBoostingApps.Clear();
    }

    return CurrentBoostingApps.Values.ToArray();
  }

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = [];

    try {
      Game[] currentGamesFarming = Bot.CurrentGamesFarming.ToArray();
      for (int index = 0; index < currentGamesFarming.Length && results.Count < count; index++) {
        cancellationToken.ThrowIfCancellationRequested();
        uint appID = currentGamesFarming[index].AppID;
        if (!CurrentBoostingApps.ContainsKey(appID)) {
          AppBoostInfo? app = await AppManager.GetAppBoost(appID, cancellationToken).ConfigureAwait(false);
          if (app != null) {
            results.Add(app);
          }
        }
      }
    }
    catch (Exception) {
      if (results.Count > 0) {
        DateTime now = DateTime.Now;
        results.ForEach(app => AppManager.MarkAppAsResting(app, now));
      }
      throw;
    }

    return results;
  }

  protected override Task<bool> PlayCurrentBoostingApps(CancellationToken cancellationToken) => Task.FromResult(true);

  protected override void LogNoneAppsForBoosting() => Logger.Info(Messages.NoBoostingAppsInArchiFarming);
}

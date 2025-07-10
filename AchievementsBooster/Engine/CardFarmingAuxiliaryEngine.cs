using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using AchievementsBooster.Model;
using ArchiSteamFarm.Steam.Cards;

namespace AchievementsBooster.Engine;

internal sealed class CardFarmingAuxiliaryEngine : BoostEngine {
  internal CardFarmingAuxiliaryEngine(Booster booster) : base(EBoostMode.CardFarming, booster) {
  }

  protected override void ResumePlay() { }

  protected override AppBoostInfo[] GetReadyToUnlockApps() {
    // Intersect between BoostingApps and CurrentGamesFarming
    List<uint> boostingAppIDs = [];
    foreach (Game game in Booster.CurrentGamesFarming) {
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
      Game[] currentGamesFarming = Booster.CurrentGamesFarming.ToArray();
      for (int index = 0; index < currentGamesFarming.Length && results.Count < count; index++) {
        cancellationToken.ThrowIfCancellationRequested();
        uint appID = currentGamesFarming[index].AppID;
        if (!CurrentBoostingApps.ContainsKey(appID)) {
          AppBoostInfo? app = await Booster.AppManager.GetAppBoost(appID, cancellationToken).ConfigureAwait(false);
          if (app != null) {
            results.Add(app);
          }
        }
      }
    }
    catch (Exception) {
      if (results.Count > 0) {
        DateTime now = DateTime.Now;
        results.ForEach(app => Booster.AppManager.MarkAppAsResting(app, now));
      }
      throw;
    }

    return results;
  }

  protected override Task<bool> PlayCurrentBoostingApps(CancellationToken cancellationToken) => Task.FromResult(true);

  protected override string GetNoBoostingAppsMessage() => Messages.NoBoostingAppsInArchiFarming;

  protected override Task FallBackToIdleGaming(CancellationToken cancellationToken) => Task.CompletedTask;
}

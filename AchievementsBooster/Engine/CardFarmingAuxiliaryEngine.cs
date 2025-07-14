using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using ArchiSteamFarm.Steam.Cards;

namespace AchievementsBooster.Engine;

internal sealed class CardFarmingAuxiliaryEngine : BoostEngine {

  [SuppressMessage("Style", "IDE0021:Use expression body for constructor", Justification = "<Pending>")]
  internal CardFarmingAuxiliaryEngine(Booster booster) : base(EBoostMode.CardFarming, booster) {
    NoBoostingAppsMessage = Messages.NoBoostingAppsInArchiFarming;
  }

  internal override TimeSpan GetNextBoostDueTime() {
    TimeSpan dueTime = base.GetNextBoostDueTime();
    return dueTime > Constants.FiveMinutes ? Constants.FiveMinutes : dueTime;
  }

  private ImmutableHashSet<uint> LastGamesFarming { get; set; } = [];
  private IReadOnlyCollection<Game> CurrentGamesFarming => Booster.Bot.CardsFarmer.CurrentGamesFarmingReadOnly;

  protected override bool AreBoostingGamesStillValid() {
    Booster.Logger.Trace("Checking if the boosting games are still valid ...");
    bool isFarmingGamesChanged = true;
    foreach (Game game in CurrentGamesFarming) {
      if (LastGamesFarming.Contains(game.AppID)) {
        isFarmingGamesChanged = false;
        break;
      }
    }

    return !isFarmingGamesChanged;
  }

  protected override AppBoostInfo[] GetReadyToUnlockApps() {
    // Intersect between BoostingApps and CurrentGamesFarming
    List<uint> boostingAppIDs = [];
    foreach (Game game in CurrentGamesFarming) {
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
      Game[] currentGamesFarming = CurrentGamesFarming.ToArray();
      for (int index = 0; index < currentGamesFarming.Length && results.Count < count; index++) {
        cancellationToken.ThrowIfCancellationRequested();
        uint appID = currentGamesFarming[index].AppID;
        if (!CurrentBoostingApps.ContainsKey(appID)) {
          AppBoostInfo? app = await Booster.AppRepository.GetAppBoost(appID, cancellationToken).ConfigureAwait(false);
          if (app != null) {
            results.Add(app);
          }
        }
      }
    }
    catch (Exception) {
      if (results.Count > 0) {
        DateTime now = DateTime.Now;
        results.ForEach(app => Booster.AppRepository.MarkAppAsResting(app, now));
      }
      throw;
    }

    LastGamesFarming = CurrentGamesFarming.Select(e => e.AppID).ToImmutableHashSet();
    return results;
  }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using ArchiSteamFarm.Steam.Cards;

namespace AchievementsBooster.Engine;

internal sealed class CardFarmingAuxiliaryEngine : BoostingEngineBase {

  internal CardFarmingAuxiliaryEngine(Booster booster) : base(EBoostMode.CardFarming, booster) {
  }

  internal override TimeSpan GetNextBoostDueTime() {
    TimeSpan dueTime = base.GetNextBoostDueTime();
    return dueTime > BoosterShared.FiveMinutes ? BoosterShared.FiveMinutes : dueTime;
  }

  private ImmutableHashSet<uint> LastGamesFarming { get; set; } = [];
  private IReadOnlyCollection<Game> CurrentGamesFarming => Booster.Bot.CardsFarmer.CurrentGamesFarmingReadOnly;

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

  protected override Task PreFillApps(bool isFirstTime, bool isUnlockTime) {
    if (!isFirstTime && !isUnlockTime) {
      if (!AreBoostingGamesStillValid()) {
        CurrentBoostingApps.Clear();
        Booster.Logger.Info("Farming games have changed, update the boosting games ...");
      }
    }
    return Task.CompletedTask;
  }

  private bool AreBoostingGamesStillValid() {
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

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = [];
    Game[] currentGamesFarming = CurrentGamesFarming.ToArray();

    for (int index = 0; index < currentGamesFarming.Length && results.Count < count; index++) {
      cancellationToken.ThrowIfCancellationRequested();
      uint appID = currentGamesFarming[index].AppID;

      if (!CurrentBoostingApps.ContainsKey(appID)) {
        AppBoostInfo? app = await Booster.AppRepository.GetBoostableApp(appID, cancellationToken).ConfigureAwait(false);
        if (app != null) {
          results.Add(app);
        }
      }
    }

    LastGamesFarming = CurrentGamesFarming.Select(e => e.AppID).ToImmutableHashSet();
    return results;
  }

  protected override void Notify(TimeSpan achieveTimeRemaining) {
    if (CurrentBoostingApps.Count > 0) {
      base.Notify(achieveTimeRemaining);
    }
    else {
      Booster.Logger.Info("No apps are available to boost achievements during farming cards. Recheck after farming game has been changed!");
    }
  }
}

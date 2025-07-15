using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;

namespace AchievementsBooster.Engine;

internal sealed class GameIdlingAuxiliaryEngine : BoostingEngineBase {
  private Queue<uint> ArchiBoostableAppsPlayedWhileIdle { get; }

  internal GameIdlingAuxiliaryEngine(Booster booster) : base(EBoostMode.IdleGaming, booster) {
    NoBoostingAppsMessage = Messages.NoBoostingAppsInArchiPlayedWhileIdle;
    ArchiBoostableAppsPlayedWhileIdle = new Queue<uint>(Booster.Bot.BotConfig.GamesPlayedWhileIdle);
  }

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = [];

    try {
      while (ArchiBoostableAppsPlayedWhileIdle.Count > 0 && results.Count < count) {
        cancellationToken.ThrowIfCancellationRequested();
        uint appID = ArchiBoostableAppsPlayedWhileIdle.Peek();
        AppBoostInfo? app = await Booster.AppRepository.GetBoostableApp(appID, cancellationToken).ConfigureAwait(false);
        if (app != null) {
          results.Add(app);
        }
        _ = ArchiBoostableAppsPlayedWhileIdle.Dequeue();
      }
    }
    catch (Exception) {
      if (results.Count > 0) {
        DateTime now = DateTime.Now;
        results.ForEach(app => Booster.AppRepository.MarkAppAsResting(app, now));
      }
      throw;
    }

    return results;
  }
}

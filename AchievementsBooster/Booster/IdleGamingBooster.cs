using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;

namespace AchievementsBooster.Booster;

internal sealed class IdleGamingBooster : Booster {
  private Queue<uint> ArchiBoostableAppsPlayedWhileIdle { get; }

  [SuppressMessage("Style", "IDE0306:Simplify collection initialization", Justification = "<Pending>")]
  [SuppressMessage("Style", "IDE0021:Use expression body for constructor", Justification = "<Pending>")]
  internal IdleGamingBooster(BoosterBot bot) : base(EBoostMode.IdleGaming, bot) {
    // Since GamesPlayedWhileIdle may never change
    ArchiBoostableAppsPlayedWhileIdle = new Queue<uint>(Bot.GamesPlayedWhileIdle ?? []);
  }

  protected override void ResumePlay() { }

  protected override AppBoostInfo[] GetReadyToUnlockApps() => CurrentBoostingApps.Values.ToArray();

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = [];

    try {
      while (ArchiBoostableAppsPlayedWhileIdle.Count > 0 && results.Count < count) {
        cancellationToken.ThrowIfCancellationRequested();
        uint appID = ArchiBoostableAppsPlayedWhileIdle.Peek();
        AppBoostInfo? app = await AppManager.GetAppBoost(appID, cancellationToken).ConfigureAwait(false);
        if (app != null) {
          results.Add(app);
        }
        _ = ArchiBoostableAppsPlayedWhileIdle.Dequeue();
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

  protected override string GetNoBoostingAppsMessage() => Messages.NoBoostingAppsInArchiPlayedWhileIdle;

}

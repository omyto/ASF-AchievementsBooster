using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Steam;

namespace AchievementsBooster.Booster;

internal sealed class IdleGamingBooster : BaseBooster {
  private Queue<uint> ArchiBoostableAppsPlayedWhileIdle { get; }

  [SuppressMessage("Style", "IDE0306:Simplify collection initialization", Justification = "<Pending>")]
  [SuppressMessage("Style", "IDE0021:Use expression body for constructor", Justification = "<Pending>")]
  internal IdleGamingBooster(Bot bot, BotCache cache, AppManager appManager, ImmutableList<uint>? gamesPlayedWhileIdle) : base(EBoostMode.IdleGaming, bot, cache, appManager) {
    // Since GamesPlayedWhileIdle may never change
    ArchiBoostableAppsPlayedWhileIdle = new Queue<uint>(gamesPlayedWhileIdle ?? []);
  }

  internal override void ResumePlay() { }

  protected override AppBoostInfo[] GetReadyToUnlockApps() => CurrentBoostingApps.Values.ToArray();

  protected override async Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = [];

    try {
      while (ArchiBoostableAppsPlayedWhileIdle.Count > 0 && results.Count < count) {
        uint appID = ArchiBoostableAppsPlayedWhileIdle.Dequeue();
        try {
          cancellationToken.ThrowIfCancellationRequested();
          AppBoostInfo? app = await AppManager.GetAppBoost(appID, cancellationToken).ConfigureAwait(false);
          if (app != null) {
            results.Add(app);
          }
        }
        catch (OperationCanceledException) {
          ArchiBoostableAppsPlayedWhileIdle.Enqueue(appID);
          throw;
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

  protected override Task PlayCurrentBoostingApps() => Task.CompletedTask;

  protected override void LogNoneAppsForBoosting() =>
    //if (lastState != EBoostingState.ArchiPlayedWhileIdle || lastPlaying.Count > 0) {
    Logger.Info(Messages.NoBoostingAppsInArchiPlayedWhileIdle);

}

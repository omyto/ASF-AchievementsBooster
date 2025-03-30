using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Steam;

namespace AchievementsBooster.Booster;

internal enum EBoostMode {
  CardFarming,
  IdleGaming,
  AutoBoost
}

internal abstract class BaseBooster(EBoostMode mode, Bot bot, BotCache cache, AppManager appManager) {
  internal EBoostMode Mode { get; } = mode;

  protected Bot Bot { get; } = bot;
  protected BotCache Cache { get; } = cache;
  protected AppManager AppManager { get; } = appManager;
  protected Logger Logger => AppManager.BoosterHandler.Logger; //TOOD

  protected BoosterHandler BoosterHandler => AppManager.BoosterHandler;
  protected Dictionary<uint, AppBoostInfo> CurrentBoostingApps { get; } = [];

  protected static BoosterGlobalConfig GlobalConfig => AchievementsBoosterPlugin.GlobalConfig;

  protected abstract AppBoostInfo[] GetReadyToUnlockApps();
  protected abstract Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken);
  protected abstract Task PlayCurrentBoostingApps();
  protected abstract void LogNoneAppsForBoosting();

  internal abstract void ResumePlay();

  internal void Stop() {
    AppManager.MarkAppsAsResting(CurrentBoostingApps.Values.ToList(), DateTime.Now);
    CurrentBoostingApps.Clear();
  }

  internal async Task Boosting(DateTime lastBoosterHeartBeatTime, CancellationToken cancellationToken) {
    await UnlockAchievements(DateTime.Now, lastBoosterHeartBeatTime, cancellationToken).ConfigureAwait(false);

    // Add new apps for boosting if need
    if (CurrentBoostingApps.Count < GlobalConfig.MaxConcurrentlyBoostingApps) {
      List<AppBoostInfo> newApps = await FindNewAppsForBoosting(GlobalConfig.MaxConcurrentlyBoostingApps - CurrentBoostingApps.Count, cancellationToken).ConfigureAwait(false);
      newApps.ForEach(app => CurrentBoostingApps.TryAdd(app.ID, app));
    }

    if (CurrentBoostingApps.Count > 0) {
      await PlayCurrentBoostingApps().ConfigureAwait(false);

      foreach (AppBoostInfo app in CurrentBoostingApps.Values) {
        Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApp, app.FullName, app.UnlockableAchievementsCount));
      }
    }
    else {
      LogNoneAppsForBoosting();
    }
  }

  private async Task UnlockAchievements(DateTime currentTime, DateTime lastBoosterHeartBeatTime, CancellationToken cancellationToken) {
    if (CurrentBoostingApps.Count == 0) {
      return;
    }

    int deltaTime = (int) (currentTime - lastBoosterHeartBeatTime).TotalMinutes;
    AppBoostInfo[] readyToUnlockApps = GetReadyToUnlockApps();

    foreach (AppBoostInfo app in readyToUnlockApps) {
      cancellationToken.ThrowIfCancellationRequested();
      //BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
      app.BoostingDuration += deltaTime;

      (bool success, string message) = await app.UnlockNextAchievement(BoosterHandler, cancellationToken).ConfigureAwait(false);
      if (success) {
        Logger.Info(message);
        if (app.UnlockableAchievementsCount == 0) {
          _ = CurrentBoostingApps.Remove(app.ID);
          _ = Cache.PerfectGames.Add(app.ID);
          Logger.Info(string.Format(CultureInfo.CurrentCulture, app.RemainingAchievementsCount == 0 ? Messages.FinishedBoost : Messages.FinishedBoostable, app.FullName));
          continue;
        }
      }
      else {
        Logger.Warning(message);
        if (app.UnlockableAchievementsCount == 0) {
          if (app.RemainingAchievementsCount == 0) {
            _ = Cache.PerfectGames.Add(app.ID);
          }
          _ = CurrentBoostingApps.Remove(app.ID);
          continue;
        }

        if (app.FailedUnlockCount > Constants.MaxUnlockAchievementTries) {
          _ = CurrentBoostingApps.Remove(app.ID);
          AppManager.MarkAppAsResting(app, DateTime.Now.AddHours(12));
          continue;
        }
      }

      if (GlobalConfig.BoostDurationPerApp > 0) {
        if (app.BoostingDuration >= GlobalConfig.BoostDurationPerApp) {
          _ = CurrentBoostingApps.Remove(app.ID);
          Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApp, app.FullName, app.BoostingDuration));
          AppManager.MarkAppAsResting(app);
        }
      }
    }
  }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;

namespace AchievementsBooster.Booster;

internal enum EBoostMode {
  CardFarming,
  IdleGaming,
  AutoBoost
}

internal abstract class BaseBooster(EBoostMode mode, BoosterBot bot) {
  internal EBoostMode Mode { get; } = mode;
  protected BoosterBot Bot { get; } = bot;

  protected Logger Logger => Bot.Logger;
  protected AppManager AppManager => Bot.AppManager;

  protected Dictionary<uint, AppBoostInfo> CurrentBoostingApps { get; } = [];

  protected abstract AppBoostInfo[] GetReadyToUnlockApps();
  protected abstract Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken);
  protected abstract Task PlayCurrentBoostingApps();
  protected abstract void LogNoneAppsForBoosting();

  internal abstract void ResumePlay();

  internal void Stop() {
    AppManager.MarkAppsAsResting(CurrentBoostingApps.Values.ToList(), DateTime.Now);
    CurrentBoostingApps.Clear();
  }

  internal async Task Boosting(DateTime lastBoosterHeartBeatTime, bool isRestingTime, CancellationToken cancellationToken) {
    await UnlockAchievements(DateTime.Now, lastBoosterHeartBeatTime, cancellationToken).ConfigureAwait(false);
    if (isRestingTime) {
      return;
    }

    // Add new apps for boosting if need
    if (CurrentBoostingApps.Count < AchievementsBoosterPlugin.GlobalConfig.MaxConcurrentlyBoostingApps) {
      List<AppBoostInfo> newApps = await FindNewAppsForBoosting(AchievementsBoosterPlugin.GlobalConfig.MaxConcurrentlyBoostingApps - CurrentBoostingApps.Count, cancellationToken).ConfigureAwait(false);
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

      (bool success, string message) = await app.UnlockNextAchievement(Bot.SteamClientHandler, cancellationToken).ConfigureAwait(false);
      if (success) {
        Logger.Info(message);
        if (app.UnlockableAchievementsCount == 0) {
          _ = CurrentBoostingApps.Remove(app.ID);
          _ = Bot.Cache.PerfectGames.Add(app.ID);
          Logger.Info(string.Format(CultureInfo.CurrentCulture, app.RemainingAchievementsCount == 0 ? Messages.FinishedBoost : Messages.FinishedBoostable, app.FullName));
          continue;
        }
      }
      else {
        Logger.Warning(message);
        if (app.UnlockableAchievementsCount == 0) {
          if (app.RemainingAchievementsCount == 0) {
            _ = Bot.Cache.PerfectGames.Add(app.ID);
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

      if (AchievementsBoosterPlugin.GlobalConfig.BoostDurationPerApp > 0) {
        if (app.BoostingDuration >= AchievementsBoosterPlugin.GlobalConfig.BoostDurationPerApp) {
          _ = CurrentBoostingApps.Remove(app.ID);
          Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApp, app.FullName, app.BoostingDuration));
          AppManager.MarkAppAsResting(app);
        }
      }
    }
  }
}

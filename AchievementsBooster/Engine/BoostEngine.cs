using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Engine;

public enum EBoostMode {
  CardFarming,
  IdleGaming,
  AutoBoost
}

internal abstract class BoostEngine(EBoostMode mode, Booster booster) {
  public EBoostMode Mode { get; } = mode;

  protected Booster Booster { get; } = booster;

  private SemaphoreSlim BoosterSemaphore { get; } = new SemaphoreSlim(1, 1);

  protected Dictionary<uint, AppBoostInfo> CurrentBoostingApps { get; } = [];

  internal int CurrentBoostingAppsCount => CurrentBoostingApps.Count;

  internal DateTime NextAchieveTime { get; private set; } = DateTime.MinValue;

  internal virtual TimeSpan GetNextBoostDueTime() => NextAchieveTime - DateTime.Now;

  protected virtual bool AreBoostingGamesStillValid() => true;

  protected abstract AppBoostInfo[] GetReadyToUnlockApps();

  protected abstract Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken cancellationToken);

  protected virtual Task<bool> PlayCurrentBoostingApps(CancellationToken cancellationToken) => Task.FromResult(true);

  protected abstract string GetNoBoostingAppsMessage();

  protected virtual Task FallBackToIdleGaming(CancellationToken cancellationToken) => Task.CompletedTask;

  protected virtual void ResumePlay() { }

  public void StopPlay(bool resumePlay = false) {
    if (CurrentBoostingApps.Count > 0) {
      BoosterSemaphore.Wait();
      try {
        Booster.AppManager.MarkAppsAsResting(CurrentBoostingApps.Values.ToList(), DateTime.Now);
        CurrentBoostingApps.Clear();
      }
      finally {
        _ = BoosterSemaphore.Release();
      }
    }

    if (resumePlay) {
      ResumePlay();
    }
  }

  public async Task Boosting(DateTime lastBoosterHeartBeatTime, bool isRestingTime, CancellationToken cancellationToken) {
    await BoosterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    bool isFirstTime = NextAchieveTime == DateTime.MinValue;
    bool shouldUpdateNextAchieveTime = isFirstTime;

    try {
      DateTime currentTime = DateTime.Now;
      if (!isFirstTime) {
        Booster.Logger.Trace($"Now        : {currentTime.ToLongTimeString()} ({currentTime.Ticks})");
        Booster.Logger.Trace($"NextAchieve: {NextAchieveTime.ToLongTimeString()} ({NextAchieveTime.Ticks})");

        if (currentTime >= NextAchieveTime) {
          shouldUpdateNextAchieveTime = true;
          await Achieve(currentTime, lastBoosterHeartBeatTime, cancellationToken).ConfigureAwait(false);
        }
        else if (!AreBoostingGamesStillValid()) {
          CurrentBoostingApps.Clear();
          shouldUpdateNextAchieveTime = true;
          Booster.Logger.Info("Farming games have changed, update the boosting games ...");
        }
      }

      if (isRestingTime) {
        return;
      }

      // Add new apps for boosting if need
      bool isBoostingAppsChanged = false;
      if (CurrentBoostingApps.Count < BoosterConfig.Global.MaxConcurrentlyBoostingApps) {
        List<AppBoostInfo> newApps = await FindNewAppsForBoosting(BoosterConfig.Global.MaxConcurrentlyBoostingApps - CurrentBoostingApps.Count, cancellationToken).ConfigureAwait(false);
        newApps.ForEach(app => CurrentBoostingApps.TryAdd(app.ID, app));
        isBoostingAppsChanged = true;
      }

      if (CurrentBoostingApps.Count == 0) {
        Booster.Logger.Info(GetNoBoostingAppsMessage());
        if (BoosterConfig.Global.BoostHoursWhenIdle) {
          await FallBackToIdleGaming(cancellationToken).ConfigureAwait(false);
        }
        return;
      }

      if (isBoostingAppsChanged && await PlayCurrentBoostingApps(cancellationToken).ConfigureAwait(false)) {
        foreach (AppBoostInfo app in CurrentBoostingApps.Values) {
          Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApp, app.FullName, app.UnlockableAchievementsCount));
        }
      }
    }
    finally {
      if (shouldUpdateNextAchieveTime) {
        TimeSpan achieveTimeRemaining = TimeSpanUtils.RandomInMinutesRange(BoosterConfig.Global.MinBoostInterval, BoosterConfig.Global.MaxBoostInterval);
        NextAchieveTime = DateTime.Now.Add(achieveTimeRemaining);
        if (CurrentBoostingAppsCount > 0) {
          Booster.Logger.Info($"Boosting {CurrentBoostingAppsCount} games, unlock achievements after: {achieveTimeRemaining.ToHumanReadable()} ({NextAchieveTime.ToShortTimeString()})");
        }
      }
      _ = BoosterSemaphore.Release();
    }
  }

  // Unlock achievements
  private async Task Achieve(DateTime currentTime, DateTime lastBoosterHeartBeatTime, CancellationToken cancellationToken) {
    Booster.Logger.Trace($"Achieving achievements for {CurrentBoostingApps.Count} game(s) ...");
    if (CurrentBoostingApps.Count == 0) {
      return;
    }

    int deltaTime = (int) (currentTime - lastBoosterHeartBeatTime).TotalMinutes;
    AppBoostInfo[] readyToUnlockApps = GetReadyToUnlockApps();

    foreach (AppBoostInfo app in readyToUnlockApps) {
      cancellationToken.ThrowIfCancellationRequested();
      app.BoostingDuration += deltaTime;

      (bool success, string message) = await app.UnlockNextAchievement(Booster.SteamClientHandler, cancellationToken).ConfigureAwait(false);
      if (success) {
        Booster.Logger.Info(message);
        if (app.UnlockableAchievementsCount == 0) {
          _ = CurrentBoostingApps.Remove(app.ID);
          _ = Booster.Cache.PerfectGames.Add(app.ID);
          Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, app.RemainingAchievementsCount == 0 ? Messages.FinishedBoost : Messages.FinishedBoostable, app.FullName));
          continue;
        }
      }
      else {
        Booster.Logger.Warning(message);
        if (app.UnlockableAchievementsCount == 0) {
          if (app.RemainingAchievementsCount == 0) {
            _ = Booster.Cache.PerfectGames.Add(app.ID);
          }
          _ = CurrentBoostingApps.Remove(app.ID);
          continue;
        }

        if (app.FailedUnlockCount > Constants.MaxUnlockAchievementTries) {
          _ = CurrentBoostingApps.Remove(app.ID);
          Booster.AppManager.MarkAppAsResting(app, DateTime.Now.AddHours(24));
          continue;
        }
      }

      if (Mode == EBoostMode.AutoBoost) {
        _ = Resting(app);
      }
    }
  }

  private bool Resting(AppBoostInfo app) {
    if (BoosterConfig.Global.BoostDurationPerApp > 0) {
      if (app.BoostingDuration >= BoosterConfig.Global.BoostDurationPerApp) {
        _ = CurrentBoostingApps.Remove(app.ID);
        Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApp, app.FullName, app.BoostingDuration));
        Booster.AppManager.MarkAppAsResting(app);
        return true;
      }
    }
    return false;
  }
}

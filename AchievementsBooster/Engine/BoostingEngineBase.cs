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

internal enum EBoostMode {
  CardFarming,
  IdleGaming,
  AutoBoost
}

internal abstract class BoostingEngineBase(EBoostMode mode, Booster booster) {
  private static byte MaxUnlockAchievementTries { get; } = 3;

  internal EBoostMode Mode { get; } = mode;

  protected Booster Booster { get; } = booster;

  protected string NoBoostingAppsMessage { get; init; } = string.Empty;

  private SemaphoreSlim BoosterSemaphore { get; } = new SemaphoreSlim(1, 1);

  protected Dictionary<uint, AppBoostInfo> CurrentBoostingApps { get; } = [];

  private DateTime NextAchieveTime { get; set; } = DateTime.MinValue;

  internal virtual Task Update() => Task.CompletedTask;

  internal virtual TimeSpan GetNextBoostDueTime() => NextAchieveTime - DateTime.Now;

  protected virtual bool AreBoostingGamesStillValid() => true;

  protected virtual bool ShouldRestingApp(AppBoostInfo app) => false;

  protected virtual Task<bool> PlayBoostingApps(CancellationToken token) => Task.FromResult(true);

  protected virtual Task FallBackToIdleGaming(CancellationToken token) => Task.CompletedTask;

  protected virtual void ResumePlay() { }

  protected virtual AppBoostInfo[] GetReadyToUnlockApps() => CurrentBoostingApps.Values.ToArray();

  protected abstract Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken token);

  internal string GetStatus() => CurrentBoostingApps.Count > 0
      ? $"AchievementsBooster is running (mode: {Mode}). Boosting {CurrentBoostingApps.Count} game(s)"
      : $"AchievementsBooster is running (mode: {Mode}), but there are no games to boost";

  internal void Initialize() => Booster.Logger.Info($"Initializing new boosting mode {Mode} ....");

  internal void Destroy() {
    Booster.Logger.Info("Boosting mode changed, stopping the current boosting process ...");
    StopPlay();
  }

  internal void StopPlay(bool resumePlay = false) {
    if (CurrentBoostingApps.Count > 0) {
      BoosterSemaphore.Wait();
      try {
        DateTime now = DateTime.Now;
        foreach (AppBoostInfo app in CurrentBoostingApps.Values) {
          Booster.AppRepository.MarkAppAsResting(app, now);
        }
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

  internal async Task Boosting(DateTime lastBoosterHeartBeatTime, bool isRestingTime, CancellationToken cancellationToken) {
    await BoosterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    bool isFirstTime = NextAchieveTime == DateTime.MinValue;
    bool shouldUpdateNextAchieveTime = isFirstTime;

    try {
      DateTime currentTime = DateTime.Now;
      if (!isFirstTime) {
#if DEBUG
        Booster.Logger.Trace($"Now        : {currentTime.ToLongTimeString()} ({currentTime.Ticks})");
        Booster.Logger.Trace($"NextAchieve: {NextAchieveTime.ToLongTimeString()} ({NextAchieveTime.Ticks})");
#endif

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
      if (CurrentBoostingApps.Count < BoosterConfig.Global.MaxConcurrentlyBoostingApps) {
        Booster.Logger.Trace($"Current boosting apps: {CurrentBoostingApps.Count}, max allowed: {BoosterConfig.Global.MaxConcurrentlyBoostingApps}");
        List<AppBoostInfo> newApps = await FindNewAppsForBoosting(BoosterConfig.Global.MaxConcurrentlyBoostingApps - CurrentBoostingApps.Count, cancellationToken).ConfigureAwait(false);
        if (newApps.Count > 0) {
          Booster.Logger.Trace($"Found {newApps.Count} new apps for boosting, adding them to the current boosting apps ...");
          newApps.ForEach(app => CurrentBoostingApps.TryAdd(app.ID, app));
          shouldUpdateNextAchieveTime = true;
        }
      }

      if (CurrentBoostingApps.Count == 0) {
        shouldUpdateNextAchieveTime = true;
        Booster.Logger.Info(NoBoostingAppsMessage);
        if (BoosterConfig.Global.BoostHoursWhenIdle) {
          await FallBackToIdleGaming(cancellationToken).ConfigureAwait(false);
        }
        return;
      }

      // Play boosting apps if first time or if the next achievement time is reached or if the boosting apps have changed
      if (shouldUpdateNextAchieveTime) {
        if (!await PlayBoostingApps(cancellationToken).ConfigureAwait(false)) {
          CurrentBoostingApps.Clear();
        }
      }
    }
    finally {
      if (shouldUpdateNextAchieveTime) {
        TimeSpan achieveTimeRemaining = BoosterShared.OneHour;
        if (CurrentBoostingApps.Count > 0) {
          TimeSpan minBoostInterval = TimeSpan.FromMinutes(BoosterConfig.Global.MinBoostInterval);
          achieveTimeRemaining = minBoostInterval.AddRandomMinutes(BoosterConfig.Global.MaxBoostInterval - BoosterConfig.Global.MinBoostInterval);
        }

        NextAchieveTime = DateTime.Now.Add(achieveTimeRemaining);
        await Task.Delay(BoosterShared.OneSeconds, cancellationToken).ConfigureAwait(false);

        Notify(achieveTimeRemaining);
      }
      _ = BoosterSemaphore.Release();
    }
  }

  protected virtual void Notify(TimeSpan timeRemaining) {
    if (CurrentBoostingApps.Count > 0) {
      if (CurrentBoostingApps.Count == 1) {
        AppBoostInfo app = CurrentBoostingApps.Values.First();
        string prefix = string.Format(CultureInfo.CurrentCulture, Messages.BoostingApp, app.FullName, app.UnlockableAchievementsCount);
        Booster.Logger.Info($"{prefix}, unlock achievements after: {timeRemaining.ToHumanReadable()} ({NextAchieveTime.ToShortTimeString()})");
      }
      else {
        foreach (AppBoostInfo app in CurrentBoostingApps.Values) {
          Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostingApp, app.FullName, app.UnlockableAchievementsCount));
        }
        Booster.Logger.Info($"Boosting {CurrentBoostingApps.Count} games, unlock achievements after: {timeRemaining.ToHumanReadable()} ({NextAchieveTime.ToShortTimeString()})");
      }
    }
    else {
      Booster.Logger.Info($"No games to boost, next check after: {timeRemaining.ToHumanReadable()} ({NextAchieveTime.ToShortTimeString()})");
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

    if (readyToUnlockApps.Length == 0) {
      Booster.Logger.Info("No apps ready to unlock achievements, skipping ...");
      return;
    }

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

        if (app.FailedUnlockCount > MaxUnlockAchievementTries) {
          _ = CurrentBoostingApps.Remove(app.ID);
          Booster.AppRepository.MarkAppAsResting(app, DateTime.Now.AddHours(24));
          continue;
        }
      }

      if (ShouldRestingApp(app)) {
        Resting(app);
      }
    }
  }

  private void Resting(AppBoostInfo app) {
    _ = CurrentBoostingApps.Remove(app.ID);
    Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApp, app.FullName, app.BoostingDuration));
    Booster.AppRepository.MarkAppAsResting(app);
  }
}

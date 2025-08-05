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

  private SemaphoreSlim BoosterSemaphore { get; } = new SemaphoreSlim(1, 1);

  protected Dictionary<uint, AppBoostInfo> CurrentBoostingApps { get; } = [];

  protected DateTime NextAchieveTime { get; private set; } = DateTime.MinValue;

  internal virtual Task Update() => Task.CompletedTask;

  protected virtual Task PreFillApps(bool isFirstTime, bool isUnlockTime) => Task.CompletedTask;

  protected virtual Task FinalizeFill(bool isBoostingAppsChanged, CancellationToken cancellationToken) => Task.CompletedTask;

  protected virtual Task PostAchieve(AppBoostInfo app) => Task.CompletedTask;

  internal virtual TimeSpan GetNextBoostDueTime() => NextAchieveTime - DateTime.Now;

  protected virtual AppBoostInfo[] GetReadyToUnlockApps() => CurrentBoostingApps.Values.ToArray();

  protected abstract Task<List<AppBoostInfo>> FindNewAppsForBoosting(int count, CancellationToken token);

  internal string GetStatus() => CurrentBoostingApps.Count > 0
      ? $"AchievementsBooster is running (mode: {Mode}). Boosting {CurrentBoostingApps.Count} game(s)"
      : $"AchievementsBooster is running (mode: {Mode}), but there are no games to boost";

  internal void Initialize() => Booster.Logger.Info($"Initializing new boosting mode {Mode} ....");

  internal virtual void Stop(bool resume = false) {
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
  }

  internal async Task Boosting(DateTime lastBoosterHeartBeatTime, bool isRestingTime, CancellationToken cancellationToken) {
    await BoosterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    bool isBoostingAppsChanged = false;
    bool isFirstTime = NextAchieveTime == DateTime.MinValue;

    DateTime currentTime = DateTime.Now;
    bool isUnlockTime = !isFirstTime && currentTime >= NextAchieveTime;

#if DEBUG
    Booster.Logger.Trace($"Now        : {currentTime.ToLongTimeString()} ({currentTime.Ticks})");
    Booster.Logger.Trace($"NextAchieve: {NextAchieveTime.ToLongTimeString()} ({NextAchieveTime.Ticks})");
#endif

    try {
      if (isUnlockTime) {
        await Achieve(currentTime, lastBoosterHeartBeatTime, cancellationToken).ConfigureAwait(false);
      }

      if (isRestingTime) {
        return;
      }

      // Pre-fill apps for boosting
      await PreFillApps(isFirstTime, isUnlockTime).ConfigureAwait(false);

      // Fill boosting apps if still available
      isBoostingAppsChanged = await FillBoostingApps(cancellationToken).ConfigureAwait(false);

      // Finalize the fill process
      await FinalizeFill(isBoostingAppsChanged, cancellationToken).ConfigureAwait(false);
    }
    finally {
      if (isFirstTime || isUnlockTime || isBoostingAppsChanged) {
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
        Booster.Logger.Info($"Boosting {CurrentBoostingApps.Count} games, unlock after: {timeRemaining.ToHumanReadable()} ({NextAchieveTime.ToShortTimeString()})");
      }
    }
    else {
      Booster.Logger.Warning("Not handing here, log in specific engine");
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

      await PostAchieve(app).ConfigureAwait(false);
    }
  }

  protected async Task<bool> FillBoostingApps(CancellationToken cancellationToken) {
    int availableBoostSlots = BoosterConfig.Global.MaxConcurrentlyBoostingApps - CurrentBoostingApps.Count;
    if (availableBoostSlots > 0) {
      List<AppBoostInfo> newApps = await FindNewAppsForBoosting(availableBoostSlots, cancellationToken).ConfigureAwait(false);
      if (newApps.Count > 0) {
        newApps.ForEach(app => CurrentBoostingApps.TryAdd(app.ID, app));
        return true;
      }
    }
    return false;
  }
}

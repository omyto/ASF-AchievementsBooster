using System;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Booster;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

internal sealed class BoostCoordinator {

  internal SteamClientHandler SteamClientHandler => Bot.SteamClientHandler;

  private readonly BoosterBot Bot;
  private Logger Logger => Bot.Logger;

  private Booster.Booster? Booster { get; set; }

  private Timer? BoosterHeartBeatTimer { get; set; }
  private DateTime LastBoosterHeartBeatTime { get; set; }
  private SemaphoreSlim BoosterHeartBeatSemaphore { get; } = new SemaphoreSlim(1);

  private CancellationTokenSource CancellationTokenSource { get; set; } = new();

  internal BoostCoordinator(Bot bot) => Bot = new BoosterBot(bot);

  ~BoostCoordinator() => StopTimer();

  private void StopTimer() {
    CancellationTokenSource.Cancel();
    BoosterHeartBeatTimer?.Dispose();
    BoosterHeartBeatTimer = null;
  }

  internal Task OnSteamCallbacksInit(CallbackManager callbackManager) {
    ArgumentNullException.ThrowIfNull(callbackManager);
    SteamClientHandler.Init();
    _ = callbackManager.Subscribe<SteamUser.PlayingSessionStateCallback>(OnPlayingSessionState);
    return Task.CompletedTask;
  }

  internal string Start(bool command = false) {
    if (BoosterHeartBeatTimer != null) {
      Logger.Trace(Messages.BoostingStarted);
      return Messages.BoostingStarted;
    }

    TimeSpan dueTime = TimeSpan.Zero;
    if (command) {
      Logger.Info("The boosting process is starting");
    }
    else {
      dueTime = TimeSpan.FromMinutes(Constants.AutoStartDelayTime);
      Logger.Info($"The boosting process will begin in {Constants.AutoStartDelayTime} minutes");
    }

    BoosterHeartBeatTimer = new Timer(Heartbeating, null, dueTime, Timeout.InfiniteTimeSpan);
    return Strings.Done;
  }

  internal string Stop() {
    if (BoosterHeartBeatTimer == null) {
      Logger.Trace(Messages.BoostingNotStart);
      return Messages.BoostingNotStart;
    }

    StopTimer();
    Booster?.StopPlay(true);

    Logger.Info("The boosting process has been stopped!");
    return Strings.Done;
  }

  private async void Heartbeating(object? state) {
    if (!BoosterHeartBeatSemaphore.Wait(0)) {
      Logger.Warning("The boosting process is currently running !!!");
      return;
    }

    Logger.Trace("Boosting heartbeating ...");

    bool isRestingTime = false;
    DateTime currentTime = DateTime.Now;

    CancellationTokenSource = new CancellationTokenSource();
    CancellationToken cancellationToken = CancellationTokenSource.Token;

    try {
      if (Bot.IsPlayingPossible) {
        if (await Bot.UpdateOwnedGames(cancellationToken).ConfigureAwait(false)) {
          EBoostMode newMode = Bot.DetermineBoostMode();
          if (newMode != Booster?.Mode) {
            Booster?.StopPlay();

            Booster = newMode switch {
              EBoostMode.CardFarming => new CardFarmingBooster(Bot),
              EBoostMode.IdleGaming => new IdleGamingBooster(Bot),
              EBoostMode.AutoBoost => new AutoBooster(Bot),
              _ => throw new NotImplementedException()
            };
          }

          isRestingTime = IsRestingTime(currentTime);
          await Booster.Boosting(LastBoosterHeartBeatTime, isRestingTime, cancellationToken).ConfigureAwait(false);
        }
        else {
          Logger.Info(Messages.NoGamesBoosting);
        }
      }
    }
    catch (Exception exception) {
      if (exception is OperationCanceledException ex) {
        Logger.Warning($"The boosting process has been canceled: {ex.Message} ({cancellationToken.IsCancellationRequested})");
      }
      else {
        Logger.Exception(exception);
      }

      Booster?.StopPlay(true);
    }
    finally {
      LastBoosterHeartBeatTime = currentTime;

      if (BoosterHeartBeatTimer != null) {
        // Due time for the next boosting
        TimeSpan dueTime;

        if (!Bot.IsPlayingPossible) {
          dueTime = TimeSpan.FromMinutes(5);
          Logger.Info(Messages.BoostingImpossible);
          Booster?.StopPlay();
        }
        else if (isRestingTime) {
          dueTime = TimeSpan.FromMinutes(AchievementsBoosterPlugin.GlobalConfig.RestTimePerDay);
          Logger.Info(Messages.RestTime);
          Booster?.StopPlay(true);
        }
        else {
          dueTime = TimeSpanUtils.RandomInMinutesRange(AchievementsBoosterPlugin.GlobalConfig.MinBoostInterval, AchievementsBoosterPlugin.GlobalConfig.MaxBoostInterval);
        }

        _ = BoosterHeartBeatTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
        Logger.Trace($"The next heartbeat will occur in {dueTime.Minutes} minutes{(dueTime.Seconds > 0 ? $" and {dueTime.Seconds} seconds" : "")}!");
      }

      _ = BoosterHeartBeatSemaphore.Release();
    }
  }

  private static bool IsRestingTime(DateTime currentTime) {
    if (AchievementsBoosterPlugin.GlobalConfig.RestTimePerDay == 0) {
      return false;
    }

    DateTime weakUpTime = new(currentTime.Year, currentTime.Month, currentTime.Day, 6, 0, 0, 0);
    if (currentTime < weakUpTime) {
      DateTime restingStartTime = weakUpTime.AddMinutes(-AchievementsBoosterPlugin.GlobalConfig.RestTimePerDay);
      if (currentTime > restingStartTime) {
        return true;
      }
    }

    return false;
  }

  private void OnPlayingSessionState(SteamUser.PlayingSessionStateCallback callback) {
    ArgumentNullException.ThrowIfNull(callback);

    if (callback.PlayingBlocked) {
      CancellationTokenSource.Cancel();
    }
  }
}

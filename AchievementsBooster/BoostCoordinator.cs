using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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

  private BaseBooster? Booster { get; set; }

  private Timer? BoosterHeartBeatTimer { get; set; }
  private DateTime LastBoosterHeartBeatTime { get; set; }
  private SemaphoreSlim BoosterHeartBeatSemaphore { get; } = new SemaphoreSlim(1);

  private CancellationTokenSource CancellationTokenSource { get; set; } = new();
  private CancellationToken CancellationToken => CancellationTokenSource.Token;

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

  [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
  internal Task OnInitModules(IReadOnlyDictionary<string, JsonElement> additionalConfigProperties) {
    if (additionalConfigProperties != null) {
    }
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
    Booster?.Stop();
    Booster?.ResumePlay();

    Logger.Info("The boosting process has been stopped!");
    return Strings.Done;
  }

  private async void Heartbeating(object? state) {
    if (!BoosterHeartBeatSemaphore.Wait(0)) {
      Logger.Warning("The boosting process is currently running !!!");
      return;
    }

    Logger.Trace("Boosting heartbeating ...");

    DateTime currentTime = DateTime.Now;
    bool isRestingTime = IsRestingTime(currentTime);
    CancellationTokenSource = new CancellationTokenSource();

    try {
      BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);
      await Bot.UpdateOwnedGames(CancellationToken).ConfigureAwait(false);

      EBoostMode newMode = Bot.DetermineBoostMode();
      if (newMode != Booster?.Mode) {
        Booster?.Stop();

        Booster = newMode switch {
          EBoostMode.CardFarming => new CardFarmingBooster(Bot),
          EBoostMode.IdleGaming => new IdleGamingBooster(Bot),
          EBoostMode.AutoBoost => new AutoBooster(Bot),
          _ => throw new NotImplementedException()
        };
      }

      await Booster.Boosting(LastBoosterHeartBeatTime, isRestingTime, CancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      Logger.Warning($"The boosting process has been canceled: {CancellationToken.IsCancellationRequested}");
      Booster?.Stop();
    }
    catch (Exception exception) {
      if (exception is BoostingImpossibleException) {
        Logger.Info(exception.Message);
      }
      else {
        Logger.Exception(exception);
      }

      Booster?.Stop();
      Booster?.ResumePlay();
    }
    finally {
      LastBoosterHeartBeatTime = currentTime;

      if (BoosterHeartBeatTimer != null) {
        // Due time for the next boosting
        TimeSpan dueTime;
        if (isRestingTime) {
          Booster?.Stop();
          dueTime = TimeSpan.FromMinutes(AchievementsBoosterPlugin.GlobalConfig.RestTimePerDay);
          Logger.Info(Messages.RestTime);
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

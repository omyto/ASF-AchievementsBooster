using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Engine;
using AchievementsBooster.Handler;
using AchievementsBooster.Handler.Exceptions;
using AchievementsBooster.Helper;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Cards;
using SteamKit2;

namespace AchievementsBooster;

internal sealed class Booster : IBooster {

  private Bot Bot { get; }

  internal Logger Logger { get; }

  internal BotCache Cache { get; }

  internal AppManager AppManager { get; }

  internal SteamClientHandler SteamClientHandler { get; }

  internal IReadOnlyCollection<Game> CurrentGamesFarming => Bot.CardsFarmer.CurrentGamesFarmingReadOnly;

  internal ImmutableList<uint>? GamesPlayedWhileIdle => Bot.BotConfig?.GamesPlayedWhileIdle;

  private BoostEngine? Engine { get; set; }

  private Timer? BeatingTimer { get; set; }

  private DateTime LastBeatingTime { get; set; }

  private SemaphoreSlim BeatingSemaphore { get; } = new SemaphoreSlim(1);

  private Timer? DetermineFarmingGamesChangedTimer { get; set; }

  private CancellationTokenSource CancellationTokenSource { get; set; } = new();

  private DateTime LastUpdateOwnedGamesTime { get; set; }

  [field: MaybeNull, AllowNull]
  internal string Identifier {
    get {
      field ??= Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Bot.SSteamID)));
      return field ?? string.Empty;
    }
  }

  internal Booster(Bot bot) {
    Bot = bot;
    Logger = new Logger(bot.ArchiLogger);
    Cache = BotCache.LoadOrCreateCacheForBot(bot);
    SteamClientHandler = new SteamClientHandler(bot, Logger);
    AppManager = new AppManager(this);
  }

  ~Booster() => StopTimer();

  private void StopTimer() {
    CancellationTokenSource.Cancel();
    BeatingTimer?.Dispose();
    BeatingTimer = null;
    DetermineFarmingGamesChangedTimer?.Dispose();
    DetermineFarmingGamesChangedTimer = null;
  }

  internal string Stop() {
    if (BeatingTimer == null) {
      Logger.Trace(Messages.BoostingNotStart);
      return Messages.BoostingNotStart;
    }

    StopTimer();
    Engine?.StopPlay(true);

    Logger.Info("The boosting process has been stopped!");
    return Strings.Done;
  }

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  internal string GetStatus() {
    if (BeatingTimer == null) {
      return string.Join(Environment.NewLine, [
        "AchievementsBooster isn't running. Use the 'abstart' command to start boosting.",
        "To enable automatic startup when ASF launches, add the bot name to the 'AutoStartBots' array in the JSON configuration file."
      ]);
    }

    if (Engine?.CurrentBoostingAppsCount > 0) {
      return $"AchievementsBooster is running (mode: {Engine.Mode}). Boosting {Engine.CurrentBoostingAppsCount} game(s)";
    }

    return "AchievementsBooster is running, but there are no games to boost";
  }

  private async void DetermineFarmingGamesChanged(object? state) {
    if (BeatingTimer != null && Engine != null && Engine.Mode == EBoostMode.CardFarming) {
      await BeatingSemaphore.WaitAsync().ConfigureAwait(false);
      try {
        bool isFarmingGamesChanged = true;
        foreach (Game game in CurrentGamesFarming) {
          if (Engine.CurrentGamesBoostingReadOnly.Contains(game.AppID)) {
            isFarmingGamesChanged = false;
            break;
          }
        }

        if (isFarmingGamesChanged) {
          Logger.Info("Farming games have changed, restarting the boosting process ...");
          _ = BeatingTimer.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        }
      }
      finally {
        _ = BeatingSemaphore.Release();
      }
    }
  }

  private async void Beating(object? state) {
    if (!BeatingSemaphore.Wait(0)) {
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
        if (await UpdateOwnedGames(cancellationToken).ConfigureAwait(false)) {
          EBoostMode newMode = DetermineBoostMode();
          if (newMode != Engine?.Mode) {
            Engine?.StopPlay();

            Engine = newMode switch {
              EBoostMode.CardFarming => new CardFarmingAuxiliaryEngine(this),
              EBoostMode.IdleGaming => new GameIdlingAuxiliaryEngine(this),
              EBoostMode.AutoBoost => new AutoBoostingEngine(this),
              _ => throw new NotImplementedException()
            };
          }

          if (Engine.Mode == EBoostMode.CardFarming) {
            DetermineFarmingGamesChangedTimer ??= new Timer(DetermineFarmingGamesChanged, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
          }
          else if (DetermineFarmingGamesChangedTimer != null) {
            DetermineFarmingGamesChangedTimer.Dispose();
            DetermineFarmingGamesChangedTimer = null;
          }

          isRestingTime = IsRestingTime(currentTime);
          await Engine.Boosting(LastBeatingTime, isRestingTime, cancellationToken).ConfigureAwait(false);
        }
        else {
          Logger.Info(Messages.NoGamesBoosting);
        }
      }
    }
    catch (Exception exception) {
      if (exception is BotDisconnectedException) {
        Logger.Warning("Boosting canceled: bot disconnected from Steam");
      }
      else if (exception is OperationCanceledException ex) {
        if (cancellationToken.IsCancellationRequested) {
          Logger.Warning("Boosting canceled: bot is disconnected or in use");
        }
        else {
          Logger.Warning("Boosting canceled");
          Logger.Warning(ex);
        }
      }
      else {
        Logger.Exception(exception);
      }

      Engine?.StopPlay(true);
    }
    finally {
      LastBeatingTime = currentTime;

      if (BeatingTimer != null) {
        // Due time for the next boosting
        TimeSpan dueTime;

        if (!Bot.IsPlayingPossible) {
          dueTime = TimeSpan.FromMinutes(5);
          Logger.Info(Messages.BoostingImpossible);
          Engine?.StopPlay();
        }
        else if (isRestingTime) {
          dueTime = TimeSpan.FromMinutes(AchievementsBoosterPlugin.GlobalConfig.RestTimePerDay);
          Logger.Info(Messages.RestTime);
          Engine?.StopPlay(true);
        }
        else {
          dueTime = TimeSpanUtils.RandomInMinutesRange(AchievementsBoosterPlugin.GlobalConfig.MinBoostInterval, AchievementsBoosterPlugin.GlobalConfig.MaxBoostInterval);
        }

        _ = BeatingTimer.Change(dueTime, Timeout.InfiniteTimeSpan);

        string timeRemainingMessage = $"{dueTime.Minutes} minutes{(dueTime.Seconds > 0 ? $" and {dueTime.Seconds} seconds" : "")}";
        if (Engine?.CurrentBoostingAppsCount > 0) {
          Logger.Info($"Boosting {Engine.CurrentBoostingAppsCount} games, unlock achievements after {timeRemainingMessage}.");
        }
        else {
          Logger.Info($"Next check after {timeRemainingMessage}.");
        }
      }

      _ = BeatingSemaphore.Release();
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
      Logger.Warning(Strings.BotStatusPlayingNotAvailable);
      CancellationTokenSource.Cancel();
    }
  }

  internal EBoostMode DetermineBoostMode() => CurrentGamesFarming.Count > 0
        ? EBoostMode.CardFarming
        : GamesPlayedWhileIdle?.Count > 0
          ? EBoostMode.IdleGaming
          : EBoostMode.AutoBoost;

  internal async Task<(bool Success, string Message)> PlayGames(IReadOnlyCollection<uint> gameIDs)
    => await Bot.Actions.Play(gameIDs).ConfigureAwait(false);

  internal (bool Success, string Message) ResumePlay() => Bot.Actions.Resume();

  internal async Task<bool> UpdateOwnedGames(CancellationToken cancellationToken) {
    DateTime now = DateTime.Now;
    if (AppManager.OwnedGames.Count == 0 || (now - LastUpdateOwnedGamesTime).TotalHours > 12.0) {
      Dictionary<uint, string>? ownedGames = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false);
      if (ownedGames != null) {
        await AppManager.UpdateQueue(ownedGames.Keys.ToHashSet(), cancellationToken).ConfigureAwait(false);
        LastUpdateOwnedGamesTime = now;
      }
    }

    return AppManager.OwnedGames.Count > 0;
  }


  /** IBooster implementation */
  public Task OnDisconnected(EResult reason) {
    Logger.Warning(Strings.BotDisconnected);
    _ = Stop();
    return Task.CompletedTask;
  }

  public Task OnSteamCallbacksInit(CallbackManager callbackManager) {
    ArgumentNullException.ThrowIfNull(callbackManager);
    SteamClientHandler.Init();
    _ = callbackManager.Subscribe<SteamUser.PlayingSessionStateCallback>(OnPlayingSessionState);
    return Task.CompletedTask;
  }

  public string Start(bool command = false) {
    if (BeatingTimer != null) {
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

    BeatingTimer = new Timer(Beating, null, dueTime, Timeout.InfiniteTimeSpan);
    return Strings.Done;
  }
}

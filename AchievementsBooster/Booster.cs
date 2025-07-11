using System;
using System.Collections.Generic;
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
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

internal sealed class Booster : IBooster {

  internal Bot Bot { get; }

  internal Logger Logger { get; }

  internal BotCache Cache { get; }

  internal AppManager AppManager { get; }

  internal SteamClientHandler SteamClientHandler { get; }

  private BoostEngine? Engine { get; set; }

  private Timer? BeatingTimer { get; set; }

  private DateTime LastBeatingTime { get; set; }

  private SemaphoreSlim BeatingSemaphore { get; } = new SemaphoreSlim(1);

  private CancellationTokenSource CancellationTokenSource { get; set; } = new();

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
        if (await AppManager.UpdateOwnedGames(cancellationToken).ConfigureAwait(false)) {
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

          isRestingTime = IsRestingTime(currentTime);
          await Engine.Boosting(LastBeatingTime, isRestingTime, cancellationToken).ConfigureAwait(false);
        }
        else {
          Logger.Info(Messages.NoGamesBoosting);
        }
      }
      else {
        Logger.Info(Messages.BoostingImpossible);
        Engine?.StopPlay();
        Engine = null;
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
        // Due time for the next beating
        TimeSpan dueTime;

        if (isRestingTime) {
          Logger.Info(Messages.RestTime);
          Engine?.StopPlay(true);
          Engine = null;
          dueTime = TimeSpan.FromMinutes(AchievementsBoosterPlugin.GlobalConfig.RestTimePerDay);
        }
        else {
          dueTime = Engine?.GetNextBoostDueTime() ?? TimeSpan.FromMinutes(10);
        }

        _ = BeatingTimer.Change(dueTime, Timeout.InfiniteTimeSpan);

        if (Engine?.CurrentBoostingAppsCount > 0) {
          TimeSpan achieveTimeRemaining = Engine.NextAchieveTime - DateTime.Now;
          Logger.Info($"Boosting {Engine.CurrentBoostingAppsCount} games, unlock achievements after: {achieveTimeRemaining.ToHumanReadable()}.");
        }
        else {
          Logger.Info($"Next check after: {dueTime.ToHumanReadable()}.");
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

  internal EBoostMode DetermineBoostMode() => Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count > 0
        ? EBoostMode.CardFarming
        : Bot.BotConfig.GamesPlayedWhileIdle.Count > 0
          ? EBoostMode.IdleGaming
          : EBoostMode.AutoBoost;

  internal async Task<(bool Success, string Message)> PlayGames(IReadOnlyCollection<uint> gameIDs)
    => await Bot.Actions.Play(gameIDs).ConfigureAwait(false);

  internal (bool Success, string Message) ResumePlay() => Bot.Actions.Resume();

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

  internal async Task<string> Debug(string[] args) {
    Logger.Debug($"Debuging with args: {string.Join(" ", args)}");

    switch (args[0].ToUpperInvariant()) {
      case "PRODUCT":
      case "INFO":
        await DataDumper.DumpProductsInfo(this, args[1].Split(',').Select(uint.Parse).ToList()).ConfigureAwait(false);
        break;
      default:
        Logger.Warning($"Unknown debug command: {args[0]}");
        break;
    }

    await Task.Delay(100).ConfigureAwait(false);
    return Strings.Done;
  }
}

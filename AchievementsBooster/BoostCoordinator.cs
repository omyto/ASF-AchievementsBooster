using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Booster;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

internal sealed class BoostCoordinator {

  private static BoosterGlobalConfig GlobalConfig => AchievementsBoosterPlugin.GlobalConfig;

  internal BoosterHandler BoosterHandler => AppManager.BoosterHandler;

  private AppManager AppManager { get; }

  private readonly Bot Bot;
  private readonly BotCache Cache;
  private readonly Logger Logger;

  private BaseBooster? Booster { get; set; }

  private Timer? BoosterHeartBeatTimer { get; set; }
  private DateTime LastBoosterHeartBeatTime { get; set; }
  private DateTime LastUpdateOwnedGamesTime { get; set; }
  private SemaphoreSlim BoosterHeartBeatSemaphore { get; } = new SemaphoreSlim(1);

  private CancellationTokenSource CancellationTokenSource { get; set; } = new();

  internal BoostCoordinator(Bot bot) {
    Bot = bot;
    Logger = new Logger(bot.ArchiLogger);
    Cache = LoadOrCreateCacheForBot(bot);
    AppManager = new AppManager(new BoosterHandler(bot, Logger), Cache, Logger);
  }

  ~BoostCoordinator() => StopTimer();

  private void StopTimer() {
    CancellationTokenSource.Cancel();
    BoosterHeartBeatTimer?.Dispose();
    BoosterHeartBeatTimer = null;
  }

  internal Task OnSteamCallbacksInit(CallbackManager callbackManager) {
    ArgumentNullException.ThrowIfNull(callbackManager);
    BoosterHandler.Init();
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
    CancellationTokenSource = new CancellationTokenSource();

    try {
      BoostingImpossibleException.ThrowIfPlayingImpossible(!Bot.IsPlayingPossible);

      if (IsRestingTime(currentTime)) {
        throw new BoostingImpossibleException(Messages.RestTime);
      }

      await UpdateOwnedGames(currentTime, CancellationTokenSource.Token).ConfigureAwait(false);
      if (AppManager.OwnedGames.Count == 0) {
        Booster?.Stop();
        throw new BoostingImpossibleException(string.Format(CultureInfo.CurrentCulture, Messages.NoGamesBoosting));
      }

      EBoostMode newMode = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count > 0
        ? EBoostMode.CardFarming
        : Bot.BotConfig?.GamesPlayedWhileIdle.Count > 0
          ? EBoostMode.IdleGaming
          : EBoostMode.AutoBoost;

      if (newMode != Booster?.Mode) {
        Booster?.Stop();

        Booster = newMode switch {
          EBoostMode.CardFarming => new CardFarmingBooster(Bot, Cache, AppManager),
          EBoostMode.IdleGaming => new IdleGamingBooster(Bot, Cache, AppManager, Bot.BotConfig?.GamesPlayedWhileIdle),
          EBoostMode.AutoBoost => new AutoBooster(Bot, Cache, AppManager),
          _ => throw new NotImplementedException()
        };
      }

      await Booster.Boosting(LastBoosterHeartBeatTime, CancellationTokenSource.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      Logger.Warning($"The boosting process has been canceled: {CancellationTokenSource.Token.IsCancellationRequested}");
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
        TimeSpan dueTime = TimeSpanUtils.RandomInMinutesRange(GlobalConfig.MinBoostInterval, GlobalConfig.MaxBoostInterval);
        _ = BoosterHeartBeatTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
        Logger.Trace($"The next heartbeat will occur in {dueTime.Minutes} minutes{(dueTime.Seconds > 0 ? $" and {dueTime.Seconds} seconds" : "")}!");
      }
      _ = BoosterHeartBeatSemaphore.Release();
    }
  }

  private async Task UpdateOwnedGames(DateTime currentTime, CancellationToken cancellationToken) {
    if (AppManager.OwnedGames.Count == 0 || (currentTime - LastUpdateOwnedGamesTime).TotalHours > 12.0) {
      Dictionary<uint, string>? ownedGames = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false);
      if (ownedGames != null) {
        await AppManager.UpdateOwnedGames(ownedGames.Keys.ToHashSet(), cancellationToken).ConfigureAwait(false);
        LastUpdateOwnedGamesTime = currentTime;
      }
    }
  }

  private BotCache LoadOrCreateCacheForBot(Bot bot) {
    if (bot.BotDatabase == null) {
      throw new InvalidOperationException(nameof(bot.BotDatabase));
    }

    BotCache? cache = null;
    JsonElement jsonElement = bot.BotDatabase.LoadFromJsonStorage(Constants.BotCacheKey);
    if (jsonElement.ValueKind == JsonValueKind.Object) {
      try {
        cache = jsonElement.ToJsonObject<BotCache>();
      }
      catch (Exception ex) {
        Logger.Exception(ex);
      }
    }

    cache ??= new BotCache();
    cache.Init(bot.BotDatabase);

    return cache;
  }

  private static bool IsRestingTime(DateTime currentTime) {
    if (GlobalConfig.RestTimePerDay == 0) {
      return false;
    }

    DateTime weakUpTime = new(currentTime.Year, currentTime.Month, currentTime.Day, 6, 0, 0, 0);
    if (currentTime < weakUpTime) {
      DateTime restingStartTime = weakUpTime.AddMinutes(-GlobalConfig.RestTimePerDay);
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

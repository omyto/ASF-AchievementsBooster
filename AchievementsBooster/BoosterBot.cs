using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Booster;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Cards;

namespace AchievementsBooster;

internal sealed class BoosterBot {
  internal BotCache Cache { get; }
  internal Logger Logger { get; }
  internal AppManager AppManager { get; }
  internal SteamClientHandler SteamClientHandler { get; }

  internal ulong SteamID => ASFBot.SteamID;

  internal bool IsPlayingPossible => ASFBot.IsPlayingPossible;

  internal IReadOnlyCollection<Game> CurrentGamesFarming => ASFBot.CardsFarmer.CurrentGamesFarmingReadOnly;

  internal ImmutableList<uint>? GamesPlayedWhileIdle => ASFBot.BotConfig?.GamesPlayedWhileIdle;

  private DateTime LastUpdateOwnedGamesTime { get; set; }
  private Bot ASFBot { get; }

  internal BoosterBot(Bot bot) {
    ASFBot = bot;
    Logger = new Logger(bot.ArchiLogger);
    Cache = LoadOrCreateCacheForBot(bot);
    SteamClientHandler = new SteamClientHandler(bot, Logger);
    AppManager = new AppManager(SteamClientHandler, Cache, Logger);
  }

  internal EBoostMode DetermineBoostMode() => CurrentGamesFarming.Count > 0
        ? EBoostMode.CardFarming
        : GamesPlayedWhileIdle?.Count > 0
          ? EBoostMode.IdleGaming
          : EBoostMode.AutoBoost;

  internal async Task<(bool Success, string Message)> PlayGames(IReadOnlyCollection<uint> gameIDs)
    => await ASFBot.Actions.Play(gameIDs).ConfigureAwait(false);

  internal (bool Success, string Message) ResumePlay() => ASFBot.Actions.Resume();

  internal async Task<bool> UpdateOwnedGames(CancellationToken cancellationToken) {
    DateTime now = DateTime.Now;
    if (AppManager.OwnedGames.Count == 0 || (now - LastUpdateOwnedGamesTime).TotalHours > 12.0) {
      Dictionary<uint, string>? ownedGames = await ASFBot.ArchiHandler.GetOwnedGames(SteamID).ConfigureAwait(false);
      if (ownedGames != null) {
        await AppManager.UpdateOwnedGames(ownedGames.Keys.ToHashSet(), cancellationToken).ConfigureAwait(false);
        LastUpdateOwnedGamesTime = now;
      }
    }

    return AppManager.OwnedGames.Count > 0;
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
}

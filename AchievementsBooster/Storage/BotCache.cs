using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementsBooster.Helper;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Storage;

namespace AchievementsBooster.Storage;

internal sealed class BotCache {
  [JsonIgnore]
  private bool Initialized = false;

  [JsonIgnore]
  private BotDatabase? BotDatabase;

  [JsonDisallowNull]
  [JsonInclude]
  internal ConcurrentHashSet<uint> PerfectGames { get; private init; } = [];

  [JsonConstructor]
  internal BotCache() {
  }

  ~BotCache() {
    BotDatabase = null;
    PerfectGames.OnModified -= OnObjectModified;
    Initialized = false;
  }

  internal void Init(BotDatabase botDatabase) {
    if (!Initialized) {
      BotDatabase = botDatabase;
      PerfectGames.OnModified += OnObjectModified;
      Initialized = true;
    }
  }

  private void OnObjectModified(object? sender, EventArgs e) => BotDatabase?.SaveToJsonStorage(Constants.GlobalCacheKey, this);

  /* [UsedImplicitly] */
  public bool ShouldSerializePerfectGames() => PerfectGames.Count > 0;

  /** Static */
  internal static BotCache LoadOrCreateCacheForBot(Bot bot) {
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
        Logger.Shared.Warning($"Failed to load bot cache for bot {bot.BotName}");
        Logger.Shared.Exception(ex);
      }
    }

    cache ??= new BotCache();
    cache.Init(bot.BotDatabase);

    return cache;
  }
}

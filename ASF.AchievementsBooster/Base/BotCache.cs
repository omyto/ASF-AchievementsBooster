using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;

namespace AchievementsBooster.Base;

internal sealed class BotCache {
  [JsonIgnore]
  private Bot? Bot;

  [JsonIgnore]
  private bool Initialized = false;

  [JsonDisallowNull]
  [JsonInclude]
  internal ConcurrentHashSet<uint> PerfectGames { get; private init; } = [];

  //[JsonDisallowNull]
  //[JsonInclude]
  //internal ConcurrentHashSet<uint> NonBoostableGames { get; private init; } = [];

  internal static BotCache? LoadFromDatabase(Bot bot) {
    if (bot.BotDatabase == null) {
      throw new InvalidOperationException(nameof(bot.BotDatabase));
    }

    JsonElement jsonElement = bot.BotDatabase.LoadFromJsonStorage(Constants.BotCacheKey);
    if (jsonElement.ValueKind == JsonValueKind.Object) {
      try {
        BotCache? cache = jsonElement.ToJsonObject<BotCache>();
        if (cache != null) {
          cache.Bot = bot;
          return cache;
        }
      } catch (Exception ex) {
        bot.ArchiLogger.LogGenericException(ex, Caller.Name());
      }
    }

    return null;
  }

  internal BotCache(Bot bot) : this() => Bot = bot;

  [JsonConstructor]
  private BotCache() {
  }

  ~BotCache() => Destroy();

  internal void Init() {
    if (!Initialized) {
      PerfectGames.OnModified += OnObjectModified;
      //NonBoostableGames.OnModified += OnObjectModified;
      Initialized = true;
    }
  }

  internal void Destroy() {
    if (Initialized) {
      PerfectGames.OnModified -= OnObjectModified;
      //NonBoostableGames.OnModified -= OnObjectModified;
      Initialized = false;
    }
  }

  private void OnObjectModified(object? sender, EventArgs e) => Bot?.BotDatabase.SaveToJsonStorage(Constants.GlobalCacheKey, this);

  [UsedImplicitly]
  public bool ShouldSerializePerfectGames() => PerfectGames.Count > 0;

  //[UsedImplicitly]
  //public bool ShouldSerializeNonBoostableGames() => NonBoostableGames.Count > 0;
}

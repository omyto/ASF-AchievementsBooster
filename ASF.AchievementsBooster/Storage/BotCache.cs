using System;
using System.Text.Json.Serialization;
using AchievementsBooster.Base;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Steam.Storage;
using JetBrains.Annotations;

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

  ~BotCache() => Destroy();

  internal void Init(BotDatabase botDatabase) {
    if (!Initialized) {
      BotDatabase = botDatabase;
      PerfectGames.OnModified += OnObjectModified;
      Initialized = true;
    }
  }

  internal void Destroy() {
    if (Initialized) {
      BotDatabase = null;
      PerfectGames.OnModified -= OnObjectModified;
      Initialized = false;
    }
  }

  private void OnObjectModified(object? sender, EventArgs e) => BotDatabase?.SaveToJsonStorage(Constants.GlobalCacheKey, this);

  [UsedImplicitly]
  public bool ShouldSerializePerfectGames() => PerfectGames.Count > 0;
}

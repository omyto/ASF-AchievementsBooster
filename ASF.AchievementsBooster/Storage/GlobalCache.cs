using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementsBooster.Base;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using JetBrains.Annotations;

namespace AchievementsBooster.Storage;

internal sealed class GlobalCache {
  [JsonIgnore]
  private bool Initialized = false;

  [JsonDisallowNull]
  [JsonInclude]
  internal ConcurrentHashSet<uint> NonAchievementApps { get; private init; } = [];

  [JsonDisallowNull]
  [JsonInclude]
  internal ConcurrentHashSet<uint> VACApps { get; private init; } = [];

  internal static GlobalCache? LoadFromDatabase() {
    if (ASF.GlobalDatabase == null) {
      throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
    }
    JsonElement jsonElement = ASF.GlobalDatabase.LoadFromJsonStorage(Constants.GlobalCacheKey);
    return jsonElement.ValueKind == JsonValueKind.Object ? jsonElement.ToJsonObject<GlobalCache?>() : null;
  }

  [JsonConstructor]
  internal GlobalCache() {
  }

  ~GlobalCache() => Destroy();

  internal void Init() {
    if (!Initialized) {
      VACApps.OnModified += OnObjectModified;
      NonAchievementApps.OnModified += OnObjectModified;
      Initialized = true;
    }
  }

  internal void Destroy() {
    if (Initialized) {
      VACApps.OnModified -= OnObjectModified;
      NonAchievementApps.OnModified -= OnObjectModified;
      Initialized = false;
    }
  }

  private void OnObjectModified(object? sender, EventArgs e) => ASF.GlobalDatabase?.SaveToJsonStorage(Constants.GlobalCacheKey, this);

  [UsedImplicitly]
  public bool ShouldSerializeNonAchievementApps() => NonAchievementApps.Count > 0;

  [UsedImplicitly]
  public bool ShouldSerializeVACApps() => VACApps.Count > 0;
}

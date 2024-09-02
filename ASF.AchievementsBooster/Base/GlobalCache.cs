using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using JetBrains.Annotations;

namespace AchievementsBooster.Base;

internal sealed class GlobalCache {

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
    NonAchievementApps.OnModified += OnObjectModified;
    VACApps.OnModified += OnObjectModified;
  }

  ~GlobalCache() {
    NonAchievementApps.OnModified -= OnObjectModified;
    VACApps.OnModified -= OnObjectModified;
  }

  private void OnObjectModified(object? sender, EventArgs e) => ASF.GlobalDatabase?.SaveToJsonStorage(Constants.GlobalCacheKey, this);

  [UsedImplicitly]
  public bool ShouldSerializeNonAchievementApps() => NonAchievementApps.Count > 0;

  [UsedImplicitly]
  public bool ShouldSerializeVACApps() => VACApps.Count > 0;
}

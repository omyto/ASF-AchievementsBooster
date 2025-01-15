using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AchievementsBooster.Data;

public sealed class AchievementsFilterResponse {
  [JsonPropertyName("success")]
  public bool? Success { get; internal set; }

  [JsonPropertyName("data")]
  public IList<uint>? AppIDs { get; internal set; }

  [JsonConstructor]
  internal AchievementsFilterResponse() { }
}

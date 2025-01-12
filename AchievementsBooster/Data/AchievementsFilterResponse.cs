using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AchievementsBooster.Data;

internal class AchievementsFilterResponse {
  [JsonPropertyName("success")]
  public bool? Success { get; set; }

  [JsonPropertyName("data")]
  public List<uint>? AppIDs { get; set; }
}

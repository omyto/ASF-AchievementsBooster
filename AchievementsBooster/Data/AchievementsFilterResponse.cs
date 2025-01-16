using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AchievementsBooster.Data;

[SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "<Pending>")]
[SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "<Pending>")]
public sealed class AchievementsFilterResponse {
  [JsonPropertyName("success")]
  public bool? Success { get; set; }

  [JsonPropertyName("data")]
  public List<uint>? AppIDs { get; set; }
}

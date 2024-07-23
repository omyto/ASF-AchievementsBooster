using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace AchievementsBooster.App;

public class GameAchievementsResponse {
  [JsonPropertyName("achievements")]
  public ImmutableList<GameAchievement>? Achievements { get; set; }
}

public class GameAchievement {
  [JsonPropertyName("internal_name")]
  public string? InternalName { get; set; }

  [JsonPropertyName("localized_name")]
  public string? LocalizedName { get; set; }

  [JsonPropertyName("localized_desc")]
  public string? LocalizedDesc { get; set; }

  [JsonPropertyName("icon")]
  public string? Icon { get; set; }

  [JsonPropertyName("icon_gray")]
  public string? IconGray { get; set; }

  [JsonPropertyName("hidden")]
  public bool Hidden { get; set; } = false;

  [JsonPropertyName("player_percent_unlocked")]
  public string? PlayerPercentUnlocked { get; set; }
}

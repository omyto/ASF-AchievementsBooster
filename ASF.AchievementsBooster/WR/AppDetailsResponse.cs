using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace AchievementsBooster.WR;

public class AppDetailsResponse {
  [JsonPropertyName("success")]
  public bool? Success { get; set; }

  [JsonPropertyName("data")]
  public AppDetails? Data { get; set; }
}

public class AppDetails {
  [JsonPropertyName("type")]
  public string? Type { get; set; }

  [JsonPropertyName("name")]
  public string? Name { get; set; }

  [JsonPropertyName("steam_appid")]
  public uint? SteamAppId { get; set; }

  [JsonPropertyName("is_free")]
  public bool? IsFree { get; set; }

  [JsonPropertyName("developers")]
  public ImmutableList<string>? Developers { get; set; }

  [JsonPropertyName("publishers")]
  public ImmutableList<string>? Publishers { get; set; }

  [JsonPropertyName("categories")]
  public ImmutableList<AppCategory>? Categories { get; set; }

  [JsonPropertyName("achievements")]
  public AppAchievements? Achievements { get; set; }
}

public class AppCategory {
  [JsonPropertyName("id")]
  public uint Id { get; set; } = default!;

  [JsonPropertyName("description")]
  public string Description { get; set; } = default!;
}

public class AppAchievements {
  [JsonPropertyName("total")]
  public uint? Total { get; set; }
}

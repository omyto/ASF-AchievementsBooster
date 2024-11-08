using System.Collections.Immutable;

namespace AchievementsBooster.Data;

public sealed class ProductInfo {
  public uint ID { get; }

  public string Name { get; }

  public string FullName { get; }

  public string Type { get; internal set; }

  public bool IsBoostable { get; internal set; }

  public string? ReleaseState { get; internal set; }

  public string? SteamReleaseDate { get; internal set; }

  public string? StoreReleaseDate { get; internal set; }

  public bool IsVACEnabled { get; internal set; }

  public ImmutableHashSet<uint> DLCs { get; internal set; } = [];

  public ImmutableHashSet<string> Developers { get; internal set; } = [];

  public ImmutableHashSet<string> Publishers { get; internal set; } = [];

  internal ProductInfo(uint id, string name) {
    ID = id;
    Name = name;
    Type = string.Empty;
    FullName = $"{ID} ({Name})";
  }
}

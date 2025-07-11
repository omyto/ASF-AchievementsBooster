using SteamKit2.Internal;

namespace AchievementsBooster.Model;

internal sealed class AchievementProgress(CPlayer_GetAchievementsProgress_Response.AchievementProgress progress) {
  public uint AppID { get; } = progress.appid;

  public uint Unlocked { get; } = progress.unlocked;

  public uint Total { get; } = progress.total;

  public float Percentage { get; } = progress.percentage;

  public bool AllUnlocked { get; } = progress.all_unlocked;

  public uint CacheTime { get; } = progress.cache_time;

  public bool Vetted { get; } = progress.vetted;
}

using System.Collections.Frozen;
using System.Collections.Generic;
using SteamKit2.Internal;

namespace AchievementsBooster.Model;

/// Achievement completion rates
public sealed class AchievementRates {
  public uint AppID { get; }

  public FrozenDictionary<string, double> Percentages { get; } // { api name : rate percentage }

  internal AchievementRates(uint appID, List<CPlayer_GetGameAchievements_Response.Achievement> achievements) {
    AppID = appID;
    Percentages = achievements.ToFrozenDictionary(k => k.internal_name, v => double.TryParse(v.player_percent_unlocked, out double value) ? value : 0.0);
  }

  public double GetAchievementRate(string apiName, double defaultValue = 0)
    => Percentages.TryGetValue(apiName, out double value) ? value : defaultValue;
}

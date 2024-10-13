using System.Collections.Frozen;

namespace AchievementsBooster.Base;

public sealed class AchievementPercentages {
  public uint AppID { get; }
  private FrozenDictionary<string, double> Dictionary { get; }

  internal AchievementPercentages(uint appID, FrozenDictionary<string, double> dictionary) {
    AppID = appID;
    Dictionary = dictionary;
  }

  public double GetPercentage(string apiName, double defaultValue = 0)
    => Dictionary.TryGetValue(apiName, out double value) ? value : defaultValue;
}

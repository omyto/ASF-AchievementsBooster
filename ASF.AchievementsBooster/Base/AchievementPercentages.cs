using System.Collections.Frozen;

namespace AchievementsBooster.Base;

public sealed class AchievementPercentages {
  public uint AppID { get; }
  public FrozenDictionary<string, double> Percentages { get; }

  internal AchievementPercentages(uint appID, FrozenDictionary<string, double> percentages) {
    AppID = appID;
    Percentages = percentages;
  }

  public double GetPercentage(string apiName, double defaultValue = 0)
    => Percentages.TryGetValue(apiName, out double value) ? value : defaultValue;
}

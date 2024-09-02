
using System.Collections.Frozen;

namespace AchievementsBooster.Base;

internal sealed class AppBooster {
  internal uint ID { get; }

  internal string Name => ProductInfo.Name;

  internal ProductInfo ProductInfo { get; }

  internal FrozenDictionary<string, double> AchievementPercentages { get; }

  internal AppBooster(uint id, ProductInfo info, FrozenDictionary<string, double> achievementPercentages) {
    ID = id;
    ProductInfo = info;
    AchievementPercentages = achievementPercentages;
  }
}

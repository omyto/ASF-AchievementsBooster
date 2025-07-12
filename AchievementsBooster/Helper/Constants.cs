using System;

namespace AchievementsBooster.Helper;

public static class Constants {
  public static readonly Version PluginVersion = typeof(AchievementsBoosterPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(PluginVersion));

  public static readonly string PluginVersionString = PluginVersion.ToString();

  public const string PluginName = "AchievementsBooster";

  public const string RepositoryName = "omyto/ASF-AchievementsBooster";

  public const string AchievementsBoosterConfigKey = PluginName;

  public const string GlobalCacheKey = PluginName;

  public const string BotCacheKey = PluginName;

  public const byte MaxGamesPlayedConcurrently = 32; /** ArchiHandler.MaxGamesPlayedConcurrently */

#if DEBUG
  public const float AutoStartDelayTime = .5f;
#else
  public const float AutoStartDelayTime = 5;
#endif

  public const string VACCategory = "category_8";

  public const string AchievementsCategory = "category_22";

  public const byte MaxUnlockAchievementTries = 3;

  public static readonly Uri AchievementsFilterAPI = new("https://ab.omyto.com/api/filters");

  public static readonly TimeSpan OneSeconds = TimeSpan.FromSeconds(1);

  public static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

  public static readonly TimeSpan FiveSeconds = TimeSpan.FromSeconds(5);

  public static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
}

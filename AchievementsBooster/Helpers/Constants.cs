namespace AchievementsBooster.Helpers;

public static class Constants {
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
}

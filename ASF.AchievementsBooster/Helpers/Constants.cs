namespace AchievementsBooster.Helpers;

public static class Constants {
  public const string AchievementsBoosterConfigKey = "AchievementsBooster";

  public const string GlobalCacheKey = AchievementsBoosterConfigKey;

  public const string BotCacheKey = AchievementsBoosterConfigKey;

#if DEBUG
  public const float AutoStartDelayTime = .5f;
#else
  public const float AutoStartDelayTime = 5;
#endif

  public const string VACCategory = "category_8";

  public const string AchievementsCategory = "category_22";

  public const byte MaxUnlockAchievementTries = 3;
}

namespace AchievementsBooster.Helpers;

internal static class Messages {
  internal const string AutoStartDisabled = "The automatic execution of AchievementsBooster is currently disabled";

  internal const string ConfigPropertyInvalid = "The configured value {1} of property {0} is invalid! It will be automatically adjusted to {2}";

  internal const string BoosterInitEror = "Can not initialize booster for bot: {0}";

  internal const string BoosterNotFound = "Unable to locate any booster for bot: {0}!";

  internal const string NoGamesBoosting = "The bot doesn't have any games for boosting";

  internal const string BoostingImpossible = "Not ready to boost; playing is blocked";

  internal const string SleepingTime = "Sleeping time";

  internal const string GamesOwned = "Games owned: {0}";

  internal const string GamesAdded = "New games added: {0}";

  internal const string GamesRemoved = "Games was removed: {0}";

  internal const string BoostingApp = "Boosting {0}: {1} achievements remaining";

  internal const string BoostingFailed = "Boosting apps failed; reason: {0}";

  internal const string BoostingStarted = "This booster has already started!";

  internal const string BoostingNotStart = "This booster is currently not running!";

  internal const string BoostingAppComplete = "Finished boosting {0}";

  internal const string NotOwnedGame = "{0} is not owned by the user";

  internal const string AppInASFBlacklist = "App {0} is on the ASF blacklist";

  internal const string InvalidAppDLC = "App {0} has invalid DLC: {1}";

  internal const string NonBoostableApp = "App {0} is not boostable";

  internal const string FoundBoostableApp = "Found  {0}: {1} achievements remaining";

  internal const string AchievementsNotAvailable = "App {0} doesn't have the achievements feature";

  internal const string IgnoreAppWithVAC = "App {0} has the VAC enabled; you configured it to be ignored";

  internal const string IgnoreAppWithDLC = "App {0} has one or more DLCs; you configured it to be ignored";

  internal const string IgnoreDeveloper = "App {0}, developed by '{1}', has been configured to be ignored";

  internal const string IgnorePublisher = "App {0}, published by '{1}', has been configured to be ignored";

  internal const string StatsNotFound = "Unable to locate user stats for app {0}";

  internal const string ProductInfoNotFound = "Can't get product info for app {0}";

  internal const string AchievementPercentagesNotFound = "Can't get global achievement percentages for app {0}";

  internal const string GameAchievementNotExist = "No global achievement percentages exist for app {0}";

  internal const string NoUnlockableStats = "There are no unlockable achievements for app {0}";

  internal const string AlreadyUnlockedAll = "All achievements for app {0} have been unlocked";

  internal const string UnlockAchievementSuccess = "Boosting status for {0}: unlocked the '{1}' achievement";

  internal const string UnlockAchievementFailed = "Failed to achieve achievement '{0}' for app {1}";

  internal const string NoBoostingApps = "No apps available to boost achievements";

  internal const string NoBoostingAppsInArchiFarming = "No apps available to boost achievements during farming cards";

  internal const string NoBoostingAppsInArchiPlayedWhileIdle = "No apps available to boost achievements while playing in idle mode";
}

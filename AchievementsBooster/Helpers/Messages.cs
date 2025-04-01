namespace AchievementsBooster.Helpers;

internal static class Messages {
  internal const string AutoStartBotsEmpty = "There are no bots configured for auto-start. To start the achievement boosting process, please use the command";

  internal const string ConfigPropertyInvalid = "The configured value {1} of property {0} is invalid! It will be automatically adjusted to {2}";

  internal const string BoosterInitEror = "Can not initialize booster for bot: {0}";

  internal const string BoosterNotFound = "Unable to locate any booster for bot: {0}!";

  internal const string NoGamesBoosting = "The bot doesn't have any games for boosting";

  internal const string BoostingImpossible = "Not ready to boost; playing is blocked. Checking again in 5 minutes";

  internal const string RestTime = "Resting time";

  internal const string GamesOwned = "Games owned: {0}";

  internal const string BoostableQueue = "Boostable queue: {0}";

  internal const string RestingApps = "Resting apps: {0}";

  internal const string GamesRemoved = "Games was removed: {0}";

  internal const string BoostingApp = "Boosting {0}: {1} achievements remaining";

  internal const string BoostingFailed = "Boosting apps failed; reason: {0}";

  internal const string BoostingStarted = "This booster has already started!";

  internal const string BoostingNotStart = "This booster is currently not running!";

  internal const string FinishedBoost = "Finished boost app {0}.";

  internal const string FinishedBoostable = "Finished boost app {0}, some achievements have been restricted.";

  internal const string RestingApp = "{0}: taking a rest after boosting for {1} minutes";

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

  internal const string PerfectApp = "{0}: Had unlocked all achievements";

  internal const string NoUnlockableStats = "{0}: No unlockable achievements, some have been restricted.";

  internal const string UnlockAchievementSuccess = "{0}: unlocked the '{1}' achievement";

  internal const string UnlockAchievementFailed = "{0}: failed to achieve achievement '{1}'";

  internal const string NoBoostingApps = "No apps available to boost achievements";

  internal const string NoBoostingAppsInArchiFarming = "No apps available to boost achievements during farming cards";

  internal const string NoBoostingAppsInArchiPlayedWhileIdle = "No apps available to boost achievements while playing in idle mode";
}

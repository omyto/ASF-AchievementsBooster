namespace AchievementsBooster.Helpers;

internal static class Messages {
  internal const string PluginDisabledInConfig = "{0} is currently disabled according to your configuration!";

  internal const string ConfigPropertyInvalid = "The configured value {1} of property {0} is invalid! It will be automatically adjusted to {2}";

  internal const string BoosterInitEror = "Can not initialize booster for bot: {0}";

  internal const string BoosterNotFound = "Unable to locate any booster for bot: {0}!";

  internal const string InvalidAppID = "Invalid app id: {0}";

  internal const string BoostingApps = "Boosting apps {0}";

  internal const string BoostingStarted = "This booster has already started!";

  internal const string BoostingNotStart = "This booster is currently not running!";

  internal const string BoostingAppComplete = "Achievements boosting for app {0}({1}) has been completed";

  internal const string NotOwnedGame = "The app {0} is not owned";

  internal const string AppInASFBlacklist = "The game {0} is in the ASF blacklist configuration";

  internal const string InvalidApp = "The app {0} is not valid";

  internal const string AchievementsNotAvailable = "Achievements are not available for app {0}";

  internal const string VACEnabled = "Valve Anti-Cheat is enabled for app {0}";

  internal const string StatsNotFound = "Unable to locate user stats for app {0}";

  internal const string NoUnlockableStats = "No unlockable stats for app {0}";

  internal const string AlreadyUnlockedAll = "Already achieved all achievements for app {0}";

  internal const string UnlockAchievementSuccess = "Achieved achievement '{0}' for app {1}";

  internal const string UnlockAchievementFailed = "Failed to achieve achievement '{0}' for app {1}";

  internal const string NoBoostingApps = "There are no apps available to boost achievements";

  internal const string NoBoostingAppsInArchiFarming = "There are no apps available to boost achievements during the card farming process";

  internal const string NoBoostingAppsInArchiPlayedWhileIdle = "There are no apps available to boost achievements while playing in idle mode";
}

namespace AchievementsBooster.Helpers;

internal static class Messages {
  internal const string PluginDisabledInConfig = "{0} is currently disabled according to your configuration!";

  internal const string ConfigPropertyInvalid = "The configured value {1} of property {0} is invalid! It will be automatically adjusted to {2}";

  internal const string BoosterInitEror = "Can not initialize booster for bot: {0}";

  internal const string BoosterNotFound = "Unable to locate any booster for bot: {0}!";

  internal const string NoGamesBoosting = "The bot doesn't have any games for boosting";

  internal const string BotNotReadyPlay = "The bot is not ready for play; reason: {0}";

  internal const string Blocked = "blocked";

  internal const string Sleeping = "sleeping";

  internal const string GamesOwned = "Games owned: {0}";

  internal const string GamesAdded = "New games added: {0}";

  internal const string GamesRemoved = "Games was removed: {0}";

  internal const string BoostingApps = "Boosting apps: {0}";

  internal const string BoostingFailed = "Boosting apps failed; reason: {0}";

  internal const string BoostingStarted = "This booster has already started!";

  internal const string BoostingNotStart = "This booster is currently not running!";

  internal const string BoostingAppComplete = "Boosting for app {0} has been completed !!!";

  internal const string NotOwnedGame = "{0} is not owned by the user";

  internal const string AppInASFBlacklist = "The app {0} is included in the ASF blacklist configuration";

  internal const string InvalidAppDLC = "The DLC {0} for app {1} is not valid";

  internal const string NonBoostableApp = "The app {0} is not boostable";

  internal const string BoostableApp = "App {0} is boostable, {1} achievements remaining";

  internal const string WillBoostApp = "Will boost {0}, {1} achievements remaining";

  internal const string AchievementsNotAvailable = "Achievements are not available for app {0}";

  internal const string IgnoreAppWithVAC = "Valve Anti-Cheat is enabled for app {0}";

  internal const string IgnoreAppWithDLC = "The app {0} has one or more DLCs";

  internal const string IgnoreDeveloper = "The app {0} developed by '{1}' is on the boosting ignore list";

  internal const string IgnorePublisher = "The app {0} published by '{1}' is on the boosting ignore list";

  internal const string StatsNotFound = "Unable to locate user stats for app {0}";

  internal const string ProductInfoNotFound = "Can't get product info for app {0}";

  internal const string AchievementPercentagesNotFound = "Can't get global achievement percentages for app {0}";

  internal const string GameAchievementNotExist = "No global achievement percentages exist for app {0}";

  internal const string NoUnlockableStats = "There are no unlockable achievements for app {0}";

  internal const string AlreadyUnlockedAll = "All achievements for app {0} have been unlocked";

  internal const string UnlockAchievementSuccess = "The '{0}' achievement in app {1} was unlocked successfully";

  internal const string UnlockAchievementFailed = "Failed to achieve achievement '{0}' for app {1}";

  internal const string NoBoostingApps = "There are no apps available to boost achievements";

  internal const string NoBoostingAppsInArchiFarming = "There are no apps available to boost achievements during the card farming process";

  internal const string NoBoostingAppsInArchiPlayedWhileIdle = "There are no apps available to boost achievements while playing in idle mode";
}

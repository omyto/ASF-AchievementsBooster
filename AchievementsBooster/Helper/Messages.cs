namespace AchievementsBooster.Helper;

internal static class Messages {
  internal static string AutoStartBotsEmpty { get; } = "There are no bots configured for auto-start. To start the achievement boosting process, please use the command";
  internal static string ConfigPropertyInvalid { get; } = "The configured value {1} of property {0} is invalid! It will be automatically adjusted to {2}";
  internal static string BoosterInitEror { get; } = "Can not initialize booster for bot: {0}";
  internal static string BoosterNotFound { get; } = "Unable to locate any booster for bot: {0}!";
  internal static string NoGamesBoosting { get; } = "The bot doesn't have any games for boosting";
  internal static string BoostingImpossible { get; } = "Not ready to boost; playing is blocked.";
  internal static string RestTime { get; } = "Resting time";
  internal static string BoostableQueue { get; } = "Boostable queue: {0}";
  internal static string RestingApps { get; } = "Resting apps: {0}";
  internal static string GamesRemoved { get; } = "Games was removed: {0}";
  internal static string BoostingApp { get; } = "Boosting {0}: {1} achievements remaining";
  internal static string BoostingFailed { get; } = "Boosting apps failed; reason: {0}";
  internal static string BoostingStarted { get; } = "This booster has already started!";
  internal static string BoostingNotStart { get; } = "This booster is currently not running!";
  internal static string FinishedBoost { get; } = "Finished boost app {0}.";
  internal static string FinishedBoostable { get; } = "Finished boost app {0}, some achievements have been restricted.";
  internal static string RestingApp { get; } = "{0}: taking a rest after boosting for {1} minutes";
  internal static string NotOwnedGame { get; } = "{0} is not owned by the user";
  internal static string AppInASFBlacklist { get; } = "App {0} is on the ASF blacklist";
  internal static string InvalidAppDLC { get; } = "App {0} has invalid DLC: {1}";
  internal static string NonBoostableApp { get; } = "App {0} is not boostable";
  internal static string FoundBoostableApp { get; } = "Found  {0}: {1} achievements remaining";
  internal static string AchievementsNotAvailable { get; } = "App {0} doesn't have the achievements feature";
  internal static string IgnoreAppWithVAC { get; } = "App {0} has the VAC enabled; you configured it to be ignored";
  internal static string IgnoreAppWithDLC { get; } = "App {0} has one or more DLCs; you configured it to be ignored";
  internal static string IgnoreDeveloper { get; } = "App {0}, developed by '{1}', has been configured to be ignored";
  internal static string IgnorePublisher { get; } = "App {0}, published by '{1}', has been configured to be ignored";
  internal static string StatsNotFound { get; } = "Unable to locate user stats for app {0}";
  internal static string ProductInfoNotFound { get; } = "Can't get product info for app {0}";
  internal static string AchievementPercentagesNotFound { get; } = "Can't get global achievement percentages for app {0}";
  internal static string GameAchievementNotExist { get; } = "No global achievement percentages exist for app {0}";
  internal static string PerfectApp { get; } = "{0}: Had unlocked all achievements";
  internal static string NoUnlockableStats { get; } = "{0}: No unlockable achievements, some have been restricted.";
  internal static string UnlockAchievementSuccess { get; } = "{0}: unlocked the '{1}' achievement";
  internal static string UnlockAchievementFailed { get; } = "{0}: failed to achieve achievement '{1}'";
}

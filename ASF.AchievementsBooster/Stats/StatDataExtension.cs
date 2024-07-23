namespace AchievementsBooster.Stats;

internal static class StatDataExtension {
  internal static bool Unlockable(this StatData self) => !self.IsSet && !self.Restricted;
}

namespace AchievementsBooster.App;

internal readonly struct Achievement(string internalName, string name, string description, uint percentUnlocked) {
  internal string InternalName { get; } = internalName;

  internal string Name { get; } = name;

  internal string Description { get; } = description;

  internal uint PercentUnlocked { get; } = percentUnlocked;
}

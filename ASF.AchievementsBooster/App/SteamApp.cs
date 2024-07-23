
using System.Collections.Generic;
using System.Collections.Immutable;

namespace AchievementsBooster.App;

internal class SteamApp : App {
  internal uint ID { get; }

  internal string Name { get; }

  internal HashSet<EStoreCategory> Categories { get; }

  internal ImmutableList<Achievement>? Achievements { get; }

  internal SteamApp(uint id, string name, HashSet<EStoreCategory> categories) {
    ID = id;
    Name = name;
    Categories = categories;
  }

  internal SteamApp Clone() => new(ID, Name, Categories);

  internal bool HasVAC() => Categories.Contains(EStoreCategory.VAC);

  internal bool HasAchievements() => Categories.Contains(EStoreCategory.Achievements);

  //internal bool Ready() => IsValid && HasAchievements() && !HasVAC();
}

internal class App {
  internal bool IsValid { get; protected private set; } = true;
}

internal sealed class InvalidApp : SteamApp {
  internal InvalidApp(uint id, string name = "") : base(id, name, []) => IsValid = false;
}

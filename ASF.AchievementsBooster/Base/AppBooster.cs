
using System.Globalization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;

namespace AchievementsBooster.Base;

internal sealed class AppBooster {
  internal uint ID { get; }

  internal string Name => Info.Name;

  internal ProductInfo Info { get; }

  internal AppBooster(uint id, ProductInfo info) {
    ID = id;
    Info = info;
  }

  internal bool IsVACEnabled => Info.VACEnabled;

  internal bool HasAchievements() => Info.AchievementsEnabled;

  internal bool IsPlayable() {
    switch (Info.ReleaseState.ToUpperInvariant()) {
      case "RELEASED":
        break;
      case "PRELOADONLY" or "PRERELEASE":
        return false;
      default:
        ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(Info.ReleaseState), Info.ReleaseState), Caller.Name());
        break;
    }

    return Info.Type.ToUpperInvariant() is "GAME" or "MOVIE" or "VIDEO";
  }
}

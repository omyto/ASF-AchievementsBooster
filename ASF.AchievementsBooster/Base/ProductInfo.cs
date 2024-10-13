using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ArchiSteamFarm;
using ArchiSteamFarm.Localization;
using SteamKit2;
using PICSProductInfo = SteamKit2.SteamApps.PICSProductInfoCallback.PICSProductInfo;

namespace AchievementsBooster.Base;

public sealed class ProductInfo {
  public uint ID { get; private init; }

  public string Name { get; private init; }

  public string Type { get; private init; }

  public string? ReleaseState { get; private init; }

  public string? SteamReleaseDate { get; private init; }

  public string? StoreReleaseDate { get; private init; }

  public bool? IsAchievementsEnabled { get; internal set; }

  public bool IsVACEnabled => HasVACCategory;

  public bool HasVACCategory { get; private init; }

  public bool HasAchievementsCategory { get; private init; }

  public ImmutableHashSet<uint> DLCs { get; private init; } = [];

  public ImmutableHashSet<string> Developers { get; private init; } = [];

  public ImmutableHashSet<string> Publishers { get; private init; } = [];

  internal ProductInfo(PICSProductInfo productInfoApp) {
    KeyValue productInfo = productInfoApp.KeyValues;
    KeyValue commonProductInfo = productInfo["common"];

    ID = productInfoApp.ID;
    Name = commonProductInfo["name"].AsString() ?? string.Empty;
    Type = commonProductInfo["type"].AsString() ?? string.Empty;

    KeyValue releaseState = commonProductInfo["ReleaseState"];
    if (releaseState != KeyValue.Invalid) {
      ReleaseState = releaseState.AsString() ?? string.Empty;
    }

    KeyValue steamReleaseDate = commonProductInfo["steam_release_date"];
    if (steamReleaseDate != KeyValue.Invalid) {
      SteamReleaseDate = steamReleaseDate.AsString() ?? string.Empty;
    }

    KeyValue storeReleaseDate = commonProductInfo["store_release_date"];
    if (storeReleaseDate != KeyValue.Invalid) {
      StoreReleaseDate = storeReleaseDate.AsString() ?? string.Empty;
    }

    List<KeyValue> categories = commonProductInfo["category"].Children;
    foreach (KeyValue category in categories) {
      if (!HasVACCategory && Constants.VACCategory.Equals(category.Name, StringComparison.OrdinalIgnoreCase)) {
        HasVACCategory = true;
        continue;
      }
      if (!HasAchievementsCategory && Constants.AchievementsCategory.Equals(category.Name, StringComparison.OrdinalIgnoreCase)) {
        HasAchievementsCategory = true;
        continue;
      }
    }

    if (HasAchievementsCategory) {
      IsAchievementsEnabled = true;
    }

    KeyValue extendedProductInfo = productInfo["extended"];
    if (extendedProductInfo != KeyValue.Invalid) {
      string? developer = extendedProductInfo["developer"].AsString();
      if (developer != null) {
        Developers = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, [developer]);
      }
      string? publisher = extendedProductInfo["publisher"].AsString();
      if (publisher != null) {
        Publishers = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, [publisher]);
      }

      string? listOfDlc = extendedProductInfo["listofdlc"].AsString();
      if (listOfDlc != null) {
        DLCs = listOfDlc.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries)
        .Select(e => {
          if (!uint.TryParse(e, out uint id) || id == 0) {
            AchievementsBooster.GlobalLogger.Warning($"Invalid DLC ID {e} for app {productInfoApp.ID}");
            return (uint) 0;
          }
          return id;
        })
        .Where(e => e > 0)
        .ToImmutableHashSet();
      }
    }
  }

  internal bool IsPlayable() {
    switch (ReleaseState?.ToUpperInvariant()) {
      case null or "":
        break;
      case "RELEASED":
        break;
      case "PRELOADONLY" or "PRERELEASE":
        return false;
      default:
        AchievementsBooster.GlobalLogger.Error(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(ReleaseState), ReleaseState));
        break;
    }

    return Type.ToUpperInvariant() is "GAME" or "MOVIE" or "VIDEO";
  }
}

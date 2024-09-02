using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ArchiSteamFarm;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using SteamKit2;
using PICSProductInfo = SteamKit2.SteamApps.PICSProductInfoCallback.PICSProductInfo;

namespace AchievementsBooster.Base;

public sealed class ProductInfo {
  public uint ID { get; private init; }

  public string Name { get; private init; }

  public string Type { get; private init; }

  public string ReleaseState { get; private init; }

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
    ReleaseState = commonProductInfo["ReleaseState"].AsString() ?? string.Empty;

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
        Developers = [developer];
      }
      string? publisher = extendedProductInfo["publisher"].AsString();
      if (publisher != null) {
        Publishers = [publisher];
      }

      string? listOfDlc = extendedProductInfo["listofdlc"].AsString();
      if (listOfDlc != null) {
        DLCs = listOfDlc.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries)
        .Select(e => {
          if (!uint.TryParse(e, out uint id) || id == 0) {
            ASF.ArchiLogger.LogGenericWarning($"Invalid DLC ID {e} for app {productInfoApp.ID}", Caller.Name());
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
    switch (ReleaseState.ToUpperInvariant()) {
      case "":
        break;
      case "RELEASED":
        break;
      case "PRELOADONLY" or "PRERELEASE":
        return false;
      default:
        ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(ReleaseState), ReleaseState), Caller.Name());
        break;
    }

    return Type.ToUpperInvariant() is "GAME" or "MOVIE" or "VIDEO";
  }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using AchievementsBooster.Helper;
using ArchiSteamFarm;
using ArchiSteamFarm.Localization;
using SteamKit2;

namespace AchievementsBooster.Model;

public sealed class ProductInfo {
  private static string VACCategory { get; } = "category_8";

  // private static string AchievementsCategory { get; } = "category_22";

  public uint ID { get; }

  public string Name { get; }

  public string FullName { get; }

  public string Type { get; init; }

  public string? ReleaseState { get; init; }

  public string? SteamReleaseDate { get; init; }

  public string? StoreReleaseDate { get; init; }

  public bool IsVACEnabled { get; init; }

  public ImmutableHashSet<uint> DLCs { get; init; }

  public ImmutableHashSet<string> Developers { get; init; }

  public ImmutableHashSet<string> Publishers { get; init; }

  public bool IsBoostable => IsBoostableValue == true;

  private bool? IsBoostableValue {
    get {
      field ??= IsBoostableProduct();
      return field;
    }
  }

  internal ProductInfo(SteamApps.PICSProductInfoCallback.PICSProductInfo productInfoApp) {
    KeyValue productInfo = productInfoApp.KeyValues;
    KeyValue commonProductInfo = productInfo["common"];

    string name = commonProductInfo["name"].AsString() ?? string.Empty;
    string type = commonProductInfo["type"].AsString() ?? string.Empty;

    KeyValue releaseState = commonProductInfo["ReleaseState"];
    KeyValue steamReleaseDate = commonProductInfo["steam_release_date"];
    KeyValue storeReleaseDate = commonProductInfo["store_release_date"];

    List<KeyValue> categories = commonProductInfo["category"].Children;
    bool hasVACCategory = false;
    //bool hasAchievementsCategory = false;
    foreach (KeyValue category in categories) {
      if (!hasVACCategory && VACCategory.Equals(category.Name, StringComparison.OrdinalIgnoreCase)) {
        hasVACCategory = true;
        continue;
      }
      //if (!hasAchievementsCategory && Constants.AchievementsCategory.Equals(category.Name, StringComparison.OrdinalIgnoreCase)) {
      //  hasAchievementsCategory = true;
      //  continue;
      //}
    }

    KeyValue extendedProductInfo = productInfo["extended"];
    string? developer = null;
    string? publisher = null;
    ImmutableHashSet<uint>? dlcs = null;
    if (extendedProductInfo != KeyValue.Invalid) {
      developer = extendedProductInfo["developer"].AsString();
      publisher = extendedProductInfo["publisher"].AsString();

      string? listOfDlc = extendedProductInfo["listofdlc"].AsString();
      if (listOfDlc != null) {
        dlcs = listOfDlc.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries)
        .Select(dlc => {
          if (!uint.TryParse(dlc, out uint id) || id == 0) {
            Logger.Shared.Warning(string.Format(CultureInfo.CurrentCulture, Messages.InvalidAppDLC, $"{productInfoApp.ID} ({name})", dlc));
            return (uint) 0;
          }
          return id;
        })
        .Where(e => e > 0)
        .ToImmutableHashSet();
      }
    }

    // Initialize fields
    ID = productInfoApp.ID;
    Name = name;
    Type = type;
    FullName = $"{ID} ({Name})";
    ReleaseState = releaseState != KeyValue.Invalid ? releaseState.AsString() ?? string.Empty : null;
    SteamReleaseDate = steamReleaseDate != KeyValue.Invalid ? steamReleaseDate.AsString() ?? string.Empty : null;
    StoreReleaseDate = storeReleaseDate != KeyValue.Invalid ? storeReleaseDate.AsString() ?? string.Empty : null;
    IsVACEnabled = hasVACCategory;
    Developers = developer != null ? ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, [developer]) : [];
    Publishers = publisher != null ? ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, [publisher]) : [];
    DLCs = dlcs ?? [];
  }

  private bool IsBoostableProduct() {
    switch (ReleaseState?.ToUpperInvariant()) {
      case null or "":
        break;
      case "RELEASED":
        break;
      case "PRELOADONLY" or "PRERELEASE":
        return false;
      default:
        Logger.Shared.Error(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(ReleaseState), ReleaseState));
        break;
    }

    return Type.ToUpperInvariant() is "GAME" or "MOVIE" or "VIDEO";
  }
}

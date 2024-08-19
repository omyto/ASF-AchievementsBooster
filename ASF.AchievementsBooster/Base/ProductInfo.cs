using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ArchiSteamFarm;
using ArchiSteamFarm.Core;
using SteamKit2;
using PICSProductInfo = SteamKit2.SteamApps.PICSProductInfoCallback.PICSProductInfo;

namespace AchievementsBooster.Base;

public sealed class ProductInfo {
  public uint ID { get; private init; }

  public string Name { get; private init; }

  public string Type { get; private init; }

  public string ReleaseState { get; private init; }

  public bool AchievementsEnabled { get; set; }

  public bool VACEnabled { get; private init; }

  public ImmutableHashSet<uint> DLCs { get; private init; } = [];

  public ImmutableHashSet<string> Developers { get; private init; } = [];

  public ImmutableHashSet<string> Publishers { get; private init; } = [];

  public FrozenDictionary<string, double>? AchievementPercentages { get; internal set; }

  internal ProductInfo(PICSProductInfo productInfoApp) {
    KeyValue productInfo = productInfoApp.KeyValues;
    KeyValue commonProductInfo = productInfo["common"];

    ID = productInfoApp.ID;
    Name = commonProductInfo["name"].AsString() ?? string.Empty;
    Type = commonProductInfo["type"].AsString() ?? string.Empty;
    ReleaseState = commonProductInfo["ReleaseState"].AsString() ?? string.Empty;

    List<KeyValue> categories = commonProductInfo["category"].Children;
    foreach (KeyValue category in categories) {
      if (!VACEnabled && Constants.VACCategory.Equals(category.Name, StringComparison.OrdinalIgnoreCase)) {
        VACEnabled = true;
        continue;
      }
      if (!AchievementsEnabled && Constants.AchievementsCategory.Equals(category.Name, StringComparison.OrdinalIgnoreCase)) {
        AchievementsEnabled = true;
        continue;
      }
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
}

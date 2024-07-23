using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam.Integration;

namespace AchievementsBooster.App;
internal static class AppUtils {

  private static readonly ConcurrentDictionary<uint, SteamApp> Apps = new();

  internal static async Task<SteamApp> GetApp(uint appID) {
    if (!Apps.TryGetValue(appID, out SteamApp? app)) {
      ASF.ArchiLogger.LogGenericDebug($"App {appID} not found! Try get from steam store...");
      AppDetails? appDetails = await GetAppDetails(appID).ConfigureAwait(false);
      app = appDetails != null ? GenerateApp(appID, appDetails) : new InvalidApp(appID);
      _ = Apps.TryAdd(appID, app);
    }

    return app;
  }

  private static SteamApp GenerateApp(uint appID, AppDetails appDetails) {
    string type = appDetails.Type?.ToUpperInvariant() ?? "";

    if (type is "GAME" or "VIDEO") {
      ImmutableList<AppCategory> categories = appDetails.Categories ?? [];
      //bool isValveAntiCheatEnabled = categories.Find(e => e.Id == (uint) EAppCategories.ValveAntiCheatEnabled) != null;
      return new SteamApp(
        appID,
        appDetails.Name ?? "",
        appDetails.Categories?.Select(e => (EStoreCategory) e.Id).ToHashSet() ?? []
      );
    }

    return new InvalidApp(appID);
  }

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  private static async Task<AppDetails?> GetAppDetails(uint appID) {
    if (ASF.WebBrowser == null) {
      throw new InvalidOperationException(nameof(ASF.WebBrowser));
    }

    string appids = $"{appID}";
    Uri request = new(ArchiWebHandler.SteamStoreURL, $"/api/appdetails?appids={appids}");
    Dictionary<string, AppDetailsResponse>? response = (await ASF.WebBrowser.UrlGetToJsonObject<Dictionary<string, AppDetailsResponse>>(request).ConfigureAwait(false))?.Content;

    if (response == null || !response.TryGetValue(appids, out AppDetailsResponse? detailsResponse)) {
      return null;
    }

    return detailsResponse.Success == true ? detailsResponse.Data : null;
  }
}

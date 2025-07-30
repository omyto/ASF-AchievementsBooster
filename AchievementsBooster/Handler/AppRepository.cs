using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Handler.Callback;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Web.Responses;

namespace AchievementsBooster.Handler;

internal sealed class AppRepository(Booster booster) {
  private static Uri AchievementsFilterAPI { get; } = new("https://ab.omyto.com/api/filters");

  private Booster Booster { get; } = booster;

  internal HashSet<uint> OwnedGames { get; private set; } = [];

  internal bool IsOwnedGamesUpdated { get; private set; }

  internal List<uint> FilteredGames { get; private set; } = [];

  private DateTime LastUpdateOwnedGamesTime { get; set; }

  private HashSet<uint> UnboostableApps { get; } = [];

  private Dictionary<uint, AppBoostInfo> RestingBoostApps { get; } = [];

  internal async Task Update(CancellationToken cancellationToken) {
    await UpdateOwnedGames(cancellationToken).ConfigureAwait(false);

    if (IsOwnedGamesUpdated) {
      await UpdateFilteredGames(cancellationToken).ConfigureAwait(false);
    }
  }

  private async Task UpdateOwnedGames(CancellationToken cancellationToken) {
    IsOwnedGamesUpdated = false;
    DateTime now = DateTime.Now;

    if (OwnedGames.Count > 0 && (now - LastUpdateOwnedGamesTime).TotalHours < 12.0) {
      return;
    }

    Dictionary<uint, string>? ownedGames = await Booster.Bot.ArchiHandler.GetOwnedGames(Booster.Bot.SteamID).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();

    if (ownedGames == null || ownedGames.Count == 0) {
      return;
    }

    HashSet<uint> newOwnedGames = ownedGames.Keys.ToHashSet();
    List<uint> gamesRemoved = OwnedGames.Except(newOwnedGames).ToList();

    OwnedGames = newOwnedGames;
    IsOwnedGamesUpdated = true;
    LastUpdateOwnedGamesTime = now;

    Booster.Logger.Info($"Owned games updated: {OwnedGames.Count} game(s).");
    //Booster.Logger.Trace($"Games owned: {string.Join(",", OwnedGames)}");

    if (gamesRemoved.Count > 0) {
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.GamesRemoved, string.Join(",", gamesRemoved)));

      // Remove from resting list
      foreach (uint appID in gamesRemoved) {
        _ = RestingBoostApps.Remove(appID);
      }

      if (RestingBoostApps.Count > 0) {
        Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApps, string.Join(",", RestingBoostApps.Keys)));
      }
    }

    return;
  }

  private async Task UpdateFilteredGames(CancellationToken cancellationToken) {
    List<uint>? filteredGames = await FilterAchievementsApps(OwnedGames, cancellationToken).ConfigureAwait(false);

    if (filteredGames != null) {
      FilteredGames = filteredGames;
    }
    else if (FilteredGames.Count == 0) {
      FilteredGames = PossibleApps.FilterAchievementsApps(OwnedGames);
    }

    // Filter out blacklisted apps and resting apps
    if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Count > 0) {
      FilteredGames = FilteredGames.Except(ASF.GlobalConfig.Blacklist).ToList();
    }

    if (BoosterConfig.Global.Blacklist.Count > 0) {
      FilteredGames = FilteredGames.Except(BoosterConfig.Global.Blacklist).ToList();
    }

    if (RestingBoostApps.Count > 0) {
      FilteredGames = FilteredGames.Except(RestingBoostApps.Keys).ToList();
    }
  }

  internal void MarkAppAsResting(AppBoostInfo app, DateTime? restingEndTime = null) {
    app.BoostingDuration = 0;
    app.RestingEndTime = restingEndTime ?? DateTime.Now.AddMinutes(BoosterConfig.Global.BoostRestTimePerApp);

    if (!RestingBoostApps.TryAdd(app.ID, app)) {
      Booster.Logger.Warning($"App {app.FullName} already resting");
    }
  }

  internal bool IsBoostableApp(uint appID, bool isFiltered = false) {
    if (!isFiltered) {
      if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Contains(appID)) {
        Booster.Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.AppInASFBlacklist, appID));
        return false;
      }

      if (BoosterConfig.Global.Blacklist.Contains(appID)) {
        Booster.Logger.Trace($"App {appID} is on your AchievementsBooster blacklist list");
        return false;
      }
    }

    return BoosterConfig.Global.UnrestrictedApps.Contains(appID)
      || (!UnboostableApps.Contains(appID)
        && !Booster.Cache.PerfectGames.Contains(appID)
        && !AchievementsBoosterPlugin.GlobalCache.NonAchievementApps.Contains(appID)
        && (!BoosterConfig.Global.RestrictAppWithVAC || !AchievementsBoosterPlugin.GlobalCache.VACApps.Contains(appID)));
  }

  internal List<AppBoostInfo> GetRestedAppsReadyForBoost(int max) {
    List<AppBoostInfo> results = [];
    DateTime now = DateTime.Now;

    foreach (AppBoostInfo app in RestingBoostApps.Values.ToList()) {
      if (now > app.RestingEndTime) {
        results.Add(app);
        _ = RestingBoostApps.Remove(app.ID);
        Booster.Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, app.FullName, app.UnlockableAchievementsCount));

        if (results.Count >= max) {
          break;
        }
      }
    }

    return results;
  }

  internal async Task<ProductInfo?> GetProductInfo(uint appID, CancellationToken cancellationToken) {
    ProductInfo? product = await Booster.SteamClientHandler.GetProduct(appID, cancellationToken).ConfigureAwait(false);
    if (product == null) {
      Booster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ProductInfoNotFound, appID));
    }
    return product;
  }

  internal async Task<AppBoostInfo?> GetBoostableApp(uint appID, CancellationToken cancellationToken, bool reCheckBoostable = true, bool allowRestingApp = true) {
    if (reCheckBoostable && !IsBoostableApp(appID)) {
      return null;
    }

    if (allowRestingApp) {
      if (RestingBoostApps.TryGetValue(appID, out AppBoostInfo? restingApp)) {
        _ = RestingBoostApps.Remove(appID);
        return restingApp;
      }
    }

    // Check achievement progress
    AchievementProgress? progress = await Booster.SteamClientHandler.GetAchievementProgress(appID, cancellationToken).ConfigureAwait(false);
    if (progress == null) {
      Booster.Logger.Warning($"Achievement progress for app {appID} not found");
      return null;
    }

    if (progress.Total == 0) {
      _ = UnboostableApps.Add(appID);
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.AchievementsNotAvailable, appID));
      return null;
    }

    if (progress.AllUnlocked && progress.Unlocked == progress.Total) {
      _ = Booster.Cache.PerfectGames.Add(appID);
      Booster.Logger.Info($"All achievements already unlocked for app {appID}");
      return null;
    }

    // Check product info
    ProductInfo? product = await GetProductInfo(appID, cancellationToken).ConfigureAwait(false);
    if (product == null) {
      Booster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ProductInfoNotFound, appID));
      return null;
    }

    if (!product.IsBoostable) {
      Booster.Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.NonBoostableApp, product.FullName));
      return null;
    }

    if (IsRestrictedApp(product)) {
      return null;
    }

    // Check user stats
    UserStatsResponse? statsResponse = await Booster.SteamClientHandler.GetStats(appID, cancellationToken).ConfigureAwait(false);
    if (statsResponse == null) {
      Booster.Logger.Warning($"No user stats found for app {appID}");
      return null;
    }

    List<StatData> stats = statsResponse.StatDatas;
    if (stats.Count == 0) {
      _ = UnboostableApps.Add(appID);
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.AchievementsNotAvailable, product.FullName));
      return null;
    }

    List<StatData> remainingAchievements = stats.Where(e => !e.IsSet).ToList();
    if (remainingAchievements.Count == 0) {
      _ = Booster.Cache.PerfectGames.Add(appID);
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.PerfectApp, product.FullName));
      return null;
    }

    int unlockableAchievementsCount = remainingAchievements.Count(e => !e.Restricted);
    if (unlockableAchievementsCount == 0) {
      _ = UnboostableApps.Add(appID);
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, product.FullName));
      return null;
    }

    AchievementRates? achievementRates = await Booster.SteamClientHandler.GetAchievementCompletionRates(appID, cancellationToken).ConfigureAwait(false);
    if (achievementRates == null) {
      Booster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.AchievementPercentagesNotFound, product.FullName));
      return null;
    }

    Booster.Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, product.FullName, unlockableAchievementsCount));
    return new AppBoostInfo(appID, product, achievementRates, remainingAchievements.Count, unlockableAchievementsCount);
  }

  private bool IsRestrictedApp(ProductInfo product) {
    uint appID = product.ID;
    if (BoosterConfig.Global.UnrestrictedApps.Contains(appID)) {
      return false;
    }

    if (product.IsVACEnabled) {
      _ = AchievementsBoosterPlugin.GlobalCache.VACApps.Add(appID);
      if (BoosterConfig.Global.RestrictAppWithVAC) {
        Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreAppWithVAC, product.FullName));
        return true;
      }
    }

    if (BoosterConfig.Global.RestrictAppWithDLC && product.DLCs.Count > 0) {
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreAppWithDLC, product.FullName));
      _ = UnboostableApps.Add(appID);
      return true;
    }

    if (BoosterConfig.Global.RestrictDevelopers.Count > 0) {
      foreach (string developer in BoosterConfig.Global.RestrictDevelopers) {
        if (product.Developers.Contains(developer)) {
          Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreDeveloper, product.FullName, developer));
          _ = UnboostableApps.Add(appID);
          return true;
        }
      }
    }

    if (BoosterConfig.Global.RestrictPublishers.Count > 0) {
      foreach (string publisher in BoosterConfig.Global.RestrictPublishers) {
        if (product.Publishers.Contains(publisher)) {
          Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnorePublisher, product.FullName, publisher));
          _ = UnboostableApps.Add(appID);
          return true;
        }
      }
    }

    return false;
  }

  internal async Task<List<uint>?> FilterAchievementsApps(HashSet<uint> ownedGames, CancellationToken cancellationToken) {
    if (ASF.WebBrowser == null) {
      throw new InvalidOperationException(nameof(ASF.WebBrowser));
    }

    List<uint>? result = null;
    await BoosterShared.AchievementsFilterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    try {
      Dictionary<string, string> headers = new() {
        { "ab-booster", Booster.Identifier },
        { "ab-version", BoosterShared.PluginVersionS },
        { "asf-version",  BoosterShared.ASFVersion }
      };

      Dictionary<string, object> data = new() {
        { "appIds", ownedGames.ToArray() },
        { "restriction",  new Dictionary<string, object>() {
          { "vac", BoosterConfig.Global.RestrictAppWithVAC },
          { "dlc", BoosterConfig.Global.RestrictAppWithDLC },
          { "developers", BoosterConfig.Global.RestrictDevelopers.ToArray() },
          { "publishers", BoosterConfig.Global.RestrictPublishers.ToArray() },
          { "excludedAppIds", BoosterConfig.Global.UnrestrictedApps.ToArray() }
        }}
      };

      ObjectResponse<AchievementsFilterResponse>? response = await ASF.WebBrowser.UrlPostToJsonObject<AchievementsFilterResponse, IDictionary<string, object>>(
        AchievementsFilterAPI, headers, data, maxTries: 3, rateLimitingDelay: 1000, cancellationToken: cancellationToken).ConfigureAwait(false);
      result = response?.Content?.AppIDs;

      if (response == null) {
        Booster.Logger.Warning($"Can't get achievements filter response");
      }
      else if (response.StatusCode != HttpStatusCode.OK) {
        Booster.Logger.Warning($"Achievements filter response status {response.StatusCode}");
      }
      else if (response.Content == null) {
        Booster.Logger.Warning($"Achievements filter response content is null");
      }
      else if (response.Content.Success != true) {
        Booster.Logger.Warning($"Achievements filter response unsuccess");
      }
    }
    finally {
      _ = BoosterShared.AchievementsFilterSemaphore.Release();
    }

    return result;
  }

}

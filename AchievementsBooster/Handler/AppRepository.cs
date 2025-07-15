using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Handler.Callback;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Handler;

internal sealed class AppRepository(Booster booster) {
  private enum EGetAppStatus : byte {
    OK,
    ProductNotFound,
    AchievementPercentagesNotFound,
    NonBoostable
  }

  private Booster Booster { get; } = booster;

  internal HashSet<uint> OwnedGames { get; private set; } = [];

  internal bool IsOwnedGamesUpdated { get; private set; }

  internal List<uint> FilteredGames { get; private set; } = [];

  private DateTime LastUpdateOwnedGamesTime { get; set; }

  private HashSet<uint> NonBoostableApps { get; } = [];

  private List<AppBoostInfo> RestingBoostApps { get; } = [];

  internal async Task Update(CancellationToken cancellationToken) {
    // Update owned games
    await UpdateOwnedGames(cancellationToken).ConfigureAwait(false);

    if (IsOwnedGamesUpdated) {
      // Filter out non-achievement apps
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
    HashSet<uint> gamesRemoved = OwnedGames.Except(newOwnedGames).ToHashSet();

    OwnedGames = newOwnedGames;
    IsOwnedGamesUpdated = true;
    LastUpdateOwnedGamesTime = now;

    Booster.Logger.Info($"Owned games updated: {OwnedGames.Count} game(s).");
    //Booster.Logger.Trace($"Games owned: {string.Join(",", OwnedGames)}");

    if (gamesRemoved.Count > 0) {
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.GamesRemoved, string.Join(",", gamesRemoved)));
      List<uint> restingApps = [];

      // Remove from resting list
      for (int index = 0; index < RestingBoostApps.Count; index++) {
        AppBoostInfo app = RestingBoostApps[index];
        if (gamesRemoved.Contains(app.ID)) {
          RestingBoostApps.RemoveAt(index);
          index--;
        }
        else {
          restingApps.Add(app.ID);
        }
      }

      if (restingApps.Count > 0) {
        Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApps, string.Join(",", restingApps)));
      }
    }

    return;
  }

  private async Task UpdateFilteredGames(CancellationToken cancellationToken) {
    List<uint>? filteredGames = await AppUtils.FilterAchievementsApps(OwnedGames, Booster, cancellationToken).ConfigureAwait(false);

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
      FilteredGames = FilteredGames.Except(RestingBoostApps.Select(e => e.ID)).ToList();
    }
  }

  internal void MarkAppAsResting(AppBoostInfo app, DateTime? restingEndTime = null) {
    app.BoostingDuration = 0;
    app.RestingEndTime = restingEndTime ?? DateTime.Now.AddMinutes(BoosterConfig.Global.BoostRestTimePerApp);
    RestingBoostApps.Add(app);
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
      || (!NonBoostableApps.Contains(appID)
        && !Booster.Cache.PerfectGames.Contains(appID)
        && !AchievementsBoosterPlugin.GlobalCache.NonAchievementApps.Contains(appID)
        && (!BoosterConfig.Global.RestrictAppWithVAC || !AchievementsBoosterPlugin.GlobalCache.VACApps.Contains(appID)));
  }

  internal List<AppBoostInfo> GetRestedAppsReadyForBoost(int max) {
    List<AppBoostInfo> results = [];
    DateTime now = DateTime.Now;

    for (int index = 0; index < RestingBoostApps.Count && results.Count < max; index++) {
      AppBoostInfo app = RestingBoostApps[index];

      if (now > app.RestingEndTime) {
        RestingBoostApps.RemoveAt(index--);
        results.Add(app);
        Booster.Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, app.FullName, app.UnlockableAchievementsCount));
      }
    }

    return results;
  }

  internal async Task<AppBoostInfo?> GetAppBoost(uint appID, CancellationToken cancellationToken) {
    if (!IsBoostableApp(appID)) {
      return null;
    }

    for (int i = 0; i < RestingBoostApps.Count; i++) {
      cancellationToken.ThrowIfCancellationRequested();
      AppBoostInfo restingApp = RestingBoostApps[i];
      if (restingApp.ID == appID) {
        RestingBoostApps.RemoveAt(i);
        return restingApp;
      }
    }

    (EGetAppStatus _, AppBoostInfo? app) = await GetApp(appID, cancellationToken).ConfigureAwait(false);
    return app;
  }

  internal async Task<ProductInfo?> GetProductInfo(uint appID, CancellationToken cancellationToken)
    => await AppUtils.GetProduct(appID, Booster, cancellationToken).ConfigureAwait(false);

  internal async Task<AppBoostInfo?> GetBoostableApp(uint appID, CancellationToken cancellationToken) {
    (EGetAppStatus status, AppBoostInfo? app) = await GetApp(appID, cancellationToken).ConfigureAwait(false);
    return status == EGetAppStatus.OK ? app : null;
  }

  private async Task<(EGetAppStatus status, AppBoostInfo?)> GetApp(uint appID, CancellationToken cancellationToken) {
    ProductInfo? productInfo = await AppUtils.GetProduct(appID, Booster, cancellationToken).ConfigureAwait(false);
    if (productInfo == null) {
      Booster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ProductInfoNotFound, appID));
      return (EGetAppStatus.ProductNotFound, null);
    }

    if (!productInfo.IsBoostable) {
      //TODO: Consider adding to the NonBoostableApps set
      Booster.Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.NonBoostableApp, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    if (!BoosterConfig.Global.UnrestrictedApps.Contains(appID)) {
      if (productInfo.IsVACEnabled) {
        _ = AchievementsBoosterPlugin.GlobalCache.VACApps.Add(appID);
        if (BoosterConfig.Global.RestrictAppWithVAC) {
          Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreAppWithVAC, productInfo.FullName));
          return (EGetAppStatus.NonBoostable, null);
        }
      }

      if (BoosterConfig.Global.RestrictAppWithDLC && productInfo.DLCs.Count > 0) {
        Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreAppWithDLC, productInfo.FullName));
        _ = NonBoostableApps.Add(appID);
        return (EGetAppStatus.NonBoostable, null);
      }

      if (BoosterConfig.Global.RestrictDevelopers.Count > 0) {
        foreach (string developer in BoosterConfig.Global.RestrictDevelopers) {
          if (productInfo.Developers.Contains(developer)) {
            Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreDeveloper, productInfo.FullName, developer));
            _ = NonBoostableApps.Add(appID);
            return (EGetAppStatus.NonBoostable, null);
          }
        }
      }

      if (BoosterConfig.Global.RestrictPublishers.Count > 0) {
        foreach (string publisher in BoosterConfig.Global.RestrictPublishers) {
          if (productInfo.Publishers.Contains(publisher)) {
            Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnorePublisher, productInfo.FullName, publisher));
            _ = NonBoostableApps.Add(appID);
            return (EGetAppStatus.NonBoostable, null);
          }
        }
      }
    }

    UserStatsResponse? response = await Booster.SteamClientHandler.GetStats(appID, cancellationToken).ConfigureAwait(false);
    List<StatData>? statDatas = response?.StatDatas;
    if (statDatas == null || statDatas.Count == 0) {
      _ = AchievementsBoosterPlugin.GlobalCache.NonAchievementApps.Add(appID);
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.AchievementsNotAvailable, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    List<StatData> remainingAchievements = statDatas.Where(e => !e.IsSet).ToList();
    if (remainingAchievements.Count == 0) {
      _ = Booster.Cache.PerfectGames.Add(appID);
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.PerfectApp, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    int unlockableAchievementsCount = remainingAchievements.Count(e => !e.Restricted);
    if (unlockableAchievementsCount == 0) {
      Booster.Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    AchievementRates? achievementRates = await AppUtils.GetAchievementCompletionRates(appID, Booster, cancellationToken).ConfigureAwait(false);
    if (achievementRates == null) {
      Booster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.AchievementPercentagesNotFound, productInfo.FullName));
      return (EGetAppStatus.AchievementPercentagesNotFound, null);
    }

    Booster.Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, productInfo.FullName, unlockableAchievementsCount));
    return (EGetAppStatus.OK, new AppBoostInfo(appID, productInfo, achievementRates, remainingAchievements.Count, unlockableAchievementsCount));
  }
}

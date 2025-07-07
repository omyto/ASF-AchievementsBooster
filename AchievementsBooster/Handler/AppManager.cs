using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler.Callback;
using AchievementsBooster.Helpers;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Handler;

internal sealed class AppManager {
  private enum EGetAppStatus : byte {
    OK,
    ProductNotFound,
    AchievementPercentagesNotFound,
    NonBoostable
  }

  private BotCache Cache { get; }
  private SteamClientHandler SteamClientHandler { get; }

  private Logger Logger { get; }

  internal HashSet<uint> OwnedGames { get; private set; } = [];

  private Queue<uint> BoostableAppQueue { get; set; } = new();

  private HashSet<uint> NonBoostableApps { get; } = [];

  private List<AppBoostInfo> RestingApps { get; } = [];

  internal AppManager(SteamClientHandler clientHandler, BotCache cache, Logger logger) {
    SteamClientHandler = clientHandler;
    Cache = cache;
    Logger = logger;
  }

  internal async Task UpdateQueue(HashSet<uint> newOwnedGames, CancellationToken cancellationToken) {
    HashSet<uint> gamesRemoved = OwnedGames.Except(newOwnedGames).ToHashSet();
    if (gamesRemoved.Count > 0) {
      Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.GamesRemoved, string.Join(",", gamesRemoved)));
      // Remove from resting list
      for (int index = 0; index < RestingApps.Count; index++) {
        AppBoostInfo app = RestingApps[index];
        if (gamesRemoved.Contains(app.ID)) {
          RestingApps.RemoveAt(index);
          index--;
        }
      }
    }

    OwnedGames = newOwnedGames;

    List<uint>? games = await AppUtils.FilterAchievementsApps(newOwnedGames, SteamClientHandler, Logger, cancellationToken).ConfigureAwait(false);
    if (games == null) {
      if (BoostableAppQueue.Count > 0) {
        return;
      }

      games = PossibleApps.FilterAchievementsApps(newOwnedGames);
    }

    BoostableAppQueue.Clear();
    HashSet<uint> restingSet = RestingApps.Select(app => app.ID).ToHashSet();

    foreach (uint appID in games) {
      if (!restingSet.Contains(appID) && IsBoostableApp(appID)) {
        BoostableAppQueue.Enqueue(appID);
      }
    }

    Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.GamesOwned, string.Join(",", newOwnedGames)));
    if (restingSet.Count > 0) {
      Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.RestingApps, string.Join(",", restingSet)));
    }
    Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.BoostableQueue, $"{string.Join(",", BoostableAppQueue.Take(50))}{(BoostableAppQueue.Count > 50 ? ", ..." : ".")}"));
  }

  internal void MarkAppAsResting(AppBoostInfo app, DateTime? restingEndTime = null) {
    app.BoostingDuration = 0;
    app.RestingEndTime = restingEndTime ?? DateTime.Now.AddMinutes(AchievementsBoosterPlugin.GlobalConfig.BoostRestTimePerApp);
    RestingApps.Add(app);
  }

  internal void MarkAppsAsResting(IList<AppBoostInfo> apps, DateTime restingEndTime) {
    foreach (AppBoostInfo app in apps) {
      MarkAppAsResting(app, restingEndTime);
    }
  }

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  internal bool IsBoostableApp(uint appID) {
    if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Contains(appID)) {
      Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.AppInASFBlacklist, appID));
      return false;
    }

    if (AchievementsBoosterPlugin.GlobalConfig.Blacklist.Contains(appID)) {
      Logger.Trace($"App {appID} is on your AchievementsBooster blacklist list");
      return false;
    }

    if (AchievementsBoosterPlugin.GlobalConfig.UnrestrictedApps.Contains(appID)) {
      return true;
    }

    if (AchievementsBoosterPlugin.GlobalCache.NonAchievementApps.Contains(appID)) {
      return false;
    }

    if (AchievementsBoosterPlugin.GlobalConfig.RestrictAppWithVAC && AchievementsBoosterPlugin.GlobalCache.VACApps.Contains(appID)) {
      return false;
    }

    if (Cache.PerfectGames.Contains(appID)) {
      return false;
    }

    return !NonBoostableApps.Contains(appID);
  }

  internal async Task<List<AppBoostInfo>> NextAppsForBoost(int size, CancellationToken cancellationToken) {
    List<AppBoostInfo> results = [];
    List<uint> pendingAppIDs = [];

    try {
      // Get from resting list first
      DateTime now = DateTime.Now;
      for (int index = 0; index < RestingApps.Count && results.Count < size; index++) {
        cancellationToken.ThrowIfCancellationRequested();
        AppBoostInfo app = RestingApps[index];
        if (now > app.RestingEndTime) {
          RestingApps.RemoveAt(index--);
          results.Add(app);
          Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, app.FullName, app.UnlockableAchievementsCount));
        }
      }

      // Get from boostable queue
      while (BoostableAppQueue.Count > 0 && results.Count < size) {
        cancellationToken.ThrowIfCancellationRequested();
        uint appID = BoostableAppQueue.Peek();
        (EGetAppStatus status, AppBoostInfo? app) = await GetApp(appID, cancellationToken).ConfigureAwait(false);
        switch (status) {
          case EGetAppStatus.OK:
            ArgumentNullException.ThrowIfNull(app);
            results.Add(app);
            break;
          case EGetAppStatus.ProductNotFound:
          case EGetAppStatus.AchievementPercentagesNotFound:
            pendingAppIDs.Add(appID);
            break;
          case EGetAppStatus.NonBoostable:
          default:
            break;
        }
        _ = BoostableAppQueue.Dequeue();
      }
    }
    catch (Exception) {
      if (results.Count > 0) {
        DateTime now = DateTime.Now;
        results.ForEach(app => MarkAppAsResting(app, now));
      }
      throw;
    }
    finally {
      if (pendingAppIDs.Count > 0) {
        pendingAppIDs.ForEach(BoostableAppQueue.Enqueue);
      }
    }

    return results;
  }

  internal async Task<AppBoostInfo?> GetAppBoost(uint appID, CancellationToken cancellationToken) {
    if (!IsBoostableApp(appID)) {
      return null;
    }

    for (int i = 0; i < RestingApps.Count; i++) {
      cancellationToken.ThrowIfCancellationRequested();
      AppBoostInfo restingApp = RestingApps[i];
      if (restingApp.ID == appID) {
        RestingApps.RemoveAt(i);
        return restingApp;
      }
    }

    (EGetAppStatus _, AppBoostInfo? app) = await GetApp(appID, cancellationToken).ConfigureAwait(false);
    return app;
  }

  internal async Task<ProductInfo?> GetProductInfo(uint appID, CancellationToken cancellationToken)
    => await AppUtils.GetProduct(appID, SteamClientHandler, Logger, cancellationToken).ConfigureAwait(false);

  private async Task<(EGetAppStatus status, AppBoostInfo?)> GetApp(uint appID, CancellationToken cancellationToken) {
    ProductInfo? productInfo = await AppUtils.GetProduct(appID, SteamClientHandler, Logger, cancellationToken).ConfigureAwait(false);
    if (productInfo == null) {
      Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ProductInfoNotFound, appID));
      return (EGetAppStatus.ProductNotFound, null);
    }

    if (!productInfo.IsBoostable) {
      //TODO: Consider adding to the NonBoostableApps set
      Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.NonBoostableApp, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    if (!AchievementsBoosterPlugin.GlobalConfig.UnrestrictedApps.Contains(appID)) {
      if (productInfo.IsVACEnabled) {
        _ = AchievementsBoosterPlugin.GlobalCache.VACApps.Add(appID);
        if (AchievementsBoosterPlugin.GlobalConfig.RestrictAppWithVAC) {
          Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreAppWithVAC, productInfo.FullName));
          return (EGetAppStatus.NonBoostable, null);
        }
      }

      if (AchievementsBoosterPlugin.GlobalConfig.RestrictAppWithDLC && productInfo.DLCs.Count > 0) {
        Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreAppWithDLC, productInfo.FullName));
        _ = NonBoostableApps.Add(appID);
        return (EGetAppStatus.NonBoostable, null);
      }

      if (AchievementsBoosterPlugin.GlobalConfig.RestrictDevelopers.Count > 0) {
        foreach (string developer in AchievementsBoosterPlugin.GlobalConfig.RestrictDevelopers) {
          if (productInfo.Developers.Contains(developer)) {
            Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreDeveloper, productInfo.FullName, developer));
            _ = NonBoostableApps.Add(appID);
            return (EGetAppStatus.NonBoostable, null);
          }
        }
      }

      if (AchievementsBoosterPlugin.GlobalConfig.RestrictPublishers.Count > 0) {
        foreach (string publisher in AchievementsBoosterPlugin.GlobalConfig.RestrictPublishers) {
          if (productInfo.Publishers.Contains(publisher)) {
            Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.IgnorePublisher, productInfo.FullName, publisher));
            _ = NonBoostableApps.Add(appID);
            return (EGetAppStatus.NonBoostable, null);
          }
        }
      }
    }

    UserStatsResponse? response = await SteamClientHandler.GetStats(appID, cancellationToken).ConfigureAwait(false);
    List<StatData>? statDatas = response?.StatDatas;
    if (statDatas == null || statDatas.Count == 0) {
      _ = AchievementsBoosterPlugin.GlobalCache.NonAchievementApps.Add(appID);
      Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.AchievementsNotAvailable, productInfo.FullName));
      productInfo.IsBoostable = false;
      return (EGetAppStatus.NonBoostable, null);
    }

    List<StatData> remainingAchievements = statDatas.Where(e => !e.IsSet).ToList();
    if (remainingAchievements.Count == 0) {
      _ = Cache.PerfectGames.Add(appID);
      Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.PerfectApp, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    int unlockableAchievementsCount = remainingAchievements.Count(e => !e.Restricted);
    if (unlockableAchievementsCount == 0) {
      Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    AchievementPercentages? percentages = await AppUtils.GetAchievementPercentages(appID, SteamClientHandler, Logger, cancellationToken).ConfigureAwait(false);
    if (percentages == null) {
      Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.AchievementPercentagesNotFound, productInfo.FullName));
      return (EGetAppStatus.AchievementPercentagesNotFound, null);
    }

    Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, productInfo.FullName, unlockableAchievementsCount));
    return (EGetAppStatus.OK, new AppBoostInfo(appID, productInfo, percentages, remainingAchievements.Count, unlockableAchievementsCount));
  }
}

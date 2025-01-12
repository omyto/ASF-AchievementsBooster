using System;
using System.Collections.Concurrent;
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
using EBoostingMode = AchievementsBooster.Storage.BoosterGlobalConfig.EBoostingMode;

namespace AchievementsBooster.Handler;

internal sealed class AppManager {
  private enum EGetAppStatus : byte {
    OK,
    ProductNotFound,
    AchievementPercentagesNotFound,
    NonBoostable
  }

  private static class Holder {
    internal static ConcurrentDictionary<uint, SemaphoreSlim> ProductSemaphores { get; } = new();
    internal static ConcurrentDictionary<uint, ProductInfo> ProductDictionary { get; } = new();
    internal static ConcurrentDictionary<uint, SemaphoreSlim> AchievementPercentagesSemaphores { get; } = new();
    internal static ConcurrentDictionary<uint, AchievementPercentages> AchievementPercentagesDictionary { get; } = new();
  }

  private readonly BotCache Cache;
  private readonly Logger Logger;

  internal BoosterHandler BoosterHandler { get; }

  internal HashSet<uint> OwnedGames { get; private set; } = [];

  private Queue<uint> LastStandAppQueue { get; set; } = new();

  private Queue<uint> BoostableAppQueue { get; set; } = new();

  private HashSet<uint> NonBoostableApps { get; } = [];

  private List<AppBoostInfo> SleepingApps { get; } = [];

  internal AppManager(BoosterHandler boosterHandler, BotCache cache, Logger logger) {
    BoosterHandler = boosterHandler;
    Cache = cache;
    Logger = logger;
  }

  internal void UpdateOwnedGames(HashSet<uint> newOwnedGames) {
    if (OwnedGames.Count == 0) {
      Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.GamesOwned, string.Join(",", newOwnedGames)));

      foreach (uint appID in newOwnedGames) {
        if (IsBoostableApp(appID)) {
          BoostableAppQueue.Enqueue(appID);
        }
      }
    }
    else {
      List<uint> gamesAdded = newOwnedGames.Except(OwnedGames).ToList();
      if (gamesAdded.Count > 0) {
        Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.GamesAdded, string.Join(",", gamesAdded)));
        foreach (uint appID in gamesAdded) {
          if (IsBoostableApp(appID)) {
            BoostableAppQueue.Enqueue(appID);
          }
        }
      }

      HashSet<uint> gamesRemoved = OwnedGames.Except(newOwnedGames).ToHashSet();
      if (gamesRemoved.Count > 0) {
        Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.GamesRemoved, string.Join(",", gamesRemoved)));
        // Sleeping apps list
        for (int index = 0; index < SleepingApps.Count; index++) {
          AppBoostInfo app = SleepingApps[index];
          if (gamesRemoved.Contains(app.ID)) {
            SleepingApps.RemoveAt(index);
            index--;
          }
        }

        // Last stand apps queue
        Queue<uint> newLastStandAppQueue = new();
        while (LastStandAppQueue.Count > 0) {
          uint appID = LastStandAppQueue.Dequeue();
          if (!gamesRemoved.Contains(appID)) {
            newLastStandAppQueue.Enqueue(appID);
          }
        }
        LastStandAppQueue = newLastStandAppQueue;

        // Boostable apps queue
        Queue<uint> newBoostableAppQueue = new();
        while (BoostableAppQueue.Count > 0) {
          uint appID = BoostableAppQueue.Dequeue();
          if (!gamesRemoved.Remove(appID)) {
            newBoostableAppQueue.Enqueue(appID);
          }
        }
        BoostableAppQueue = newBoostableAppQueue;
      }
    }

    OwnedGames = newOwnedGames;
  }

  internal void Update() {
    if (BoostableAppQueue.Count == 0 && LastStandAppQueue.Count > 0) {
      BoostableAppQueue = LastStandAppQueue;
      LastStandAppQueue = new();
    }
  }

  internal void SetAppToSleep(AppBoostInfo app) => SleepingApps.Add(app);
  internal void PlaceAtLastStandQueue(AppBoostInfo app) => LastStandAppQueue.Enqueue(app.ID);

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  internal bool IsBoostableApp(uint appID) {
    if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Contains(appID)) {
      Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.AppInASFBlacklist, appID));
      return false;
    }

    if (AchievementsBooster.GlobalConfig.FocusApps.Contains(appID)) {
      return true;
    }

    if (AchievementsBooster.GlobalConfig.IgnoreApps.Contains(appID)) {
      Logger.Trace($"App {appID} is on your ignore list");
      return false;
    }

    if (AchievementsBooster.GlobalCache.NonAchievementApps.Contains(appID)) {
      return false;
    }

    if (AchievementsBooster.GlobalConfig.IgnoreAppWithVAC && AchievementsBooster.GlobalCache.VACApps.Contains(appID)) {
      return false;
    }

    if (Cache.PerfectGames.Contains(appID)) {
      return false;
    }

    return !NonBoostableApps.Contains(appID);
  }

  internal async Task<List<AppBoostInfo>> NextAppsForBoost(int size, uint lastSessionNo) {
    List<AppBoostInfo> results = [];
    List<uint> pendingAppIDs = [];

    // Get from sleeping list first
    DateTime now = DateTime.Now;
    for (int index = 0; index < SleepingApps.Count && results.Count < size; index++) {
      AppBoostInfo app = SleepingApps[index];

      bool match = false;
      switch (AchievementsBooster.GlobalConfig.BoostingMode) {
        case EBoostingMode.ContinuousBoosting:
          match = now.Date > app.LastPlayedTime.Date && (now - app.LastPlayedTime).TotalHours > AchievementsBooster.GlobalConfig.MaxContinuousBoostHours;
          break;
        case EBoostingMode.UniqueGamesPerSession:
          match = app.BoostSessionNo != lastSessionNo;
          break;
        case EBoostingMode.SingleDailyAchievementPerGame:
          match = now.Date > app.LastPlayedTime.Date;
          break;
        default:
          break;
      }

      if (match) {
        SleepingApps.RemoveAt(index--);
        app.ContinuousBoostingHours = 0;
        results.Add(app);
        Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, app.FullName, app.UnlockableAchievementsCount));
      }
    }

    // Get from boostable queue
    while (BoostableAppQueue.Count > 0 && results.Count < size) {
      uint appID = BoostableAppQueue.Dequeue();
      (EGetAppStatus status, AppBoostInfo? app) = await GetApp(appID).ConfigureAwait(false);
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
    }

    if (pendingAppIDs.Count > 0) {
      pendingAppIDs.ForEach(BoostableAppQueue.Enqueue);
    }

    return results;
  }

  internal async Task<AppBoostInfo?> GetAppBoost(uint appID) {
    if (!OwnedGames.Contains(appID)) {
      Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.NotOwnedGame, appID));
      return null;
    }

    if (!IsBoostableApp(appID)) {
      return null;
    }

    (EGetAppStatus _, AppBoostInfo? app) = await GetApp(appID).ConfigureAwait(false);
    return app;
  }

  private async Task<(EGetAppStatus status, AppBoostInfo?)> GetApp(uint appID) {
    ProductInfo? productInfo = await GetProduct(appID).ConfigureAwait(false);
    if (productInfo == null) {
      Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ProductInfoNotFound, appID));
      return (EGetAppStatus.ProductNotFound, null);
    }

    if (!productInfo.IsBoostable) {
      //TODO: Consider adding to the NonBoostableApps set
      Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.NonBoostableApp, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    if (productInfo.IsVACEnabled) {
      _ = AchievementsBooster.GlobalCache.VACApps.Add(appID);
      if (AchievementsBooster.GlobalConfig.IgnoreAppWithVAC) {
        Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreAppWithVAC, productInfo.FullName));
        return (EGetAppStatus.NonBoostable, null);
      }
    }

    if (AchievementsBooster.GlobalConfig.IgnoreAppWithDLC && productInfo.DLCs.Count > 0) {
      Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreAppWithDLC, productInfo.FullName));
      _ = NonBoostableApps.Add(appID);
      return (EGetAppStatus.NonBoostable, null);
    }

    if (AchievementsBooster.GlobalConfig.IgnoreDevelopers.Count > 0) {
      foreach (string developer in AchievementsBooster.GlobalConfig.IgnoreDevelopers) {
        if (productInfo.Developers.Contains(developer)) {
          Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.IgnoreDeveloper, productInfo.FullName, developer));
          _ = NonBoostableApps.Add(appID);
          return (EGetAppStatus.NonBoostable, null);
        }
      }
    }

    if (AchievementsBooster.GlobalConfig.IgnorePublishers.Count > 0) {
      foreach (string publisher in AchievementsBooster.GlobalConfig.IgnorePublishers) {
        if (productInfo.Publishers.Contains(publisher)) {
          Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.IgnorePublisher, productInfo.FullName, publisher));
          _ = NonBoostableApps.Add(appID);
          return (EGetAppStatus.NonBoostable, null);
        }
      }
    }

    UserStatsResponse? response = await BoosterHandler.GetStats(appID).ConfigureAwait(false);
    List<StatData>? statDatas = response?.StatDatas;
    if (statDatas == null || statDatas.Count == 0) {
      _ = AchievementsBooster.GlobalCache.NonAchievementApps.Add(appID);
      Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.AchievementsNotAvailable, productInfo.FullName));
      productInfo.IsBoostable = false;
      return (EGetAppStatus.NonBoostable, null);
    }

    List<StatData> remainingAchievements = statDatas.Where(e => !e.IsSet).ToList();
    if (remainingAchievements.Count == 0) {
      _ = Cache.PerfectGames.Add(appID);
      Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.PerfectApp, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    int unlockableAchievementsCount = remainingAchievements.Where(e => !e.Restricted).Count();
    if (unlockableAchievementsCount == 0) {
      Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, productInfo.FullName));
      return (EGetAppStatus.NonBoostable, null);
    }

    AchievementPercentages? percentages = await GetAchievementPercentages(appID).ConfigureAwait(false);
    if (percentages == null) {
      Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.AchievementPercentagesNotFound, productInfo.FullName));
      return (EGetAppStatus.AchievementPercentagesNotFound, null);
    }

    Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.FoundBoostableApp, productInfo.FullName, unlockableAchievementsCount));
    return (EGetAppStatus.OK, new AppBoostInfo(appID, productInfo, percentages, remainingAchievements.Count, unlockableAchievementsCount));
  }

  private async Task<ProductInfo?> GetProduct(uint appID) {
    SemaphoreSlim semaphore = Holder.ProductSemaphores.GetOrAdd(appID, _ => new SemaphoreSlim(1, 1));
    await semaphore.WaitAsync().ConfigureAwait(false);

    ProductInfo? product = null;
    try {
      if (!Holder.ProductDictionary.TryGetValue(appID, out product)) {
        product = await BoosterHandler.GetProductInfo(appID).ConfigureAwait(false);
        if (product != null) {
          _ = Holder.ProductDictionary.TryAdd(appID, product);
        }
      }
#if DEBUG
      else {
        Logger.Trace($"Get product infor for app {product.FullName} from cache");
      }
#endif
    }
    finally {
      _ = semaphore.Release();
      _ = Holder.ProductSemaphores.TryRemove(appID, out _);
    }

    return product;
  }

  private async Task<AchievementPercentages?> GetAchievementPercentages(uint appID) {
    SemaphoreSlim semaphore = Holder.AchievementPercentagesSemaphores.GetOrAdd(appID, _ => new SemaphoreSlim(1, 1));
    await semaphore.WaitAsync().ConfigureAwait(false);

    AchievementPercentages? percentages = null;
    try {
      if (!Holder.AchievementPercentagesDictionary.TryGetValue(appID, out percentages)) {
        percentages = await BoosterHandler.GetAchievementPercentages(appID).ConfigureAwait(false);
        if (percentages != null) {
          _ = Holder.AchievementPercentagesDictionary.TryAdd(appID, percentages);
        }
      }
#if DEBUG
      else {
        Logger.Trace($"Get achievement percentages for app {appID} from cache");
      }
#endif
    }
    finally {
      _ = semaphore.Release();
      _ = Holder.AchievementPercentagesSemaphores.TryRemove(appID, out _);
    }

    return percentages;
  }

}

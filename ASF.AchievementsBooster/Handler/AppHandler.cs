using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using AchievementsBooster.Callback;
using AchievementsBooster.Logger;
using AchievementsBooster.Stats;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Handler;

internal sealed class AppHandler {
  private static class Holder {
    internal static ConcurrentDictionary<uint, SemaphoreSlim> ProductSemaphores { get; } = new();
    internal static ConcurrentDictionary<uint, ProductInfo> ProductDictionary { get; } = new();
    internal static ConcurrentDictionary<uint, SemaphoreSlim> AchievementPercentagesSemaphores { get; } = new();
    internal static ConcurrentDictionary<uint, AchievementPercentages> AchievementPercentagesDictionary { get; } = new();
  }

  private readonly BotCache Cache;
  private readonly PLogger Logger;

  internal BoosterHandler BoosterHandler { get; }

  internal HashSet<uint> OwnedGames { get; private set; } = [];

  private List<uint> LastStandApps { get; } = [];

  private Queue<uint> BoostableAppQueue { get; } = new();

  private HashSet<uint> NonBoostableApps { get; } = [];

  private List<BoostableApp> SleepingApps { get; } = [];

  internal AppHandler(BotCache cache, BoosterHandler boosterHandler, PLogger logger) {
    Cache = cache;
    BoosterHandler = boosterHandler;
    Logger = logger;
  }

  internal void Update(Dictionary<uint, string>? ownedGamesDictionary) {
    if (ownedGamesDictionary != null && ownedGamesDictionary.Count > 0) {
      if (OwnedGames.Count == 0) {
        OwnedGames = [.. ownedGamesDictionary.Keys];
        Logger.Trace($"OwnedGames: {string.Join(",", OwnedGames)}");

        foreach (uint appID in OwnedGames) {
          if (IsBoostableApp(appID)) {
            BoostableAppQueue.Enqueue(appID);
          }
        }
      }
      else {
        //TODO: intersec
      }
    }

    if (BoostableAppQueue.Count == 0 && LastStandApps.Count > 0) {
      LastStandApps.ForEach(BoostableAppQueue.Enqueue);
      LastStandApps.Clear();
    }
  }

  internal void SetAppToSleep(BoostableApp app) => SleepingApps.Add(app);
  internal void PlaceAtLastStandQueue(BoostableApp app) => LastStandApps.Add(app.ID);

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  internal bool IsBoostableApp(uint appID) {
    if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Contains(appID)) {
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

  internal async Task<List<BoostableApp>> NextBoosterApps(int size = 1) {
    List<BoostableApp> apps = [];
    List<uint> pendingAppIDs = [];

    // Get from sleeping list first
    DateTime currentTime = DateTime.Now;
    for (int index = 0; index < SleepingApps.Count && apps.Count < size; index++) {
      BoostableApp app = SleepingApps[index];
      if ((currentTime - app.LastPlayedTime).TotalHours > AchievementsBooster.GlobalConfig.MaxBoostingHours) {
        SleepingApps.RemoveAt(index);
        index--;
        app.LastPlayedTime = currentTime;
        app.ContinuousBoostingHours = 0;
        apps.Add(app);
      }
    }

    // Get from boostable queue
    while (BoostableAppQueue.Count > 0 && apps.Count < size) {
      uint appID = BoostableAppQueue.Dequeue();
      (EGetAppStatus status, BoostableApp? app) = await GetApp(appID).ConfigureAwait(false);
      switch (status) {
        case EGetAppStatus.OK:
          ArgumentNullException.ThrowIfNull(app);
          apps.Add(app);
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

    return apps;
  }

  internal async Task<BoostableApp?> GetBoostableApp(uint appID) {
    //if (!OwnedGames.ContainsKey(appID)) {
    //  // Oh God! Why?
    //  Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.NotOwnedGame, appID));
    //  return null;
    //}

    if (!IsBoostableApp(appID)) {
      //if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Contains(appID)) {
      //  Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.AppInASFBlacklist, appID));
      //}
      return null;
    }

    (EGetAppStatus _, BoostableApp? app) = await GetApp(appID).ConfigureAwait(false);
    return app;
  }

  private async Task<(EGetAppStatus status, BoostableApp?)> GetApp(uint appID) {
    ProductInfo? productInfo = await GetProduct(appID).ConfigureAwait(false);
    if (productInfo == null) {
      Logger.Warning($"Can't get product info for app {appID}");
      return (EGetAppStatus.ProductNotFound, null);
    }

    if (!productInfo.IsBoostable) {
      //TODO: Consider adding to the NonBoostableApps set
      Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.InvalidApp, appID));
      return (EGetAppStatus.NonBoostable, null);
    }

    if (productInfo.IsVACEnabled) {
      _ = AchievementsBooster.GlobalCache.VACApps.Add(appID);
      if (AchievementsBooster.GlobalConfig.IgnoreAppWithVAC) {
        Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.VACEnabled, appID));
        return (EGetAppStatus.NonBoostable, null);
      }
    }

    if (AchievementsBooster.GlobalConfig.IgnoreAppWithDLC && productInfo.DLCs.Count > 0) {
      _ = NonBoostableApps.Add(appID);
      return (EGetAppStatus.NonBoostable, null);
    }

    if (AchievementsBooster.GlobalConfig.IgnoreDevelopers.Count > 0) {
      foreach (string developer in AchievementsBooster.GlobalConfig.IgnoreDevelopers) {
        if (productInfo.Developers.Contains(developer)) {
          _ = NonBoostableApps.Add(appID);
          return (EGetAppStatus.NonBoostable, null);
        }
      }
    }

    if (AchievementsBooster.GlobalConfig.IgnorePublishers.Count > 0) {
      foreach (string publisher in AchievementsBooster.GlobalConfig.IgnorePublishers) {
        if (productInfo.Publishers.Contains(publisher)) {
          _ = NonBoostableApps.Add(appID);
          return (EGetAppStatus.NonBoostable, null);
        }
      }
    }

    UserStatsResponse? response = await BoosterHandler.GetStats(appID).ConfigureAwait(false);
    List<StatData>? statDatas = response?.StatDatas;
    if (statDatas == null || statDatas.Count == 0) {
      _ = AchievementsBooster.GlobalCache.NonAchievementApps.Add(appID);
      Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.AchievementsNotAvailable, appID));
      productInfo.IsBoostable = false;
      //TODO: Consider adding to the NonBoostableApps set, same above
      return (EGetAppStatus.NonBoostable, null);
    }

    List<StatData> unlockableStats = statDatas.Where(e => e.Unlockable()).ToList();
    if (unlockableStats.Count == 0) {
      _ = Cache.PerfectGames.Add(appID);
      Logger.Debug(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, appID));
      return (EGetAppStatus.NonBoostable, null);
    }

    AchievementPercentages? percentages = await GetAchievementPercentages(appID).ConfigureAwait(false);
    return percentages == null
      ? (EGetAppStatus.AchievementPercentagesNotFound, null)
      : (EGetAppStatus.OK, new BoostableApp(appID, productInfo, percentages));
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
    }
    finally {
      _ = semaphore.Release();
      _ = Holder.AchievementPercentagesSemaphores.TryRemove(appID, out _);
    }

    return percentages;
  }

}

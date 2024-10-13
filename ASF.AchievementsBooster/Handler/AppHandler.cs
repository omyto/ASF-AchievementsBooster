using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using AchievementsBooster.Logger;
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
  private readonly BoosterHandler BoosterHandler;
  private readonly PLogger Logger;

  private HashSet<uint> NonBoostableApps { get; }

  internal AppHandler(BotCache cache, BoosterHandler boosterHandler, PLogger logger) {
    Cache = cache;
    BoosterHandler = boosterHandler;
    Logger = logger;
    NonBoostableApps = [.. Cache.PerfectGames];
  }

  internal void IgnoreApp(uint appID) => NonBoostableApps.Add(appID);

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  internal bool IsBoostableApp(uint appID) {
    if (ASF.GlobalConfig != null && ASF.GlobalConfig.Blacklist.Contains(appID)) {
      return false;
    }

    if (AchievementsBooster.GlobalCache.NonAchievementApps.Contains(appID)) {
      return false;
    }

    if (AchievementsBooster.Config.IgnoreAppWithVAC && AchievementsBooster.GlobalCache.VACApps.Contains(appID)) {
      return false;
    }

    return !NonBoostableApps.Contains(appID);
  }

  internal async Task<ProductInfo?> GetProduct(uint appID) {
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

  internal async Task<AchievementPercentages?> GetAchievementPercentages(uint appID) {
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

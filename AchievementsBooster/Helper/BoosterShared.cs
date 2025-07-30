using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using AchievementsBooster.Model;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Helper;

internal static class BoosterShared {
  /** Locks */
  private static ConcurrentDictionary<uint, Lazy<SemaphoreSlim>> ProductInfoLocks { get; } = new();
  private static ConcurrentDictionary<uint, Lazy<SemaphoreSlim>> AchievementRatesLocks { get; } = new();

  /** Semaphores */
  internal static SemaphoreSlim AchievementsFilterSemaphore { get; } = new(1, 1);

  /** Caches */
  internal static LRUCache<ProductInfo> ProductCache { get; } = new(128);
  internal static LRUCache<AchievementRates> AchievementRatesCache { get; } = new(128);

  /** Constants */
  internal static string PluginName { get; } = "AchievementsBooster";
  internal static string PluginShortName { get; } = "AchvBoost";

  [field: MaybeNull, AllowNull]
  internal static string ASFVersion {
    get {
      field ??= typeof(ASF).Assembly.GetName().Version?.ToString() ?? "";
      return field ?? string.Empty;
    }
  }

  internal static Version PluginVersion {
    get {
      field ??= typeof(AchievementsBoosterPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(PluginVersion));
      return field;
    }
  }

  internal static string PluginVersionS => PluginVersion.ToString();

  internal static TimeSpan OneSeconds { get; } = TimeSpan.FromSeconds(1);
  internal static TimeSpan FiveMinutes { get; } = TimeSpan.FromMinutes(5);
  internal static TimeSpan FiveSeconds { get; } = TimeSpan.FromSeconds(5);
  internal static TimeSpan TenMinutes { get; } = TimeSpan.FromMinutes(10);

  internal static SemaphoreSlim GetProductInfoLock(uint productId)
    => ProductInfoLocks.GetOrAdd(productId, _ => new Lazy<SemaphoreSlim>(() => new SemaphoreSlim(1, 1))).Value;

  internal static SemaphoreSlim GetAchievementRatesLock(uint achievementId)
    => AchievementRatesLocks.GetOrAdd(achievementId, _ => new Lazy<SemaphoreSlim>(() => new SemaphoreSlim(1, 1))).Value;

  internal static bool RemoveProductInfoLock(uint productId)
    => ProductInfoLocks.TryRemove(productId, out _);

  internal static bool RemoveAchievementRatesLock(uint achievementId)
    => AchievementRatesLocks.TryRemove(achievementId, out _);
}


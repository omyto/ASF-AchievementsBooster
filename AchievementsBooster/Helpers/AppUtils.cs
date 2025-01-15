using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Data;
using AchievementsBooster.Handler;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;

namespace AchievementsBooster.Helpers;

internal static class AppUtils {
  private static readonly Lazy<string> ASFVersion = new(() => typeof(ASF).Assembly.GetName().Version?.ToString() ?? "");

  private static class Holder {
    internal static ConcurrentDictionary<uint, SemaphoreSlim> ProductSemaphores { get; } = new();
    internal static ConcurrentDictionary<uint, ProductInfo> ProductDictionary { get; } = new();
    internal static ConcurrentDictionary<uint, SemaphoreSlim> AchievementPercentagesSemaphores { get; } = new();
    internal static ConcurrentDictionary<uint, AchievementPercentages> AchievementPercentagesDictionary { get; } = new();
  }

  internal static async Task<ProductInfo?> GetProduct(uint appID, BoosterHandler boosterHandler, Logger logger) {
    SemaphoreSlim semaphore = Holder.ProductSemaphores.GetOrAdd(appID, _ => new SemaphoreSlim(1, 1));
    await semaphore.WaitAsync().ConfigureAwait(false);

    ProductInfo? product = null;
    try {
      if (!Holder.ProductDictionary.TryGetValue(appID, out product)) {
        product = await boosterHandler.GetProductInfo(appID).ConfigureAwait(false);
        if (product != null) {
          _ = Holder.ProductDictionary.TryAdd(appID, product);
        }
      }
#if DEBUG
      else {
        logger.Trace($"Get product infor for app {product.FullName} from cache");
      }
#endif
    }
    finally {
      _ = semaphore.Release();
      _ = Holder.ProductSemaphores.TryRemove(appID, out _);
    }

    return product;
  }

  internal static async Task<AchievementPercentages?> GetAchievementPercentages(uint appID, BoosterHandler boosterHandler, Logger logger) {
    SemaphoreSlim semaphore = Holder.AchievementPercentagesSemaphores.GetOrAdd(appID, _ => new SemaphoreSlim(1, 1));
    await semaphore.WaitAsync().ConfigureAwait(false);

    AchievementPercentages? percentages = null;
    try {
      if (!Holder.AchievementPercentagesDictionary.TryGetValue(appID, out percentages)) {
        percentages = await boosterHandler.GetAchievementPercentages(appID).ConfigureAwait(false);
        if (percentages != null) {
          _ = Holder.AchievementPercentagesDictionary.TryAdd(appID, percentages);
        }
      }
#if DEBUG
      else {
        logger.Trace($"Get achievement percentages for app {appID} from cache");
      }
#endif
    }
    finally {
      _ = semaphore.Release();
      _ = Holder.AchievementPercentagesSemaphores.TryRemove(appID, out _);
    }

    return percentages;
  }

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  internal static async Task<List<uint>?> FilterAchievementsApps(HashSet<uint> ownedGames, BoosterHandler boosterHandler) {
    if (ASF.WebBrowser == null) {
      throw new InvalidOperationException(nameof(ASF.WebBrowser));
    }

    string machineName = Environment.MachineName;

    Dictionary<string, string> headers = new() {
      { "asf-version",  ASFVersion.Value },
      { "ab-booster", boosterHandler.Identifier },
      { "ab-version", Constants.PluginVersionString },
      { "ab-signature", GenerateSignature() },
    };

    Dictionary<string, uint[]> data = new() {
      { "appids", ownedGames.ToArray() }
    };

    ObjectResponse<AchievementsFilterResponse>? response = await ArchiWebHandler.WebLimitRequest(
      Constants.AchievementsFilterHost,
      async () => await ASF.WebBrowser.UrlPostToJsonObject<AchievementsFilterResponse, IDictionary<string, uint[]>>(Constants.AchievementsFilterAPI, headers, data, maxTries: 3, rateLimitingDelay: 1000).ConfigureAwait(false)
    ).ConfigureAwait(false);

    if (response == null || response.StatusCode != HttpStatusCode.OK || response.Content == null || response.Content.Success != true) {
      return null;
    }

    return response.Content.AppIDs?.ToList() ?? [];
  }

  private static string GenerateSignature() {
    ulong unixTime = Utilities.GetUnixTime();
    return unixTime.ToString(CultureInfo.InvariantCulture);
  }
}

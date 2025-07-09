using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Handler;
using AchievementsBooster.Model;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;

namespace AchievementsBooster.Helpers;

internal static class AppUtils {
  private static readonly Lazy<string> ASFVersion = new(() => typeof(ASF).Assembly.GetName().Version?.ToString() ?? "");
  private static readonly SemaphoreSlim AchievementsFilterSemaphore = new(1, 1);

  private static class Holder {
    internal static ConcurrentDictionary<uint, SemaphoreSlim> ProductSemaphores { get; } = new();
    internal static ConcurrentDictionary<uint, ProductInfo> ProductDictionary { get; } = new();
    internal static ConcurrentDictionary<uint, SemaphoreSlim> AchievementPercentagesSemaphores { get; } = new();
    internal static ConcurrentDictionary<uint, AchievementPercentages> AchievementPercentagesDictionary { get; } = new();
  }

  internal static async Task<ProductInfo?> GetProduct(uint appID, SteamClientHandler clientHandler, Logger logger, CancellationToken cancellationToken) {
    SemaphoreSlim semaphore = Holder.ProductSemaphores.GetOrAdd(appID, _ => new SemaphoreSlim(1, 1));
    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    ProductInfo? product = null;
    try {
      if (!Holder.ProductDictionary.TryGetValue(appID, out product)) {
        product = await clientHandler.GetProductInfo(appID, WebBrowser.MaxTries, cancellationToken).ConfigureAwait(false);
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

  internal static async Task<AchievementPercentages?> GetAchievementPercentages(uint appID, SteamClientHandler clientHandler, Logger logger, CancellationToken cancellationToken) {
    SemaphoreSlim semaphore = Holder.AchievementPercentagesSemaphores.GetOrAdd(appID, _ => new SemaphoreSlim(1, 1));
    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    AchievementPercentages? percentages = null;
    try {
      if (!Holder.AchievementPercentagesDictionary.TryGetValue(appID, out percentages)) {
        percentages = await clientHandler.GetAchievementPercentages(appID, cancellationToken).ConfigureAwait(false);
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

  //TODO: Maybe no need cancel token for this method
  internal static async Task<List<uint>?> FilterAchievementsApps(HashSet<uint> ownedGames, SteamClientHandler clientHandler, Logger logger, CancellationToken cancellationToken) {
    if (ASF.WebBrowser == null) {
      throw new InvalidOperationException(nameof(ASF.WebBrowser));
    }

    List<uint>? result = null;
    await AchievementsFilterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    try {
      BoosterGlobalConfig config = AchievementsBoosterPlugin.GlobalConfig;

      Dictionary<string, string> headers = new() {
        { "ab-booster", clientHandler.Identifier },
        { "ab-version", Constants.PluginVersionString },
        { "asf-version",  ASFVersion.Value }
      };

      Dictionary<string, object> data = new() {
        { "appIds", ownedGames.ToArray() },
        { "restriction",  new Dictionary<string, object>() {
          { "vac", config.RestrictAppWithVAC },
          { "dlc", config.RestrictAppWithDLC },
          { "developers", config.RestrictDevelopers.ToArray() },
          { "publishers", config.RestrictPublishers.ToArray() },
          { "excludedAppIds", config.UnrestrictedApps.ToArray() }
        }}
      };

      ObjectResponse<AchievementsFilterResponse>? response = await ASF.WebBrowser.UrlPostToJsonObject<AchievementsFilterResponse, IDictionary<string, object>>(
        Constants.AchievementsFilterAPI, headers, data, maxTries: 3, rateLimitingDelay: 1000, cancellationToken: cancellationToken).ConfigureAwait(false);
      result = response?.Content?.AppIDs;

      if (response == null) {
        logger.Warning($"Can't get achievements filter response");
      }
      else if (response.StatusCode != HttpStatusCode.OK) {
        logger.Warning($"Achievements filter response status {response.StatusCode}");
      }
      else if (response.Content == null) {
        logger.Warning($"Achievements filter response content is null");
      }
      else if (response.Content.Success != true) {
        logger.Warning($"Achievements filter response unsuccess");
      }
    }
    finally {
      _ = AchievementsFilterSemaphore.Release();
    }

    return result;
  }
}

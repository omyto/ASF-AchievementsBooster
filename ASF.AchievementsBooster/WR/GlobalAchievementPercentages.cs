using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using AchievementsBooster.Extensions;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using SteamKit2;

namespace AchievementsBooster.WR;

internal class GlobalAchievementPercentages(Bot bot) {
  private readonly Bot Bot = bot;
  private static readonly ConcurrentDictionary<uint, Dictionary<string, double>> AppsAchievementPercentages = [];

  internal async Task<Dictionary<string, double>> GetAppAchievementPercentages(uint appid) {
    if (AppsAchievementPercentages.TryGetValue(appid, out Dictionary<string, double>? percentages)) {
      return percentages;
    }

    percentages = await GetGlobalAchievementPercentagesForApp(appid).ConfigureAwait(false);
    if (percentages == null) {
      Bot.ArchiLogger.LogGenericWarning($"No global achievement percentages exist for app {appid}", Caller.Name());
      return [];
    }

    if (!AppsAchievementPercentages.TryAdd(appid, percentages)) {
      Bot.ArchiLogger.LogGenericWarning($"The global achievement percentages for app {appid} are already present", Caller.Name());
    }
    return percentages;
  }

  internal async Task<Dictionary<string, double>?> GetGlobalAchievementPercentagesForApp(uint appid) {
    Dictionary<string, object?> arguments = new(2, StringComparer.Ordinal) {
      { "gameid", appid },
      { "t", DateTime.UtcNow.ToFileTimeUtc() }
    };

    using WebAPI.AsyncInterface steamUserStatsService = Bot.SteamConfiguration.GetAsyncWebAPIInterface("ISteamUserStats");
    steamUserStatsService.Timeout = Bot.ArchiWebHandler.WebBrowser.Timeout;
    KeyValue? response = null;
    try {
      response = await ArchiWebHandler.WebLimitRequest(
        WebAPI.DefaultBaseAddress,
        async () => await steamUserStatsService.CallAsync(HttpMethod.Get, "GetGlobalAchievementPercentagesForApp", 2, arguments).ConfigureAwait(false)
      ).ConfigureAwait(false);
    } catch (TaskCanceledException e) {
      Bot.ArchiLogger.LogGenericDebuggingException(e, Caller.Name());
    } catch (Exception e) {
      Bot.ArchiLogger.LogGenericWarningException(e, Caller.Name());
    }

    if (response == null) {
      Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, 1/*WebBrowser.MaxTries*/), Caller.Name());
      return null;
    }

    return ParseGlobalAchievementPercentagesForApp(appid, response["achievements"].Children);
  }

  private Dictionary<string, double> ParseGlobalAchievementPercentagesForApp(uint appid, List<KeyValue> achievements) {
    Dictionary<string, double> percentages = [];
    for (int i = 0; i < achievements.Count; i++) {
      KeyValue achievement = achievements[i];
      string? apiName = achievement["name"].Value;
      if (apiName == null) {
        Bot.ArchiLogger.LogGenericWarning($"App {appid} has an invalid internal achievement name", Caller.Name());
        continue;
      }

      double percent = achievement["percent"].AsDouble(double.MinValue);
      if (percent < 0) {
        Bot.ArchiLogger.LogGenericWarning($"Achievement '{apiName}' has no percentage data", Caller.Name());
        percent = 0;
      }

      if (!percentages.TryAdd(apiName, percent)) {
        Bot.ArchiLogger.LogGenericWarning($"Internal achievement name '{apiName}' for app {appid} already exists", Caller.Name());
      }
    }

    return percentages;
  }
}

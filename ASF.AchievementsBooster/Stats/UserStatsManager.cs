using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using AchievementsBooster.Extensions;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using SteamKit2;
using SteamKit2.Internal;

namespace AchievementsBooster.Stats;

internal sealed class UserStatsManager : ClientMsgHandler {
  private static readonly ConcurrentDictionary<uint, Dictionary<string, double>> AppsAchievementPercentages = [];

  private readonly Bot Bot;

  private SteamUnifiedMessages.UnifiedService<IPlayer>? UnifiedPlayerService;

  internal UserStatsManager(Bot bot) => Bot = bot;

  internal void Setup() {
    ArgumentNullException.ThrowIfNull(Client);
    UnifiedPlayerService = Client.GetHandler<SteamUnifiedMessages>()?.CreateService<IPlayer>();
  }

  internal async Task<(TaskResult result, List<StatData> statDatas)> GetStats(uint appID) {
    GetUserStatsResponseCallback? response = await RequestUserStats(appID).ConfigureAwait(false);
    if (response == null || !response.Success) {
      string message = string.Format(CultureInfo.CurrentCulture, Messages.StatsNotFound, appID);
      Bot.ArchiLogger.LogGenericDebug(message, Caller.Name());
      return (new TaskResult(false, message), []);
    }

    return (new TaskResult(true), response.StatDatas);
  }

  internal async Task<(TaskResult result, int unachievedCount)> UnlockNextStat(uint appID) {
    GetUserStatsResponseCallback? response = await RequestUserStats(appID).ConfigureAwait(false);
    if (response == null || !response.Success) {
      string message = string.Format(CultureInfo.CurrentCulture, Messages.StatsNotFound, appID);
      Bot.ArchiLogger.LogGenericDebug(message, Caller.Name());
      return (new TaskResult(false, message), -1);
    }

    List<StatData> unlockableStats = response.StatDatas.Where(e => e.Unlockable()).ToList();
    if (unlockableStats.Count == 0) {
      Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, appID), Caller.Name());
      return (new TaskResult(true), 0);
    }

    // Order by global achievement percentages
    Dictionary<string, double> achievementPercentages = await GetAppAchievementPercentages(appID).ConfigureAwait(false);
    if (achievementPercentages.Count == 0) {
      string message = $"No global achievement percentages for app {appID}";
      Bot.ArchiLogger.LogGenericWarning(message, Caller.Name());
      return (new TaskResult(false, message), 0);
    }

    foreach (StatData statData in unlockableStats) {
      if (achievementPercentages.TryGetValue(statData.APIName ?? "", out double percentage)) {
        statData.Percentage = percentage;
      }
    }
    unlockableStats.Sort((x, y) => y.Percentage.CompareTo(x.Percentage));

    // Unlock next achievement
    StatData stat = unlockableStats.First();
    bool success = await UnlockStat(appID, stat, response.CrcStats).ConfigureAwait(false);
    if (success) {
      Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementSuccess, stat.Name, appID), Caller.Name());
    } else {
      Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementFailed, stat.Name, appID), Caller.Name());
    }
    return (new TaskResult(success), success ? unlockableStats.Count - 1 : unlockableStats.Count);
  }

  private async Task<bool> UnlockStat(ulong appID, StatData stat, uint crcStats) {
    ClientMsgProtobuf<CMsgClientStoreUserStats2> request = new(EMsg.ClientStoreUserStats2) {
      SourceJobID = Client.GetNextJobID(),
      Body = {
        game_id = appID,
        settor_steam_id = Bot.SteamID,
        settee_steam_id = Bot.SteamID,
        explicit_reset = false,
        crc_stats = crcStats
      }
    };

    List<CMsgClientStoreUserStats2.Stats> stats = UserStatsUtils.GetStatsToSet([], stat).ToList();
    request.Body.stats.AddRange(stats);
    Client.Send(request);
    StoreUserStatsResponseCallback response = await new AsyncJob<StoreUserStatsResponseCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);
    return response.Success;
  }

  private async Task<GetUserStatsResponseCallback?> RequestUserStats(ulong appID) {
    ClientMsgProtobuf<CMsgClientGetUserStats> request = new(EMsg.ClientGetUserStats) {
      SourceJobID = Client.GetNextJobID(),
      Body = {
        game_id = appID,
        steam_id_for_user = Bot.SteamID,
      }
    };

    Client.Send(request);
    try {
      return await new AsyncJob<GetUserStatsResponseCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);
    } catch (Exception e) {
      Bot.ArchiLogger.LogGenericException(e, Caller.Name());
      return null;
    }
  }

  /** ClientMsgHandler */
  [SuppressMessage("Style", "IDE0010:Add missing cases", Justification = "<Pending>")]
  public override void HandleMsg(IPacketMsg packetMsg) {
    ArgumentNullException.ThrowIfNull(packetMsg);

    switch (packetMsg.MsgType) {
      //case EMsg.ClientGetUserStats:
      //  break;
      case EMsg.ClientGetUserStatsResponse:
        ClientMsgProtobuf<CMsgClientGetUserStatsResponse> getUserStatsResponse = new(packetMsg);
        Client.PostCallback(new GetUserStatsResponseCallback(packetMsg.TargetJobID, getUserStatsResponse.Body));
        break;
      //case EMsg.ClientStoreUserStats:
      //  break;
      //case EMsg.ClientStoreUserStats2:
      //  break;
      case EMsg.ClientStoreUserStatsResponse:
        ClientMsgProtobuf<CMsgClientStoreUserStatsResponse> storeUserStatsResponse = new(packetMsg);
        Client.PostCallback(new StoreUserStatsResponseCallback(packetMsg.TargetJobID, storeUserStatsResponse.Body));
        break;
      default:
        //ASF.ArchiLogger.LogGenericTrace($"[Booster] Not Handler {packetMsg.MsgType}");
        break;
    }
  }

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

  private async Task<Dictionary<string, double>?> GetGlobalAchievementPercentagesForApp(uint appid) {
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

  internal async Task<List<Dictionary<string, object>>?> GetGameAchievements(uint appid) {
    ArgumentNullException.ThrowIfNull(Client);
    ArgumentNullException.ThrowIfNull(UnifiedPlayerService);

    if (!Client.IsConnected) {
      return null;
    }

    CPlayer_GetGameAchievements_Request request = new() {
      appid = appid,
      language = "english"
    };

    SteamUnifiedMessages.ServiceMethodResponse response;

    try {
      response = await UnifiedPlayerService.SendMessage(e => e.GetGameAchievements(request)).ToLongRunningTask().ConfigureAwait(false);
    } catch (Exception e) {
      ASF.ArchiLogger.LogGenericWarningException(e, Caller.Name());
      return null;
    }

    if (response.Result != EResult.OK) {
      return null;
    }

    CPlayer_GetGameAchievements_Response body = response.GetDeserializedResponse<CPlayer_GetGameAchievements_Response>();
    List<Dictionary<string, object>> list = [];
    foreach (CPlayer_GetGameAchievements_Response.Achievement achievement in body.achievements) {
      list.Add(new Dictionary<string, object> {
        { "internal_name", achievement.internal_name },
        { "localized_name", achievement.localized_name },
        { "localized_desc", achievement.localized_desc },
        { "icon", achievement.icon },
        { "icon_gray", achievement.icon_gray },
        { "hidden", achievement.hidden },
        { "player_percent_unlocked", achievement.player_percent_unlocked }
      });
    }

    return list;
  }
}

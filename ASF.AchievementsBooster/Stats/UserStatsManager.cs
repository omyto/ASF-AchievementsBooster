using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;

namespace AchievementsBooster.Stats;

internal sealed class UserStatsManager : ClientMsgHandler {
  private readonly Bot Bot;

  internal UserStatsManager(Bot bot) => Bot = bot;

  /// <summary>
  /// 
  /// </summary>
  /// <param name="appID"></param>
  /// <returns></returns>
  internal async Task<(TaskResult result, List<StatData> statDatas)> GetStats(ulong appID) {
    GetUserStatsResponseCallback? response = await RequestUserStats(appID).ConfigureAwait(false);
    if (response != null && response.Success) {
      return (new TaskResult(true), response.StatDatas);
    }

    string message = string.Format(CultureInfo.CurrentCulture, Messages.StatsNotFound, appID);
    Bot.ArchiLogger.LogGenericDebug(message);
    return (new TaskResult(false, message), []);
  }

  /// <summary>
  /// 
  /// </summary>
  /// <param name="appID"></param>
  /// <returns></returns>
  internal async Task<(TaskResult result, int unachievedCount)> UnlockNextStat(ulong appID) {
    GetUserStatsResponseCallback? response = await RequestUserStats(appID).ConfigureAwait(false);
    if (response == null || !response.Success) {
      string message = string.Format(CultureInfo.CurrentCulture, Messages.StatsNotFound, appID);
      Bot.ArchiLogger.LogGenericWarning(message);
      return (new TaskResult(false, message), 0);
    }

    List<StatData> unlockableStats = response.StatDatas.Where(e => e.Unlockable()).ToList();
    if (unlockableStats.Count == 0) {
      Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, appID));
      return (new TaskResult(true), 0);
    }

    StatData stat = unlockableStats.First();
    bool success = await UnlockStat(appID, stat, response.CrcStats).ConfigureAwait(false);
    if (success) {
      Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementSuccess, stat.Name, appID));
    } else {
      Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementFailed, stat.Name, appID));
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
      Bot.ArchiLogger.LogGenericException(e);
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
}

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using AchievementsBooster.Stats;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Web;
using SteamKit2;
using SteamKit2.Internal;
using static SteamKit2.SteamApps.PICSProductInfoCallback;
using GameAchievement = SteamKit2.Internal.CPlayer_GetGameAchievements_Response.Achievement;

namespace AchievementsBooster;

internal sealed class BoosterHandler : ClientMsgHandler {
  // Global
  private static readonly ConcurrentDictionary<uint, ProductInfo> Products = new();
  private static readonly ConcurrentDictionary<uint, FrozenDictionary<string, double>> AppsAchievementPercentages = [];

  private readonly Bot Bot;

  private SteamUnifiedMessages.UnifiedService<IPlayer>? UnifiedPlayerService;

  internal BoosterHandler(Bot bot) => Bot = bot;

  internal void Init() {
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
    FrozenDictionary<string, double>? achievementPercentages = await GetAppAchievementPercentages(appID).ConfigureAwait(false);
    if (achievementPercentages == null || achievementPercentages.Count == 0) {
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

  internal async Task<FrozenDictionary<string, double>?> GetAppAchievementPercentages(uint appid) {
    if (AppsAchievementPercentages.TryGetValue(appid, out FrozenDictionary<string, double>? percentages)) {
      return percentages;
    }

    List<GameAchievement>? gameAchievements = await GetGameAchievements(appid).ConfigureAwait(false);
    if (gameAchievements == null) {
      Bot.ArchiLogger.LogGenericWarning($"No global achievement percentages exist for app {appid}", Caller.Name());
      return null;
    }

    percentages = gameAchievements.ToFrozenDictionary(k => k.internal_name, v => double.TryParse(v.player_percent_unlocked, out double value) ? value : 0.0);
    if (!AppsAchievementPercentages.TryAdd(appid, percentages)) {
      Bot.ArchiLogger.LogGenericWarning($"The global achievement percentages for app {appid} are already present", Caller.Name());
    }
    return percentages;
  }

  internal async Task<List<GameAchievement>?> GetGameAchievements(uint appid) {
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
    return body.achievements;
  }

  internal async Task<ProductInfo?> GetProductInfo(uint appID, byte maxTries = WebBrowser.MaxTries) {
    // Get if exist
    if (Products.TryGetValue(appID, out ProductInfo? info)) {
      return info;
    }

    ulong? accessToken = await GetPICSAccessTokens(appID, maxTries).ConfigureAwait(false);
    SteamApps.PICSRequest request = new(appID, accessToken ?? 0);

    AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet? productInfoResultSet = null;

    for (byte i = 0; i < maxTries && productInfoResultSet == null && Bot.IsConnectedAndLoggedOn; i++) {
      try {
        productInfoResultSet = await Bot.SteamApps.PICSGetProductInfo(request.ToEnumerable(), []).ToLongRunningTask().ConfigureAwait(false);
      } catch (Exception e) {
        Bot.ArchiLogger.LogGenericWarningException(e, Caller.Name());
      }
    }

    if (productInfoResultSet?.Results == null) {
      return null;
    }

    foreach (Dictionary<uint, PICSProductInfo> productInfoApps in productInfoResultSet.Results.Select(static result => result.Apps)) {
      if (!productInfoApps.TryGetValue(appID, out PICSProductInfo? productInfoApp)) {
        continue;
      }

      KeyValue productInfo = productInfoApp.KeyValues;
      if (productInfo == KeyValue.Invalid) {
        Bot.ArchiLogger.LogNullError(productInfo, Caller.Name());
        break;
      }

      KeyValue commonProductInfo = productInfo["common"];
      if (commonProductInfo == KeyValue.Invalid) {
        continue;
      }

      info = new ProductInfo(productInfoApp);
      _ = Products.TryAdd(appID, info);
      return info;
    }

    return null;
  }

  private async Task<ulong?> GetPICSAccessTokens(uint appID, byte maxTries = WebBrowser.MaxTries) {
    SteamApps.PICSTokensCallback? tokenCallback = null;

    for (byte i = 0; i < maxTries && tokenCallback == null && Bot.IsConnectedAndLoggedOn; i++) {
      try {
        tokenCallback = await Bot.SteamApps.PICSGetAccessTokens(appID, null).ToLongRunningTask().ConfigureAwait(false);
      } catch (Exception e) {
        Bot.ArchiLogger.LogGenericWarningException(e, Caller.Name());
      }
    }

    return tokenCallback?.AppTokens.GetValueOrDefault(appID);
  }
}

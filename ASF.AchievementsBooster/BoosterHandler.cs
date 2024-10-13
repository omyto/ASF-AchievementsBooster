using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using AchievementsBooster.Logger;
using AchievementsBooster.Stats;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Web;
using SteamKit2;
using SteamKit2.Internal;
using GameAchievement = SteamKit2.Internal.CPlayer_GetGameAchievements_Response.Achievement;
using PICSProductInfo = SteamKit2.SteamApps.PICSProductInfoCallback.PICSProductInfo;

namespace AchievementsBooster;

internal sealed class BoosterHandler : ClientMsgHandler {
  private readonly Bot Bot;
  private readonly PLogger Logger;

  private SteamUnifiedMessages.UnifiedService<IPlayer>? UnifiedPlayerService;

  internal BoosterHandler(Bot bot, PLogger logger) {
    Bot = bot;
    Logger = logger;
  }

  internal void Init() {
    ArgumentNullException.ThrowIfNull(Client);
    UnifiedPlayerService = Client.GetHandler<SteamUnifiedMessages>()?.CreateService<IPlayer>();
  }

  /** ClientMsgHandler */
  [SuppressMessage("Style", "IDE0010:Add missing cases", Justification = "<Pending>")]
  public override void HandleMsg(IPacketMsg packetMsg) {
    ArgumentNullException.ThrowIfNull(packetMsg);
    switch (packetMsg.MsgType) {
      case EMsg.ClientGetUserStatsResponse:
        ClientMsgProtobuf<CMsgClientGetUserStatsResponse> getUserStatsResponse = new(packetMsg);
        Client.PostCallback(new GetUserStatsResponseCallback(packetMsg.TargetJobID, getUserStatsResponse.Body));
        break;
      case EMsg.ClientStoreUserStatsResponse:
        ClientMsgProtobuf<CMsgClientStoreUserStatsResponse> storeUserStatsResponse = new(packetMsg);
        Client.PostCallback(new StoreUserStatsResponseCallback(packetMsg.TargetJobID, storeUserStatsResponse.Body));
        break;
      default:
        //ASF.ArchiLogger.LogGenericTrace($"[Booster] Not Handler {packetMsg.MsgType}");
        break;
    }
  }

  internal async Task<List<StatData>?> GetStats(uint appID) {
    GetUserStatsResponseCallback? response = await RequestUserStats(appID).ConfigureAwait(false);
    if (response == null || !response.Success) {
      Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.StatsNotFound, appID));
      return null;
    }

    return response.StatDatas;
  }

  internal async Task<bool> UnlockNextStat(AppBooster app) {
    GetUserStatsResponseCallback? response = await RequestUserStats(app.ID).ConfigureAwait(false);
    if (response == null || !response.Success) {
      string message = string.Format(CultureInfo.CurrentCulture, Messages.StatsNotFound, app.ID);
      Logger.Debug(message);
      return false;
    }

    StatData? nextStat = app.GetUpcomingUnlockableStat(response.StatDatas);
    if (nextStat == null) {
      Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.NoUnlockableStats, app.ID));
      return false;
    }

    // Unlock next achievement
    bool success = await UnlockStat(app.ID, nextStat, response.CrcStats).ConfigureAwait(false);
    app.UpdateUnlockStatus(nextStat, success);

    if (success) {
      Logger.Info(string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementSuccess, nextStat.Name, app.ID));
    }
    else {
      Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.UnlockAchievementFailed, nextStat.Name, app.ID));
    }
    return success;
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
    }
    catch (Exception e) {
      Logger.Exception(e);
      return null;
    }
  }

  internal async Task<AchievementPercentages?> GetAchievementPercentages(uint appID) {
    List<GameAchievement>? gameAchievements = await GetGameAchievements(appID).ConfigureAwait(false);
    if (gameAchievements == null || gameAchievements.Count == 0) {
      Logger.Warning($"No global achievement percentages exist for app {appID}");
      return null;
    }

    FrozenDictionary<string, double> percentages = gameAchievements.ToFrozenDictionary(k => k.internal_name, v => double.TryParse(v.player_percent_unlocked, out double value) ? value : 0.0);
    return new AchievementPercentages(appID, percentages);
  }

  private async Task<List<GameAchievement>?> GetGameAchievements(uint appid) {
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
    }
    catch (Exception exception) {
      AchievementsBooster.GlobalLogger.Warning(exception);
      return null;
    }

    if (response.Result != EResult.OK) {
      return null;
    }

    CPlayer_GetGameAchievements_Response body = response.GetDeserializedResponse<CPlayer_GetGameAchievements_Response>();
    return body.achievements;
  }

  internal async Task<ProductInfo?> GetProductInfo(uint appID, byte maxTries = WebBrowser.MaxTries) {
    ulong? accessToken = await GetPICSAccessTokens(appID, maxTries).ConfigureAwait(false);
    SteamApps.PICSRequest request = new(appID, accessToken ?? 0);

    AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet? productInfoResultSet = null;

    for (byte i = 0; i < maxTries && productInfoResultSet == null && Bot.IsConnectedAndLoggedOn; i++) {
      try {
        productInfoResultSet = await Bot.SteamApps.PICSGetProductInfo(request.ToEnumerable(), []).ToLongRunningTask().ConfigureAwait(false);
      }
      catch (Exception exception) {
        Logger.Warning(exception);
      }
    }

    if (productInfoResultSet?.Results == null) {
      return null;
    }

    foreach (Dictionary<uint, PICSProductInfo> productInfoApps in productInfoResultSet.Results.Select(static result => result.Apps)) {
      if (!productInfoApps.TryGetValue(appID, out PICSProductInfo? productInfoApp)) {
        continue;
      }
      Logger.Trace($"PICSProductInfo {appID}: {JsonSerializer.Serialize(productInfoApp)}");

      KeyValue productInfo = productInfoApp.KeyValues;
      if (productInfo == KeyValue.Invalid) {
        Logger.NullError(productInfo);
        break;
      }

      KeyValue commonProductInfo = productInfo["common"];
      if (commonProductInfo == KeyValue.Invalid) {
        continue;
      }

      ProductInfo? info = new(productInfoApp);
      Logger.Trace($"ProductInfo {appID}: {JsonSerializer.Serialize(info)}");
      return info;
    }

    return null;
  }

  private async Task<ulong?> GetPICSAccessTokens(uint appID, byte maxTries = WebBrowser.MaxTries) {
    SteamApps.PICSTokensCallback? tokenCallback = null;

    for (byte i = 0; i < maxTries && tokenCallback == null && Bot.IsConnectedAndLoggedOn; i++) {
      try {
        tokenCallback = await Bot.SteamApps.PICSGetAccessTokens(appID, null).ToLongRunningTask().ConfigureAwait(false);
      }
      catch (Exception exception) {
        Logger.Warning(exception);
      }
    }

    return tokenCallback?.AppTokens.GetValueOrDefault(appID);
  }
}

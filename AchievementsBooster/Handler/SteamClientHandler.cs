using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Handler.Callback;
using AchievementsBooster.Handler.Exceptions;
using AchievementsBooster.Helper;
using AchievementsBooster.Model;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;
using PICSProductInfo = SteamKit2.SteamApps.PICSProductInfoCallback.PICSProductInfo;

namespace AchievementsBooster.Handler;

internal sealed class SteamClientHandler : ClientMsgHandler {
  private readonly Bot Bot;
  private readonly Logger Logger;
  private const int RequestDelay = 600;

  private Player? UnifiedPlayerService;

  internal SteamClientHandler(Bot bot, Logger logger) {
    Bot = bot;
    Logger = logger;
  }

  internal void Init() {
    ArgumentNullException.ThrowIfNull(Client);
    UnifiedPlayerService = Client.GetHandler<SteamUnifiedMessages>()?.CreateService<Player>();
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

  internal async Task<UserStatsResponse?> GetStats(uint appID, CancellationToken cancellationToken) {
    GetUserStatsResponseCallback? response = await RequestUserStats(appID, cancellationToken).ConfigureAwait(false);
    if (response == null || !response.Success) {
      Logger.Trace(string.Format(CultureInfo.CurrentCulture, Messages.StatsNotFound, appID));
      return null;
    }

    return response.UserStats;
  }

  internal async Task<bool> UnlockStat(ulong appID, StatData stat, uint crcStats) {
    if (!Client.IsConnected) {
      throw new BotDisconnectedException();
    }

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

  private async Task<GetUserStatsResponseCallback?> RequestUserStats(ulong appID, CancellationToken cancellationToken) {
    if (!Client.IsConnected) {
      throw new BotDisconnectedException();
    }

    ClientMsgProtobuf<CMsgClientGetUserStats> request = new(EMsg.ClientGetUserStats) {
      SourceJobID = Client.GetNextJobID(),
      Body = {
        game_id = appID,
        steam_id_for_user = Bot.SteamID,
      }
    };

    try {
      await Task.Delay(RequestDelay, cancellationToken).ConfigureAwait(false);
      Client.Send(request);
      GetUserStatsResponseCallback responseCallback = await new AsyncJob<GetUserStatsResponseCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);
      return responseCallback;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
      throw;
    }
    catch (Exception e) {
      Logger.Exception(e);
      return null;
    }
  }

  internal async Task<AchievementRates?> GetAchievementCompletionRates(uint appid, CancellationToken cancellationToken) {
    ArgumentNullException.ThrowIfNull(Client);
    ArgumentNullException.ThrowIfNull(UnifiedPlayerService);

    if (!Client.IsConnected) {
      throw new BotDisconnectedException();
    }

    CPlayer_GetGameAchievements_Request request = new() {
      appid = appid,
      language = "english"
    };

    SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetGameAchievements_Response> response;

    try {
      await Task.Delay(RequestDelay, cancellationToken).ConfigureAwait(false);
      response = await UnifiedPlayerService.GetGameAchievements(request).ToLongRunningTask().ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
      throw;
    }
    catch (Exception exception) {
      Logger.Shared.Warning(exception);
      return null;
    }

    if (response.Result != EResult.OK) {
      Logger.Trace($"Get game achievements completion rates failed for app {appid}: {response.Result}");
      Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.GameAchievementNotExist, appid));
      return null;
    }

    return new AchievementRates(appid, response.Body.achievements);
  }

  internal async Task<List<AchievementProgress>?> GetAchievementsProgress(List<uint> appids, CancellationToken cancellationToken) {
    ArgumentNullException.ThrowIfNull(Client);
    ArgumentNullException.ThrowIfNull(UnifiedPlayerService);

    if (!Client.IsConnected) {
      throw new BotDisconnectedException();
    }

    CPlayer_GetAchievementsProgress_Request request = new() {
      steamid = Bot.SteamID,
      language = "english"
    };
    request.appids.AddRange(appids);

    SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetAchievementsProgress_Response> response;

    try {
      await Task.Delay(RequestDelay, cancellationToken).ConfigureAwait(false);
      response = await UnifiedPlayerService.GetAchievementsProgress(request).ToLongRunningTask().ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
      throw;
    }
    catch (Exception exception) {
      Logger.Shared.Warning(exception);
      return null;
    }

    return response.Result == EResult.OK ? response.Body.achievement_progress.Select(e => new AchievementProgress(e)).ToList() : null;
  }

  internal async Task<ProductInfo?> GetProductInfo(uint appID, byte maxTries, CancellationToken cancellationToken) {
    ulong? accessToken = await GetPICSAccessTokens(appID, maxTries, cancellationToken).ConfigureAwait(false);
    SteamApps.PICSRequest request = new(appID, accessToken ?? 0);

    AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet? productInfoResultSet = null;

    for (byte i = 0; i < maxTries && productInfoResultSet == null; i++) {
      if (!Bot.IsConnectedAndLoggedOn) {
        throw new BotDisconnectedException();
      }

      try {
        await Task.Delay(RequestDelay, cancellationToken).ConfigureAwait(false);
        productInfoResultSet = await Bot.SteamApps.PICSGetProductInfo(request.ToEnumerable(), []).ToLongRunningTask().ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        throw;
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

      KeyValue productInfo = productInfoApp.KeyValues;
      if (productInfo == KeyValue.Invalid) {
        Logger.NullError(productInfo);
        break;
      }

      KeyValue commonProductInfo = productInfo["common"];
      if (commonProductInfo == KeyValue.Invalid) {
        continue;
      }

      ProductInfo info = ProductUtils.GenerateProduct(productInfoApp);

#if DEBUG_PRODUCT
      string fileName = $"{appID} ({info.Name}) - {Bot.BotName}";
      await FileSerializer.WriteToFile(productInfoApp, fileName).ConfigureAwait(false);
#endif

      return info;
    }

    return null;
  }

  private async Task<ulong?> GetPICSAccessTokens(uint appID, byte maxTries, CancellationToken cancellationToken) {
    SteamApps.PICSTokensCallback? tokenCallback = null;

    for (byte i = 0; i < maxTries && tokenCallback == null; i++) {
      if (!Bot.IsConnectedAndLoggedOn) {
        throw new BotDisconnectedException();
      }

      try {
        await Task.Delay(RequestDelay, cancellationToken).ConfigureAwait(false);
        tokenCallback = await Bot.SteamApps.PICSGetAccessTokens(appID, null).ToLongRunningTask().ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        throw;
      }
      catch (Exception exception) {
        Logger.Warning(exception);
      }
    }

    return tokenCallback?.AppTokens.GetValueOrDefault(appID);
  }
}

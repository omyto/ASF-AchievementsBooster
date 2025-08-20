using System;
using System.Collections.Generic;
using AchievementsBooster.Helper;
using SteamKit2;
using CMsgClientGetUserStatsResponse = SteamKit2.Internal.CMsgClientGetUserStatsResponse;

namespace AchievementsBooster.Handler.Callback;

internal sealed class UserStatsResponse {
  internal uint CrcStats { get; }
  internal List<StatData>? StatDatas { get; }

  internal UserStatsResponse(uint crcStats, List<StatData>? stats) {
    CrcStats = crcStats;
    StatDatas = stats;
  }
}

internal sealed class GetUserStatsResponseCallback : CallbackMsg {
  internal bool Success { get; }

  internal CMsgClientGetUserStatsResponse Response { get; }

  internal GetUserStatsResponseCallback(JobID jobID, CMsgClientGetUserStatsResponse response) {
    ArgumentNullException.ThrowIfNull(jobID);
    ArgumentNullException.ThrowIfNull(response);

    JobID = jobID;
    Response = response;
    Success = EResult.OK == (EResult) response.eresult;
  }

  internal UserStatsResponse? ParseResponse(Logger logger)
    => Success ? new(Response.crc_stats, UserStatsUtils.ParseResponse(Response, logger)) : null;
}

using System;
using System.Collections.Generic;
using AchievementsBooster.Helpers;
using SteamKit2;
using CMsgClientGetUserStatsResponse = SteamKit2.Internal.CMsgClientGetUserStatsResponse;

namespace AchievementsBooster.Handler.Callback;

internal sealed class UserStatsResponse {
  internal uint CrcStats { get; }
  internal List<StatData> StatDatas { get; }

  internal UserStatsResponse(List<StatData> stats, uint crcStats) {
    StatDatas = stats;
    CrcStats = crcStats;
  }
}

internal sealed class GetUserStatsResponseCallback : CallbackMsg {
  internal bool Success { get; }

  internal UserStatsResponse? UserStats { get; }

  internal GetUserStatsResponseCallback(JobID jobID, CMsgClientGetUserStatsResponse msg) {
    ArgumentNullException.ThrowIfNull(jobID);
    ArgumentNullException.ThrowIfNull(msg);

    JobID = jobID;
    Success = EResult.OK == (EResult) msg.eresult;

    if (Success) {
      UserStats = new(UserStatsUtils.ParseResponse(msg) ?? [], msg.crc_stats);
      UserStatsDump.Dump(msg, UserStats.StatDatas);
    }
  }
}

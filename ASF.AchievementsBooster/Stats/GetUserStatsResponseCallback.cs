using System;
using System.Collections.Generic;
using SteamKit2;
using CMsgClientGetUserStatsResponse = SteamKit2.Internal.CMsgClientGetUserStatsResponse;

namespace AchievementsBooster.Stats;

internal sealed class GetUserStatsResponseCallback : CallbackMsg {

  internal bool Success { get; }

  internal uint CrcStats { get; }

  internal List<StatData> StatDatas { get; } = [];

  internal GetUserStatsResponseCallback(JobID jobID, CMsgClientGetUserStatsResponse msg) {
    ArgumentNullException.ThrowIfNull(jobID);
    ArgumentNullException.ThrowIfNull(msg);

    JobID = jobID;
    CrcStats = msg.crc_stats;
    Success = EResult.OK == (EResult) msg.eresult;

    if (Success) {
      StatDatas = UserStatsUtils.ParseResponse(msg) ?? [];
      UserStatsDump.Dump(msg, StatDatas);
    }
  }
}

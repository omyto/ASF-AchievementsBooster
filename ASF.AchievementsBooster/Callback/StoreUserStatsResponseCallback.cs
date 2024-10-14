using System;
using SteamKit2;
using SteamKit2.Internal;

namespace AchievementsBooster.Callback;

internal sealed class StoreUserStatsResponseCallback : CallbackMsg {

  internal bool Success { get; }

  internal uint CrcStats { get; }

  internal StoreUserStatsResponseCallback(JobID jobID, CMsgClientStoreUserStatsResponse msg) {
    ArgumentNullException.ThrowIfNull(jobID);
    ArgumentNullException.ThrowIfNull(msg);

    JobID = jobID;
    Success = EResult.OK == (EResult) msg.eresult;
    CrcStats = msg.crc_stats;
  }
}

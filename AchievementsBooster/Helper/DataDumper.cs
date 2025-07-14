using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AchievementsBooster.Model;
using ArchiSteamFarm.Helpers.Json;

namespace AchievementsBooster.Helper;

internal static class DataDumper {
  internal static async Task DumpProductsInfo(Booster booster, List<uint> appids) {
    foreach (uint appid in appids) {
      ProductInfo? p = await booster.AppRepository.GetProductInfo(appid, new()).ConfigureAwait(false);
      if (p == null) {
        booster.Logger.Debug($"Product infor of {appid} not found!");
        continue;
      }

      booster.Logger.Debug($"Product info of {appid} found:{Environment.NewLine}{p.ToJsonText(true)}");
    }
  }

  internal static async Task DumpAchievementsProgress(Booster booster, List<uint> appids) {
    List<AchievementProgress>? values = await booster.SteamClientHandler.GetAchievementsProgress(appids, new()).ConfigureAwait(false);
    if (values == null) {
      booster.Logger.Debug($"Achievements progress of {string.Join(",", appids)} not found!");
      return;
    }

    foreach (AchievementProgress progress in values) {
      booster.Logger.Debug($"Achievement progress of {progress.AppID}:{Environment.NewLine}{progress.ToJsonText(true)}");
    }
  }

  internal static async Task DumpStatsData(Booster booster, List<uint> appids) {
    foreach (uint appid in appids) {
      List<StatData>? stats = (await booster.SteamClientHandler.GetStats(appid, new()).ConfigureAwait(false))?.StatDatas;
      if (stats == null) {
        booster.Logger.Debug($"Stats data of {appid} not found!");
        continue;
      }

      booster.Logger.Debug($"Stats data of {appid} found:{Environment.NewLine}{stats.ToJsonText(true)}");
    }
  }
}

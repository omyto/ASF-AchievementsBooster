using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AchievementsBooster.Model;
using ArchiSteamFarm.Helpers.Json;

namespace AchievementsBooster.Helper;

internal static class DataDumper {
  internal static async Task DumpProductsInfo(Booster booster, List<uint> appids) {
    foreach (uint appid in appids) {
      ProductInfo? p = await booster.AppManager.GetProductInfo(appid, new()).ConfigureAwait(false);
      if (p == null) {
        booster.Logger.Debug($"Product infor of {appid} not found!");
        continue;
      }

      booster.Logger.Debug($"Product info of {appid} found:{Environment.NewLine}{p.ToJsonText(true)}");
    }
  }
}

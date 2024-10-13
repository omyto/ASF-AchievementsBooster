using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Localization;
using SteamKit2;
using CMsgClientGetUserStatsResponse = SteamKit2.Internal.CMsgClientGetUserStatsResponse;

namespace AchievementsBooster.Stats;

internal static class UserStatsDump {
  private static readonly ConcurrentHashSet<ulong> GameIDs = [];

  internal static void Dump(CMsgClientGetUserStatsResponse msg, List<StatData> statDatas) {
    if (GameIDs.Add(msg.game_id)) {
      AchievementsBooster.GlobalLogger.Trace($"User Stats Response for game id: {msg.game_id}");
      AchievementsBooster.GlobalLogger.Trace(JsonSerializer.Serialize(UserStatsResponseToDictionary(msg)));
      AchievementsBooster.GlobalLogger.Trace($"User Stats Data for game id: {msg.game_id}");
      AchievementsBooster.GlobalLogger.Trace(JsonSerializer.Serialize(statDatas));
    }
  }

  private static Dictionary<string, object> UserStatsResponseToDictionary(CMsgClientGetUserStatsResponse msg) {
    using MemoryStream ms = new(msg.schema ?? []);
    KeyValue kv = new();
    if (!kv.TryReadAsBinary(ms)) {
      AchievementsBooster.GlobalLogger.Error(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(msg.schema)));
    }

    return new Dictionary<string, object> {
      { "game_id", msg.game_id },
      { "stats", msg.stats?.Select(StatsToDictionary).ToList() ?? [] },
      { "achievement_blocks", msg.achievement_blocks?.Select(AchievementBlocksToDictionary).ToList() ?? [] },
      { "schema", KVToDictionary(kv) }
    };
  }

  private static Dictionary<string, object> KVToDictionary(KeyValue kv) {
    Dictionary<string, object> dic = [];
    if (kv.Name != null) {
      dic.Add("key", kv.Name);
    }
    if (kv.Value != null) {
      dic.Add("value", kv.Value);
    }
    if (kv.Children != null && kv.Children.Count > 0) {
      List<Dictionary<string, object>> children = [];
      foreach (KeyValue child in kv.Children) {
        children.Add(KVToDictionary(child));
      }
      dic.Add("children", children);
    }

    return dic;
  }

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  private static Dictionary<string, uint> StatsToDictionary(CMsgClientGetUserStatsResponse.Stats stat) {
    if (stat != null) {
      return new Dictionary<string, uint> {
        { "stat_id", stat.stat_id },
        { "stat_value", stat.stat_value }
      };
    }
    return [];
  }

  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  private static Dictionary<string, object> AchievementBlocksToDictionary(CMsgClientGetUserStatsResponse.Achievement_Blocks achievementBlocks) {
    if (achievementBlocks != null) {
      return new Dictionary<string, object> {
        { "achievement_id", achievementBlocks.achievement_id },
        { "unlock_time",  achievementBlocks.unlock_time }
      };
    }
    return [];
  }
}

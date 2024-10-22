using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ArchiSteamFarm.Localization;
using SteamKit2;
using SteamKit2.Internal;

namespace AchievementsBooster.Helpers;

/// This source code was referenced from https://github.com/Rudokhvist/ASF-Achievement-Manager and belongs to Rudokhvist.
/// Special thanks to Rudokhvist

public sealed class StatData {
  public uint StatNum { get; set; }
  public int BitNum { get; set; }
  public bool IsSet { get; set; }
  public bool Restricted { get; set; }
  public uint Dependancy { get; set; }
  public uint DependancyValue { get; set; }
  public string? DependancyName { get; set; }
  public string? Name { get; set; }
  public uint StatValue { get; set; }
  public string APIName { get; set; } = string.Empty;
  public double Percentage { get; set; }

  internal bool Unlockable() => !IsSet && !Restricted;
}

internal static class UserStatsUtils {

  internal static List<StatData>? ParseResponse(CMsgClientGetUserStatsResponse response) {
    List<StatData> result = [];
    KeyValue keyValues = new();
    if (response.schema != null) {
      using (MemoryStream ms = new(response.schema)) {
        if (!keyValues.TryReadAsBinary(ms)) {
          AchievementsBooster.Logger.Error(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(response.schema)));
          return null;
        };
      }

      //first we enumerate all real achievements
      foreach (KeyValue stat in keyValues.Children.Find(child => child.Name == "stats")?.Children ?? []) {
        if (stat.Children.Find(child => child.Name == "type")?.Value == "4") {
          foreach (KeyValue achievement in stat.Children.Find(child => child.Name == "bits")?.Children ?? []) {
            if (int.TryParse(achievement.Name, out int bitNum)) {
              if (uint.TryParse(stat.Name, out uint statNum)) {
                uint? stat_value = response?.stats?.Find(statElement => statElement.stat_id == statNum)?.stat_value;
                bool isSet = stat_value != null && (stat_value & ((uint) 1 << bitNum)) != 0;

                bool restricted = achievement.Children.Find(child => child.Name == "permission") != null;

                string? dependancyName = achievement.Children.Find(child => child.Name == "progress") == null ? "" : achievement.Children.Find(child => child.Name == "progress")?.Children?.Find(child => child.Name == "value")?.Children?.Find(child => child.Name == "operand1")?.Value;

                if (!uint.TryParse(achievement.Children.Find(child => child.Name == "progress") == null ? "0" : achievement.Children.Find(child => child.Name == "progress")!.Children.Find(child => child.Name == "max_val")?.Value, out uint dependancyValue)) {
                  AchievementsBooster.Logger.Error(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(dependancyValue)));
                  return null;
                }

                string lang = "english";

                //Dictionary<string, string> countryLanguageMap = new()
                //{
                //  { "portuguese (brazil)", "brazilian" },
                //  { "korean", "koreana" },
                //  { "chinese (traditional)", "tchinese" },
                //  { "chinese (simplified)", "schinese" }
                //  };

                //if (countryLanguageMap.TryGetValue(lang, out string? value)) {
                //  lang = value;
                //} else {
                //  if (lang.IndexOf('(') > 0) {
                //    lang = lang[..(lang.IndexOf('(') - 1)];
                //  }
                //}
                //if (achievement.Children.Find(child => child.Name == "display")?.Children?.Find(child => child.Name == "name")?.Children?.Find(child => child.Name == lang) == null) {
                //  lang = "english"; // Fallback
                //}

                string? name = achievement.Children.Find(child => child.Name == "display")?.Children?.Find(child => child.Name == "name")?.Children?.Find(child => child.Name == lang)?.Value;
                string? apiName = achievement.Children.Find(child => child.Name == "name")?.Value;
                result.Add(new StatData() {
                  StatNum = statNum,
                  BitNum = bitNum,
                  IsSet = isSet,
                  Restricted = restricted,
                  DependancyValue = dependancyValue,
                  DependancyName = dependancyName,
                  Dependancy = 0,
                  Name = name,
                  StatValue = stat_value ?? 0,
                  APIName = apiName ?? string.Empty,
                });

              }
            }
          }
        }
      }
      //Now we update all dependancies
      foreach (KeyValue stat in keyValues.Children.Find(child => child.Name == "stats")?.Children ?? []) {
        if (stat.Children.Find(child => child.Name == "type")?.Value == "1") {
          if (uint.TryParse(stat.Name, out uint statNum)) {
            bool restricted = stat.Children.Find(child => child.Name == "permission") != null;
            string? name = stat.Children.Find(child => child.Name == "name")?.Value;
            if (name != null) {
              StatData? parentStat = result.Find(item => item.DependancyName == name);
              if (parentStat != null) {
                parentStat.Dependancy = statNum;
                if (restricted && !parentStat.Restricted) {
                  parentStat.Restricted = true;
                }
              }
            }
          }
        }
      }
    }
    return result;
  }

  internal static IEnumerable<CMsgClientStoreUserStats2.Stats> GetStatsToSet(List<CMsgClientStoreUserStats2.Stats> statsToSet, StatData statToSet, bool set = true) {
    if (statToSet == null) {
      yield break; //it should never happen
    }

    CMsgClientStoreUserStats2.Stats? currentstat = statsToSet.Find(stat => stat.stat_id == statToSet.StatNum);
    if (currentstat == null) {
      currentstat = new CMsgClientStoreUserStats2.Stats() {
        stat_id = statToSet.StatNum,
        stat_value = statToSet.StatValue
      };
      yield return currentstat;
    }

    uint statMask = (uint) 1 << statToSet.BitNum;
    if (set) {
      currentstat.stat_value |= statMask;
    }
    else {
      currentstat.stat_value &= ~statMask;
    }

    if (!string.IsNullOrEmpty(statToSet.DependancyName)) {
      CMsgClientStoreUserStats2.Stats? dependancystat = statsToSet.Find(stat => stat.stat_id == statToSet.Dependancy);
      if (dependancystat == null) {
        dependancystat = new CMsgClientStoreUserStats2.Stats() {
          stat_id = statToSet.Dependancy,
          stat_value = set ? statToSet.DependancyValue : 0
        };
        yield return dependancystat;
      }
    }
  }

}

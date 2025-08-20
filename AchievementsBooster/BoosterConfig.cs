using System;
using System.Collections.Generic;
using System.Linq;
using AchievementsBooster.Storage;

namespace AchievementsBooster;

internal class BoosterConfig {
  internal bool AutoStart { get; private set; }
  internal int MaxConcurrentlyBoostingApps { get; private set; }
  internal int MinBoostInterval { get; private set; }
  internal int MaxBoostInterval { get; private set; }
  internal int BoostDurationPerApp { get; private set; }
  internal int BoostRestTimePerApp { get; private set; }
  internal int RestTimePerDay { get; private set; }
  internal bool BoostHoursWhenIdle { get; private set; }
  internal bool RestrictAppWithVAC { get; private set; }
  internal bool RestrictAppWithDLC { get; private set; }

  private HashSet<uint> Blacklist { get; set; }
  private HashSet<uint> UnrestrictedApps { get; set; }
  private HashSet<string> RestrictDevelopers { get; set; }
  private HashSet<string> RestrictPublishers { get; set; }

  internal IReadOnlySet<uint> BlacklistReadOnly => Blacklist;
  internal IReadOnlySet<uint> UnrestrictedAppsReadOnly => UnrestrictedApps;
  internal IReadOnlySet<string> RestrictDevelopersReadOnly => RestrictDevelopers;
  internal IReadOnlySet<string> RestrictPublishersReadOnly => RestrictPublishers;

  internal bool IsBlacklistedApp(uint appID) => Blacklist.Contains(appID);
  internal bool IsUnrestrictedApp(uint appID) => UnrestrictedApps.Contains(appID);
  internal bool IsRestrictedByDeveloper(string developer) => RestrictDevelopers.Contains(developer);
  internal bool IsRestrictedByPublisher(string publisher) => RestrictPublishers.Contains(publisher);

  internal BoosterConfig(string botName, BoosterBotConfig? botConfig = null) {
    BoosterGlobalConfig globalConfig = AchievementsBoosterPlugin.GlobalConfig;

    AutoStart = botConfig?.AutoStart == true || globalConfig.AutoStartBots.Contains(botName);

    MaxConcurrentlyBoostingApps = botConfig?.MaxConcurrentlyBoostingApps ?? globalConfig.MaxConcurrentlyBoostingApps;
    MinBoostInterval = botConfig?.MinBoostInterval ?? globalConfig.MinBoostInterval;
    MaxBoostInterval = botConfig?.MaxBoostInterval ?? globalConfig.MaxBoostInterval;
    BoostDurationPerApp = botConfig?.BoostDurationPerApp ?? globalConfig.BoostDurationPerApp;
    BoostRestTimePerApp = botConfig?.BoostRestTimePerApp ?? globalConfig.BoostRestTimePerApp;
    RestTimePerDay = botConfig?.RestTimePerDay ?? globalConfig.RestTimePerDay;
    BoostHoursWhenIdle = botConfig?.BoostHoursWhenIdle ?? globalConfig.BoostHoursWhenIdle;
    RestrictAppWithVAC = botConfig?.RestrictAppWithVAC ?? globalConfig.RestrictAppWithVAC;
    RestrictAppWithDLC = botConfig?.RestrictAppWithDLC ?? globalConfig.RestrictAppWithDLC;

    Blacklist = globalConfig.Blacklist.ToHashSet();
    if (botConfig?.Blacklist != null && botConfig.Blacklist.Count > 0) {
      foreach (uint appID in botConfig.Blacklist) {
        _ = Blacklist.Add(appID);
      }
    }

    UnrestrictedApps = globalConfig.UnrestrictedApps.ToHashSet();
    if (botConfig?.UnrestrictedApps != null && botConfig.UnrestrictedApps.Count > 0) {
      foreach (uint appID in botConfig.UnrestrictedApps) {
        _ = UnrestrictedApps.Add(appID);
      }
    }

    RestrictDevelopers = globalConfig.RestrictDevelopers.ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (botConfig?.RestrictDevelopers != null && botConfig.RestrictDevelopers.Count > 0) {
      foreach (string developer in botConfig.RestrictDevelopers) {
        _ = RestrictDevelopers.Add(developer);
      }
    }

    RestrictPublishers = globalConfig.RestrictPublishers.ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (botConfig?.RestrictPublishers != null && botConfig.RestrictPublishers.Count > 0) {
      foreach (string publisher in botConfig.RestrictPublishers) {
        _ = RestrictPublishers.Add(publisher);
      }
    }
  }
}

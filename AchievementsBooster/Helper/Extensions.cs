using System;
using System.Diagnostics.CodeAnalysis;
using AchievementsBooster.Storage;

namespace AchievementsBooster.Helper;

public static class Extensions {
  public static bool IsBoosterRestingTime(this DateTime time) {
    if (BoosterConfig.Global.RestTimePerDay == 0) {
      return false;
    }

    DateTime weakUpTime = new(time.Year, time.Month, time.Day, 6, 0, 0, 0);
    if (time < weakUpTime) {
      DateTime restingStartTime = weakUpTime.AddMinutes(-BoosterConfig.Global.RestTimePerDay);
      if (time > restingStartTime) {
        return true;
      }
    }

    return false;
  }

  [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
  public static TimeSpan AddRandomMinutes(this TimeSpan timeSpan, int minutes)
    => minutes > 0 ? timeSpan.Add(TimeSpan.FromSeconds(Random.Shared.Next(0, (minutes * 60) + 1))) : timeSpan;
}

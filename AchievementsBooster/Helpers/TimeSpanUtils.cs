using System;

namespace AchievementsBooster.Helpers;

internal static class TimeSpanUtils {
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
  internal static TimeSpan RandomInMinutesRange(int min, int max)
    => min == max ? TimeSpan.Zero : TimeSpan.FromSeconds(Random.Shared.Next(min * 60, max * 60));
}

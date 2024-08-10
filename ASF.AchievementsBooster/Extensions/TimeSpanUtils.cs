using System;

namespace AchievementsBooster.Extensions;

internal static class TimeSpanUtils {
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
  internal static TimeSpan InMinutesRange(int min, int max) => TimeSpan.FromSeconds(Random.Shared.Next(min * 60, max * 60));
}

using System;
using System.Diagnostics.CodeAnalysis;

namespace AchievementsBooster.Helper;

internal static class TimeSpanUtils {
  [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  internal static TimeSpan RandomInMinutesRange(int min, int max) {
    if (min < 0) {
      min = 0;
    }

    if (min > max) {
      return TimeSpan.Zero;
    }

    if (min == max) {
      return min > 0 ? TimeSpan.FromMinutes(min) : TimeSpan.Zero;
    }

    return TimeSpan.FromSeconds(Random.Shared.Next(min * 60, max * 60));
  }
}

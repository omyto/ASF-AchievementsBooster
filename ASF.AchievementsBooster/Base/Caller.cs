using System;
using System.Runtime.CompilerServices;

namespace AchievementsBooster.Base;

internal static class Caller {
  internal static string Name([CallerMemberName] string? callerMethodName = null) {
    ArgumentException.ThrowIfNullOrEmpty(callerMethodName);
    return $"{nameof(AchievementsBooster)}|{callerMethodName}";
  }
}

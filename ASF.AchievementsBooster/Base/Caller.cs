using System;
using System.Runtime.CompilerServices;

namespace AchievementsBooster.Base;

public static class Caller {
  public static string Name([CallerMemberName] string? callerMethodName = null) {
    ArgumentException.ThrowIfNullOrEmpty(callerMethodName);
    return $"{nameof(AchievementsBooster)}|{callerMethodName}";
  }
}

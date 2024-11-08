using System;
using AchievementsBooster.Helpers;

namespace AchievementsBooster;

public class BoostingImpossibleException : Exception {

  public BoostingImpossibleException() : base(Messages.BoostingImpossible) {
  }

  public BoostingImpossibleException(string message) : base(message) {
  }

  public BoostingImpossibleException(string message, Exception innerException) : base(message, innerException) {
  }

  public static void ThrowIfPlayingImpossible(bool condition) {
    if (condition) {
      throw new BoostingImpossibleException();
    }
  }
}

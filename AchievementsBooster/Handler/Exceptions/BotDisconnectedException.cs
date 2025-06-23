using System;
using ArchiSteamFarm.Localization;

namespace AchievementsBooster.Handler.Exceptions;

internal sealed class BotDisconnectedException : OperationCanceledException {
  public BotDisconnectedException() : base(Strings.BotDisconnected) {
  }

  public BotDisconnectedException(string message) : base(message) {
  }

  public BotDisconnectedException(string message, Exception innerException) : base(message, innerException) {
  }
}

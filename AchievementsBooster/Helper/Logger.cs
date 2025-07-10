using System;
using System.Runtime.CompilerServices;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.NLog;

namespace AchievementsBooster.Helper;

public sealed class Logger(ArchiLogger archiLogger, string pluginName = Constants.PluginName) {
  internal static readonly Logger Shared = new(ASF.ArchiLogger);

  private readonly ArchiLogger ArchiLogger = archiLogger;
  private readonly string PluginName = pluginName;

  public void Trace(string message, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogGenericTrace(message, PreviousMethodName(callerMethodName));

  public void Debug(string message, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogGenericDebug(message, PreviousMethodName(callerMethodName));

  public void Debug(Exception exception, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogGenericDebuggingException(exception, PreviousMethodName(callerMethodName));

  public void Info(string message, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogGenericInfo(message, PreviousMethodName(callerMethodName));

  public void Warning(string message, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogGenericWarning(message, PreviousMethodName(callerMethodName));

  public void Warning(Exception exception, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogGenericWarningException(exception, PreviousMethodName(callerMethodName));

  public void Error(string message, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogGenericError(message, PreviousMethodName(callerMethodName));

  public void Exception(Exception exception, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogGenericException(exception, PreviousMethodName(callerMethodName));

  public void NullError(object? nullObject, [CallerArgumentExpression(nameof(nullObject))] string? nullObjectName = null, [CallerMemberName] string? callerMethodName = null)
    => ArchiLogger.LogNullError(nullObject, nullObjectName, PreviousMethodName(callerMethodName));

  private string PreviousMethodName(string? callerMethodName) {
    ArgumentException.ThrowIfNullOrEmpty(callerMethodName);
    return $"{PluginName}|{callerMethodName}";
  }
}

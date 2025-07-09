using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AchievementsBooster.Helpers;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Interaction;

namespace AchievementsBooster;

internal static class CommandsCoordinator {

  internal static async Task<string?> OnBotCommand(Bot bot, EAccess access, string _/*message*/, string[] args, ulong steamID) {
    if (!Enum.IsDefined(access)) {
      throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
    }

    if (args == null || args.Length == 0) {
      return null;
    }

    switch (args[0].ToUpperInvariant()) {
      case "ABSTART":
        return args.Length == 1 ? ResponseStart(access, bot) : await ResponseStart(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
      case "ABSTOP":
        return args.Length == 1 ? ResponseStop(access, bot) : await ResponseStop(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
      case "ABSTATUS":
        return args.Length == 1 ? ResponseStatus(access, bot) : await ResponseStatus(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
      default:
        break;
    }
    return null;
  }

  // Generic

  private static string? InvokeBot(EAccess access, Bot bot, string methodName, Type[]? types = null, object[]? parameters = null, EAccess accessRequired = EAccess.Master) {
    if (access < accessRequired) {
      return null;
    }

    if (AchievementsBoosterPlugin.GetBooster(bot) is not Booster booster) {
      return Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }

    MethodInfo? method = booster.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, types ?? Type.EmptyTypes);
    if (method != null) {
      string? response = method.Invoke(booster, parameters) as string;
      return bot.Commands.FormatBotResponse(response ?? "No response");
    }
    else {
      return Commands.FormatStaticResponse($"Method {methodName} not found");
    }
  }

  private static async Task<string?> InvokeBots(EAccess access, string botNames, ulong steamID, string methodName, Type[]? types = null, object[]? parameters = null, EAccess accessRequired = EAccess.Master) {
    ArgumentException.ThrowIfNullOrEmpty(botNames);

    HashSet<Bot>? bots = Bot.GetBots(botNames);

    if (bots == null || bots.Count == 0) {
      return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
    }

    IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => InvokeBot(Commands.GetProxyAccess(bot, access, steamID), bot, methodName, types, parameters, accessRequired)))).ConfigureAwait(false);

    List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result))!];

    return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
  }

  // -- Start

  private static string? ResponseStart(EAccess access, Bot bot)
    => InvokeBot(access, bot, nameof(Booster.Start), [typeof(bool)], [true], EAccess.Master);

  private static async Task<string?> ResponseStart(EAccess access, string botNames, ulong steamID = 0)
    => await InvokeBots(access, botNames, steamID, nameof(Booster.Start), [typeof(bool)], [true], EAccess.Master).ConfigureAwait(false);

  // -- Stop

  private static string? ResponseStop(EAccess access, Bot bot)
    => InvokeBot(access, bot, nameof(Booster.Stop), null, null, EAccess.Master);

  private static async Task<string?> ResponseStop(EAccess access, string botNames, ulong steamID = 0)
    => await InvokeBots(access, botNames, steamID, nameof(Booster.Stop), null, null, EAccess.Master).ConfigureAwait(false);

  // -- Status

  private static string? ResponseStatus(EAccess access, Bot bot)
  => InvokeBot(access, bot, nameof(Booster.GetStatus), null, null, EAccess.Master);

  private static async Task<string?> ResponseStatus(EAccess access, string botNames, ulong steamID = 0)
    => await InvokeBots(access, botNames, steamID, nameof(Booster.GetStatus), null, null, EAccess.Master).ConfigureAwait(false);
}

using System.Collections.Generic;
using System.Globalization;
using System;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Interaction;
using ArchiSteamFarm.Localization;
using System.Linq;
using System.ComponentModel;
using AchievementsBooster.Helpers;

namespace AchievementsBooster.Handler;

internal static class CommandsHandler {

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
      default:
        break;
    }
    return null;
  }

  private static async Task<string?> ResponseStart(EAccess access, string botNames, ulong steamID = 0) {
    ArgumentException.ThrowIfNullOrEmpty(botNames);

    HashSet<Bot>? bots = Bot.GetBots(botNames);

    if (bots == null || bots.Count == 0) {
      return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
    }

    IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => ResponseStart(Commands.GetProxyAccess(bot, access, steamID), bot)))).ConfigureAwait(false);

    List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result))!];

    return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
  }

  private static string? ResponseStart(EAccess access, Bot bot) {
    if (access < EAccess.Master) {
      return null;
    }

    if (!AchievementsBooster.Boosters.TryGetValue(bot, out Booster? booster)) {
      return Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }

    string response = booster.Start(true);
    return bot.Commands.FormatBotResponse(response);
  }

  private static string? ResponseStop(EAccess access, Bot bot) {
    if (access < EAccess.Master) {
      return null;
    }

    if (!AchievementsBooster.Boosters.TryGetValue(bot, out Booster? booster)) {
      return Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }

    string response = booster.Stop();
    return bot.Commands.FormatBotResponse(response);
  }

  private static async Task<string?> ResponseStop(EAccess access, string botNames, ulong steamID = 0) {
    ArgumentException.ThrowIfNullOrEmpty(botNames);

    HashSet<Bot>? bots = Bot.GetBots(botNames);

    if (bots == null || bots.Count == 0) {
      return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
    }

    IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => ResponseStop(Commands.GetProxyAccess(bot, access, steamID), bot)))).ConfigureAwait(false);

    List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result))!];

    return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
  }
}

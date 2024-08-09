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
using AchievementsBooster.Base;

namespace AchievementsBooster;

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
        return args.Length == 1 ? await ResponseStart(access, bot).ConfigureAwait(false) : await ResponseStart(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
      case "ABSTOP":
        return args.Length == 1 ? ResponseStop(access, bot) : await ResponseStop(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
      case "ABLOG" when args.Length == 2:
        return await ResponseLog(access, bot, args[1]).ConfigureAwait(false);
      case "ABLOG" when args.Length > 2:
        return await ResponseLog(access, steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
      case "ABNEXT" when args.Length == 2:
        return await ResponseUnlockNext(access, bot, args[1]).ConfigureAwait(false);
      case "ABNEXT" when args.Length > 2:
        return await ResponseUnlockNext(access, steamID, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
      default:
        break;
    }
    return null;
  }

  private static async Task<string?> ResponseStart(EAccess access, string botNames, ulong steamID = 0) {
    ArgumentException.ThrowIfNullOrEmpty(botNames);

    HashSet<Bot>? bots = Bot.GetBots(botNames);

    if ((bots == null) || (bots.Count == 0)) {
      return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
    }

    IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => ResponseStart(Commands.GetProxyAccess(bot, access, steamID), bot)))).ConfigureAwait(false);

    List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result))!];

    return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
  }

  private static async Task<string?> ResponseStart(EAccess access, Bot bot) {
    if (access < EAccess.Master) {
      return null;
    }

    if (!AchievementsBooster.Boosters.TryGetValue(bot, out Booster? booster)) {
      return Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }

    string response = await booster.Start(true).ConfigureAwait(false);
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

    if ((bots == null) || (bots.Count == 0)) {
      return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
    }

    IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => ResponseStop(Commands.GetProxyAccess(bot, access, steamID), bot)))).ConfigureAwait(false);

    List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result))!];

    return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
  }

  private static async Task<string?> ResponseLog(EAccess access, Bot bot, string appid) {
    if (access < EAccess.Master) {
      return null;
    }

    if (!AchievementsBooster.Boosters.TryGetValue(bot, out Booster? booster)) {
      return Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }

    if (!uint.TryParse(appid, out uint appID) || appID == 0) {
      return string.Format(CultureInfo.CurrentCulture, Messages.InvalidAppID, appid);
    }

    string response = await booster.Log(appID).ConfigureAwait(false);
    return bot.Commands.FormatBotResponse(response);
  }

  private static async Task<string?> ResponseLog(EAccess access, ulong steamID, string botNames, string appid) {
    ArgumentException.ThrowIfNullOrEmpty(botNames);

    HashSet<Bot>? bots = Bot.GetBots(botNames);

    if ((bots == null) || (bots.Count == 0)) {
      return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
    }

    IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => ResponseLog(Commands.GetProxyAccess(bot, access, steamID), bot, appid)))).ConfigureAwait(false);

    List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result))!];

    return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
  }

  private static async Task<string?> ResponseUnlockNext(EAccess access, Bot bot, string appid) {
    if (access < EAccess.Master) {
      return null;
    }

    if (!AchievementsBooster.Boosters.TryGetValue(bot, out Booster? booster)) {
      return Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }

    if (!uint.TryParse(appid, out uint appID) || appID == 0) {
      return string.Format(CultureInfo.CurrentCulture, Messages.InvalidAppID, appid);
    }

    string response = await booster.UnlockNext(appID).ConfigureAwait(false);
    return bot.Commands.FormatBotResponse(response);
  }

  private static async Task<string?> ResponseUnlockNext(EAccess access, ulong steamID, string botNames, string appid) {
    ArgumentException.ThrowIfNullOrEmpty(botNames);

    HashSet<Bot>? bots = Bot.GetBots(botNames);

    if ((bots == null) || (bots.Count == 0)) {
      return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
    }

    IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => ResponseUnlockNext(Commands.GetProxyAccess(bot, access, steamID), bot, appid)))).ConfigureAwait(false);

    List<string> responses = [.. results.Where(static result => !string.IsNullOrEmpty(result))!];

    return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
  }
}

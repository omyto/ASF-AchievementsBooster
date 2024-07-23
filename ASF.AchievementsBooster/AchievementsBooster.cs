using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Composition;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using AchievementsBooster.Base;
using AchievementsBooster.Config;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

[Export(typeof(IPlugin))]
internal sealed class AchievementsBooster : IASF, IBot, IBotModules, IBotConnection, IBotSteamClient, IBotCommand2 {

  internal static readonly ConcurrentDictionary<Bot, Booster> Boosters = new();

  internal static readonly BoosterGlobalConfig Config = new();

  public string Name => nameof(AchievementsBooster);

  public Version Version => typeof(AchievementsBooster).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

  public Task OnLoaded() {
    ASF.ArchiLogger.LogGenericInfo("Achievements Booster | Automatically boosting achievements while farming cards.");
    return Task.CompletedTask;
  }

  /** IASF */

  public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties) {
    if (additionalConfigProperties != null && additionalConfigProperties.Count > 0) {
    }
    return Task.CompletedTask;
  }

  /** IBot */

  public Task OnBotInit(Bot bot) => Task.CompletedTask;

  public Task OnBotDestroy(Bot bot) {
    RemoveBoosterBot(bot);
    return Task.CompletedTask;
  }

  /* IBotConnection */

  public Task OnBotDisconnected(Bot bot, EResult reason) {
    if (Boosters.TryGetValue(bot, out Booster? booster)) {
      _ = booster.Stop();
    } else {
      ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }
    return Task.CompletedTask;
  }

  public Task OnBotLoggedOn(Bot bot) {
    if (Boosters.TryGetValue(bot, out Booster? booster)) {
      _ = booster.Start();
    } else {
      ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }
    return Task.CompletedTask;
  }

  /** IBotModules */
  public Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
    if (additionalConfigProperties == null || additionalConfigProperties.Count == 0) {
      return Task.CompletedTask;
    }
    if (Boosters.TryGetValue(bot, out Booster? booster)) {
      return booster.OnInitModules(additionalConfigProperties);
    }
    ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    return Task.CompletedTask;
  }

  /** IBotSteamClient */

  public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
    if (Boosters.TryGetValue(bot, out Booster? botBooster)) {
      botBooster.OnSteamCallbacksInit(callbackManager);
    } else {
      ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }
    return Task.CompletedTask;
  }

  public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
    RemoveBoosterBot(bot);
    Booster booster = new(bot);
    if (!Boosters.TryAdd(bot, booster)) {
      booster.Dispose();
      ASF.ArchiLogger.LogGenericError($"Can not initial booster for bot {bot.BotName}");
      return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>(null);
    }
    return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([booster.StatsManager]);
  }

  /** IBotCommand */
  public async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) => await CommandsHandler.OnBotCommand(bot, access, message, args, steamID).ConfigureAwait(false);

  /** Internal Method */
  private static void RemoveBoosterBot(Bot bot) {
    if (Boosters.TryRemove(bot, out Booster? booster)) {
      booster.Dispose();
    }
  }
}

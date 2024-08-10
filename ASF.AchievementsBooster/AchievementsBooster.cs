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
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

[Export(typeof(IPlugin))]
internal sealed class AchievementsBooster : IASF, IBot, IBotModules, IBotConnection, IBotSteamClient, IBotCommand2 {

  internal static readonly ConcurrentDictionary<Bot, Booster> Boosters = new();

  internal static BoosterGlobalConfig Config { get; private set; } = new();

  internal static GlobalCache GlobalCache { get; private set; } = new();

  public string Name => nameof(AchievementsBooster);

  public Version Version => typeof(AchievementsBooster).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

  public Task OnLoaded() {
    ASF.ArchiLogger.LogGenericInfo("Achievements Booster | Automatically boosting achievements while farming cards.");
    GlobalCache? globalCache = GlobalCache.LoadFromDatabase();
    if (globalCache != null) {
      GlobalCache = globalCache;
    }
    return Task.CompletedTask;
  }

  /** IASF */

  public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties) {
    if (additionalConfigProperties != null && additionalConfigProperties.Count > 0) {
      if (additionalConfigProperties.TryGetValue(Constants.AchievementsBoosterConfigKey, out JsonElement configValue)) {
        BoosterGlobalConfig? config = configValue.ToJsonObject<BoosterGlobalConfig>();
        //ASF.ArchiLogger.LogGenericInfo(JsonSerializer.Serialize(config));
        if (config != null) {
          Config = config;
          if (Config.Enabled) {
            Config.Validate();
          } else {
            ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.PluginDisabledInConfig, Name), Caller.Name());
          }
        }
      }
    }
    return Task.CompletedTask;
  }

  /** IBot */

  public Task OnBotInit(Bot bot) => Task.CompletedTask;

  public Task OnBotDestroy(Bot bot) {
    if (Config.Enabled) {
      RemoveBoosterBot(bot);
    }
    return Task.CompletedTask;
  }

  /* IBotConnection */

  public Task OnBotDisconnected(Bot bot, EResult reason) {
    if (Config.Enabled) {
      if (Boosters.TryGetValue(bot, out Booster? booster)) {
        _ = booster.Stop();
      } else {
        ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName), Caller.Name());
      }
    }
    return Task.CompletedTask;
  }

  public Task OnBotLoggedOn(Bot bot) {
    if (Config.Enabled) {
      if (Boosters.TryGetValue(bot, out Booster? booster)) {
        _ = booster.Start();
      } else {
        ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName), Caller.Name());
      }
    }
    return Task.CompletedTask;
  }

  /** IBotModules */
  public Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
    if (Config.Enabled) {
      if (additionalConfigProperties == null || additionalConfigProperties.Count == 0) {
        return Task.CompletedTask;
      }
      if (Boosters.TryGetValue(bot, out Booster? booster)) {
        return booster.OnInitModules(additionalConfigProperties);
      }
      ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName), Caller.Name());
    }
    return Task.CompletedTask;
  }

  /** IBotSteamClient */

  public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
    if (Config.Enabled) {
      if (Boosters.TryGetValue(bot, out Booster? botBooster)) {
        return botBooster.OnSteamCallbacksInit(callbackManager);
      }
      ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName), Caller.Name());
    }
    return Task.CompletedTask;
  }

  public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
    if (Config.Enabled) {
      RemoveBoosterBot(bot);
      Booster booster = new(bot);
      if (Boosters.TryAdd(bot, booster)) {
        return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([booster.StatsManager]);
      }

      booster.Destroy();
      ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Messages.BoosterInitEror, bot.BotName), Caller.Name());
    }
    return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>(null);
  }

  /** IBotCommand */
  public async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) => Config.Enabled ? await CommandsHandler.OnBotCommand(bot, access, message, args, steamID).ConfigureAwait(false) : null;

  /** Internal Method */
  private static void RemoveBoosterBot(Bot bot) {
    if (Boosters.TryRemove(bot, out Booster? booster)) {
      booster.Dispose();
      booster.Destroy();
    }
  }
}

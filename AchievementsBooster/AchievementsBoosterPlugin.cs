using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Composition;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using AchievementsBooster.Handler;
using AchievementsBooster.Helpers;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

[Export(typeof(IPlugin))]
public sealed class AchievementsBoosterPlugin : IASF, IBot, IBotConnection, IBotSteamClient, IBotCommand2, IGitHubPluginUpdates {

  internal static readonly ConcurrentDictionary<Bot, BoostCoordinator> Coordinators = new();

  internal static BoosterGlobalConfig GlobalConfig { get; private set; } = new();

  internal static GlobalCache GlobalCache { get; private set; } = new();

  public string Name => Constants.PluginName;

  public Version Version => Constants.PluginVersion;

  public string RepositoryName => Constants.RepositoryName;

  public Task OnLoaded() {
    ASF.ArchiLogger.LogGenericInfo("** Achievements Booster | Automatically boosting achievements while farming cards **");
    GlobalCache? globalCache = GlobalCache.LoadFromDatabase();
    if (globalCache != null) {
      GlobalCache.Destroy();
      GlobalCache = globalCache;
    }
    GlobalCache.Init();
    return Task.CompletedTask;
  }

  /** IASF */

  public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties) {
    if (additionalConfigProperties != null && additionalConfigProperties.Count > 0) {
      if (additionalConfigProperties.TryGetValue(Constants.AchievementsBoosterConfigKey, out JsonElement configValue)) {
        BoosterGlobalConfig? config = configValue.ToJsonObject<BoosterGlobalConfig>();
        if (config != null) {
          GlobalConfig = config;
          GlobalConfig.Validate();
          if (GlobalConfig.AutoStartBots.IsEmpty) {
            Logger.Shared.Warning(string.Format(CultureInfo.CurrentCulture, Messages.AutoStartBotsEmpty));
          }
        }
      }
    }
    return Task.CompletedTask;
  }

  /** IBot */

  public Task OnBotInit(Bot bot) => Task.CompletedTask;

  public Task OnBotDestroy(Bot bot) {
    ArgumentNullException.ThrowIfNull(bot);
    if (Coordinators.TryRemove(bot, out BoostCoordinator? coordinator)) {
      Logger.Shared.Trace($"Destroy booster for bot: {bot.BotName}");
      _ = coordinator.Stop();
    }
    return Task.CompletedTask;
  }

  /* IBotConnection */

  public Task OnBotDisconnected(Bot bot, EResult reason) {
    ArgumentNullException.ThrowIfNull(bot);
    if (Coordinators.TryGetValue(bot, out BoostCoordinator? coordinator)) {
      _ = coordinator.Stop();
    }
    else {
      Logger.Shared.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }
    return Task.CompletedTask;
  }

  public Task OnBotLoggedOn(Bot bot) {
    ArgumentNullException.ThrowIfNull(bot);
    if (Coordinators.TryGetValue(bot, out BoostCoordinator? coordinator)) {
      if (GlobalConfig.AutoStartBots.Contains(bot.BotName)) {
        _ = coordinator.Start();
      }
    }
    else {
      Logger.Shared.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    }
    return Task.CompletedTask;
  }

  /** IBotSteamClient */

  public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
    ArgumentNullException.ThrowIfNull(bot);
    if (Coordinators.TryGetValue(bot, out BoostCoordinator? coordinator)) {
      return coordinator.OnSteamCallbacksInit(callbackManager);
    }
    Logger.Shared.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName));
    return Task.CompletedTask;
  }

  public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
    ArgumentNullException.ThrowIfNull(bot);
    BoostCoordinator coordinator = new(bot);
    if (Coordinators.TryAdd(bot, coordinator)) {
      return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([coordinator.SteamClientHandler]);
    }

    Logger.Shared.Error(string.Format(CultureInfo.CurrentCulture, Messages.BoosterInitEror, bot.BotName));
    return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>(null);
  }

  /** IBotCommand */
  public Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0)
    => CommandsHandler.OnBotCommand(bot, access, message, args, steamID);
}

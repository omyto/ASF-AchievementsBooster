using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Composition;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using AchievementsBooster.Helper;
using AchievementsBooster.Storage;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

[Export(typeof(IPlugin))]
public sealed class AchievementsBoosterPlugin : IASF, IBot, IBotConnection, IBotSteamClient, IBotCommand2, IGitHubPluginUpdates {

  private static readonly ConcurrentDictionary<Bot, Booster> Boosters = new();

  internal static BoosterConfig GlobalConfig { get; private set; } = new();

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

  internal static IBooster GetBooster(Bot bot, [CallerMemberName] string? callerMethodName = null) {
    ArgumentNullException.ThrowIfNull(bot);
    if (Boosters.TryGetValue(bot, out Booster? booster)) {
      return booster;
    }

    Logger.Shared.Warning(string.Format(CultureInfo.CurrentCulture, Messages.BoosterNotFound, bot.BotName), callerMethodName);
    return DummyBooster.Shared;
  }

  /** IASF */

  public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties) {
    if (additionalConfigProperties != null && additionalConfigProperties.Count > 0) {
      if (additionalConfigProperties.TryGetValue(Constants.AchievementsBoosterConfigKey, out JsonElement configValue)) {
        BoosterConfig? config = configValue.ToJsonObject<BoosterConfig>();
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
    if (Boosters.TryRemove(bot, out Booster? booster)) {
      Logger.Shared.Trace($"Destroy booster for bot: {bot.BotName}");
      _ = booster.Stop();
    }
    return Task.CompletedTask;
  }

  /* IBotConnection */

  public Task OnBotDisconnected(Bot bot, EResult reason) => GetBooster(bot).OnDisconnected(reason);

  public Task OnBotLoggedOn(Bot bot) {
    ArgumentNullException.ThrowIfNull(bot);
    if (GlobalConfig.AutoStartBots.Contains(bot.BotName)) {
      IBooster booster = GetBooster(bot);
      _ = booster.Start();
    }
    return Task.CompletedTask;
  }

  /** IBotSteamClient */

  public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) => GetBooster(bot).OnSteamCallbacksInit(callbackManager);

  public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
    ArgumentNullException.ThrowIfNull(bot);
    Booster booster = new(bot);
    if (Boosters.TryAdd(bot, booster)) {
      return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([booster.SteamClientHandler]);
    }

    Logger.Shared.Error(string.Format(CultureInfo.CurrentCulture, Messages.BoosterInitEror, bot.BotName));
    return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>(null);
  }

  /** IBotCommand */
  public Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0)
    => CommandsCoordinator.OnBotCommand(bot, access, message, args, steamID);
}

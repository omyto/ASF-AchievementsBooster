using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

public interface IBooster {
  public string Start(uint delayInSeconds);

  /** IBotConnection */
  public Task OnDisconnected(EResult reason);

  public Task OnLoggedOn(Bot bot);

  /** IBotModules */
  public Task OnInitModules(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties);

  /** IBotSteamClient */
  public Task OnSteamCallbacksInit(CallbackManager callbackManager);
}

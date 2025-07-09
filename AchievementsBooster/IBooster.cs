using System.Threading.Tasks;
using SteamKit2;

namespace AchievementsBooster;

public interface IBooster {
  public string Start(bool command = false);

  /* IBotConnection */
  public Task OnDisconnected(EResult reason);

  /** IBotSteamClient */
  public Task OnSteamCallbacksInit(CallbackManager callbackManager);
}

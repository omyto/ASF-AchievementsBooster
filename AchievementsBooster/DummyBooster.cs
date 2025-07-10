using System.Threading.Tasks;
using SteamKit2;

namespace AchievementsBooster;

public sealed class DummyBooster : IBooster {
  public static readonly DummyBooster Shared = new();
  private DummyBooster() {
  }

  public string Start(bool command = false) => string.Empty;

  /** ASF Plugin Interfaces */
  public Task OnDisconnected(EResult reason) => Task.CompletedTask;

  public Task OnSteamCallbacksInit(CallbackManager callbackManager) => Task.CompletedTask;

  public Task OnFarmingStarted() => Task.CompletedTask;

  public Task OnFarmingStopped() => Task.CompletedTask;

  public Task OnFarmingFinished(bool farmedSomething) => Task.CompletedTask;
}

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace AchievementsBooster;

public sealed class DummyBooster : IBooster {
  public static readonly DummyBooster Shared = new();
  private DummyBooster() {
  }

  public string Start(uint delayInSeconds) => "Booster not found!";

  /** ASF Plugin Interfaces */
  public Task OnDisconnected(EResult reason) => Task.CompletedTask;
  public Task OnLoggedOn(Bot bot) => Task.CompletedTask;
  public Task OnInitModules(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties) => Task.CompletedTask;
  public Task OnSteamCallbacksInit(CallbackManager callbackManager) => Task.CompletedTask;
}

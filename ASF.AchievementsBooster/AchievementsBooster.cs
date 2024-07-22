using System;
using System.Composition;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;

namespace AchievementsBooster;

[Export(typeof(IPlugin))]
internal sealed class AchievementsBooster : IPlugin {
  public string Name => nameof(AchievementsBooster);
  public Version Version => typeof(AchievementsBooster).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

  public Task OnLoaded() {
    ASF.ArchiLogger.LogGenericInfo($"Hello {Name}!");

    return Task.CompletedTask;
  }
}

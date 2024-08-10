using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using AchievementsBooster.Base;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Config;

public sealed class BoosterGlobalConfig {
  public const byte DefaultBoostingPeriod = 15;
  public const byte DefaultMaxBoostingGames = 1;

  [JsonInclude]
  public bool Enabled { get; internal set; } = true;

  [JsonInclude]
  [Range(5, byte.MaxValue)]
  public byte BoostingPeriod { get; private set; } = DefaultBoostingPeriod;

  [JsonInclude]
  [Range(1, 10)]
  public byte MaxBoostingGames { get; private set; } = DefaultMaxBoostingGames;

  [JsonConstructor]
  internal BoosterGlobalConfig() { }

  internal void Validate() {
    if (BoostingPeriod < 5) {
      ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, nameof(BoostingPeriod), BoostingPeriod, 5), Caller.Name());
      //ASF.ArchiLogger.LogGenericWarning("BoostingPeriod less than 5 is invalid and will be automatically adjusted to 5");
      BoostingPeriod = 5;
    }

    if (MaxBoostingGames < 1) {
      ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, nameof(MaxBoostingGames), MaxBoostingGames, 1), Caller.Name());
      MaxBoostingGames = 1;
    } else if (MaxBoostingGames > 10) {
      ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, nameof(MaxBoostingGames), MaxBoostingGames, 10), Caller.Name());
      MaxBoostingGames = 10;
    }
  }
}

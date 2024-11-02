using System;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using AchievementsBooster.Helpers;

namespace AchievementsBooster.Storage;

public sealed class BoosterGlobalConfig {
  public const byte DefaultMinBoostInterval = 30;
  public const byte DefaultMaxBoostInterval = 60;
  public const byte DefaultSleepingHours = 0;
  public const byte DefaultMaxAppBoostConcurrently = 1;
  public const byte DefaultMaxContinuousBoostHours = 10;
  public const bool DefaultIgnoreAppWithVAC = true;
  public const bool DefaultIgnoreAppWithDLC = true;

  [JsonInclude]
  public ImmutableHashSet<string> AutoStartBots { get; private set; } = [];

  [JsonInclude]
  public ImmutableHashSet<uint> FocusApps { get; private set; } = [];

  [JsonInclude]
  public ImmutableHashSet<uint> IgnoreApps { get; private set; } = [];

  [JsonInclude]
  [Range(5, 250)]
  public short MinBoostInterval { get; private set; } = DefaultMinBoostInterval;

  [JsonInclude]
  [Range(6, 1600)]
  public short MaxBoostInterval { get; private set; } = DefaultMaxBoostInterval;

  [JsonInclude]
  public EBoostingMode BoostingMode { get; private set; } = EBoostingMode.ContinuousBoosting;

  [JsonInclude]
  [Range(0, 12)]
  public byte SleepingHours { get; private set; } = DefaultSleepingHours;

  [JsonInclude]
  [Range(1, Constants.MaxGamesPlayedConcurrently)]
  public byte MaxAppBoostConcurrently { get; private set; } = DefaultMaxAppBoostConcurrently;

  [JsonInclude]
  [Range(byte.MinValue, byte.MaxValue)]
  public byte MaxContinuousBoostHours { get; private set; } = DefaultMaxContinuousBoostHours;

  [JsonInclude]
  public bool IgnoreAppWithVAC { get; private set; } = DefaultIgnoreAppWithVAC;

  [JsonInclude]
  public bool IgnoreAppWithDLC { get; private set; } = DefaultIgnoreAppWithDLC;

  [JsonInclude]
  public ImmutableHashSet<string> IgnoreDevelopers { get; private set; } = [];

  [JsonInclude]
  public ImmutableHashSet<string> IgnorePublishers { get; private set; } = [];

  [JsonConstructor]
  internal BoosterGlobalConfig() { }

  internal void Validate() {
    PropertyInfo[] properties = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
    foreach (PropertyInfo property in properties) {
      RangeAttribute? rangeAttribute = property.GetCustomAttribute<RangeAttribute>();
      if (rangeAttribute != null) {
        int value = (int) (property.GetValue(this) ?? 0);
        int clampedValue = Math.Clamp(value, (int) rangeAttribute.Minimum, (int) rangeAttribute.Maximum);

        if (value != clampedValue) {
          property.SetValue(this, clampedValue);
          AchievementsBooster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, property.Name, value, clampedValue));
        }
      }
    }

    if (!Enum.IsDefined(BoostingMode)) {
      AchievementsBooster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, nameof(BoostingMode), BoostingMode, EBoostingMode.ContinuousBoosting));
      BoostingMode = EBoostingMode.ContinuousBoosting;
    }

    if (MinBoostInterval >= MaxBoostInterval) {
      short newMaxBoostInterval = (short) (MinBoostInterval + 1);
      AchievementsBooster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, nameof(MaxBoostInterval), MaxBoostInterval, newMaxBoostInterval));
      MaxBoostInterval = newMaxBoostInterval;
    }
  }

  public enum EBoostingMode : byte {
    ContinuousBoosting,
    UniqueGamesPerSession,
    SingleDailyAchievementPerGame,
  }
}

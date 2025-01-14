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
  public const byte DefaultBoostDurationPerApp = 10;
  public const byte DefaultBoostRestTimePerApp = 24;
  public const byte DefaultRestTimePerDay = 0;
  public const byte DefaultMaxAppBoostConcurrently = 1;
  public const bool DefaultRestrictAppWithVAC = true;
  public const bool DefaultRestrictAppWithDLC = true;

  [JsonInclude]
  public ImmutableHashSet<string> AutoStartBots { get; private set; } = [];

  [JsonInclude]
  [Range(1, Constants.MaxGamesPlayedConcurrently)]
  public int MaxAppBoostConcurrently { get; private set; } = DefaultMaxAppBoostConcurrently;

  [JsonInclude]
  [Range(1, byte.MaxValue)]
  public int MinBoostInterval { get; private set; } = DefaultMinBoostInterval;

  [JsonInclude]
  [Range(1, byte.MaxValue)]
  public int MaxBoostInterval { get; private set; } = DefaultMaxBoostInterval;

  [JsonInclude]
  [Range(0, short.MaxValue)]
  public int BoostDurationPerApp { get; private set; } = DefaultBoostDurationPerApp;

  [JsonInclude]
  [Range(0, short.MaxValue)]
  public int BoostRestTimePerApp { get; private set; } = DefaultBoostRestTimePerApp;

  [JsonInclude]
  [Range(0, 600)]
  public int RestTimePerDay { get; private set; } = DefaultRestTimePerDay;

  [JsonInclude]
  public bool RestrictAppWithVAC { get; private set; } = DefaultRestrictAppWithVAC;

  [JsonInclude]
  public bool RestrictAppWithDLC { get; private set; } = DefaultRestrictAppWithDLC;

  [JsonInclude]
  public ImmutableHashSet<string> RestrictDevelopers { get; private set; } = [];

  [JsonInclude]
  public ImmutableHashSet<string> RestrictPublishers { get; private set; } = [];

  [JsonInclude]
  public ImmutableHashSet<uint> UnrestrictedApps { get; private set; } = [];

  [JsonInclude]
  public ImmutableHashSet<uint> Blacklist { get; private set; } = [];

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

    if (MinBoostInterval > MaxBoostInterval) {
      AchievementsBooster.Logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, nameof(MaxBoostInterval), MaxBoostInterval, MinBoostInterval));
      MaxBoostInterval = MinBoostInterval;
    }
  }
}

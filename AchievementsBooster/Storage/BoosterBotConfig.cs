using System;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using AchievementsBooster.Helper;

namespace AchievementsBooster.Storage;

public sealed class BoosterBotConfig {

  [JsonInclude]
  public bool AutoStart { get; private set; } = false;

  [JsonInclude]
  [Range(1, 32)] // ArchiHandler.MaxGamesPlayedConcurrently
  public int? MaxConcurrentlyBoostingApps { get; private set; }

  [JsonInclude]
  [Range(1, byte.MaxValue)]
  public int? MinBoostInterval { get; private set; }

  [JsonInclude]
  [Range(1, byte.MaxValue)]
  public int? MaxBoostInterval { get; private set; }

  [JsonInclude]
  [Range(0, 30000)]
  public int? BoostDurationPerApp { get; private set; }

  [JsonInclude]
  [Range(0, 30000)]
  public int? BoostRestTimePerApp { get; private set; }

  [JsonInclude]
  [Range(0, 600)]
  public int? RestTimePerDay { get; private set; }

  [JsonInclude]
  public bool? RestrictAppWithVAC { get; private set; }

  [JsonInclude]
  public bool? RestrictAppWithDLC { get; private set; }

  [JsonInclude]
  public ImmutableList<string>? RestrictDevelopers { get; private set; }

  [JsonInclude]
  public ImmutableList<string>? RestrictPublishers { get; private set; }

  [JsonInclude]
  public ImmutableList<uint>? UnrestrictedApps { get; private set; }

  [JsonInclude]
  public ImmutableList<uint>? Blacklist { get; private set; }

  [JsonInclude]
  public bool? BoostHoursWhenIdle { get; private set; }

  [JsonConstructor]
  internal BoosterBotConfig() { }

  internal void Normalize(Logger logger) {
    PropertyInfo[] properties = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
    foreach (PropertyInfo property in properties) {
      RangeAttribute? rangeAttribute = property.GetCustomAttribute<RangeAttribute>();
      if (rangeAttribute == null) {
        continue;
      }

      object? valueObj = property.GetValue(this);
      if (valueObj == null) {
        continue;
      }

      int value = (int) valueObj;
      int clampedValue = Math.Clamp(value, (int) rangeAttribute.Minimum, (int) rangeAttribute.Maximum);

      if (value != clampedValue) {
        property.SetValue(this, clampedValue);
        logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, property.Name, value, clampedValue));
      }
    }

    if (MinBoostInterval > MaxBoostInterval) {
      logger.Warning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, nameof(MaxBoostInterval), MaxBoostInterval, MinBoostInterval));
      MaxBoostInterval = MinBoostInterval;
    }
  }
}

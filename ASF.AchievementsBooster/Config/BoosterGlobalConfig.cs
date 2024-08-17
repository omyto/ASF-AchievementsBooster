using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using AchievementsBooster.Base;
using ArchiSteamFarm.Core;

namespace AchievementsBooster.Config;

public sealed class BoosterGlobalConfig {
  public const byte DefaultBoostingPeriod = 15;
  public const byte DefaultMaxExpandTimePeriod = 5;
  public const byte DefaultMaxBoostingGames = 1;

  [JsonInclude]
  public bool Enabled { get; internal set; } = true;

  [JsonInclude]
  [Range(5, byte.MaxValue)]
  public byte BoostingPeriod { get; private set; } = DefaultBoostingPeriod;

  [JsonInclude]
  [Range(byte.MinValue, byte.MaxValue)]
  public byte MaxExpandTimePeriod { get; private set; } = DefaultMaxExpandTimePeriod;

  [JsonInclude]
  [Range(1, 10)]
  public byte MaxBoostingGames { get; private set; } = DefaultMaxBoostingGames;

  [JsonConstructor]
  internal BoosterGlobalConfig() { }

  internal void Validate() {
    PropertyInfo[] properties = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
    foreach (PropertyInfo property in properties) {
      if (property.PropertyType == typeof(byte)) {
        ValidateRange(property);
      }
    }
  }

  private void ValidateRange(PropertyInfo property) {
    RangeAttribute? rangeAttribute = property.GetCustomAttribute<RangeAttribute>();
    if (rangeAttribute == null) {
      return;
    }

    byte value = (byte) (property.GetValue(this) ?? 0);
    byte newValue = value;
    if (value < (int) rangeAttribute.Minimum) {
      newValue = Convert.ToByte((int) rangeAttribute.Minimum);
    } else if (value > (int) rangeAttribute.Maximum) {
      newValue = Convert.ToByte((int) rangeAttribute.Maximum);
    }

    if (value != newValue) {
      property.SetValue(this, newValue);
      ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Messages.ConfigPropertyInvalid, property.Name, value, newValue), Caller.Name());
    }
  }
}

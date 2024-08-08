using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SteamKit2;

namespace AchievementsBooster.Extensions;

internal static class KeyValueExtensions {
  [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "<Pending>")]
  internal static double AsDouble(this KeyValue self, double defaultValue = default) {
    if (double.TryParse(self.Value, out double value)) {
      return value;
    }

    if (double.TryParse(self.Value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) {
      return value;
    }

    return defaultValue;
  }
}

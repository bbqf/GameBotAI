using System.Globalization;

namespace GameBot.Emulator.Adb;

/// <summary>
/// Feature 141: finds the LDPlayer instance index of a device serial. LDPlayer gives the ADB port
/// <c>5554 + 2 * index</c> to an instance, so the serial <c>emulator-5558</c> is the instance with index 2.
/// The mapper reads only the serial that the caller gives, so it never points to another emulator.
/// </summary>
public static class LdInstanceMapper {
  private const string Prefix = "emulator-";
  private const int BasePort = 5554;
  private const int MaxIndex = 255;

  /// <summary>The reason that <see cref="TryGetIndex"/> gives when the serial is empty.</summary>
  public const string NoDeviceSerial = "no_device_serial";

  /// <summary>The reason that <see cref="TryGetIndex"/> gives when the serial is not an LDPlayer serial.</summary>
  public const string UnsupportedSerial = "unsupported_serial";

  /// <summary>
  /// Maps <paramref name="serial"/> to an instance index. Returns false and a reason when the serial is
  /// empty or has a form that does not map to an LDPlayer instance (for example a host:port serial).
  /// </summary>
  public static bool TryGetIndex(string? serial, out int index, out string? unavailableReason) {
    index = -1;
    unavailableReason = null;
    if (string.IsNullOrWhiteSpace(serial)) {
      unavailableReason = NoDeviceSerial;
      return false;
    }

    var text = serial.Trim();
    if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
        || !int.TryParse(text.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
        || port < BasePort
        || (port - BasePort) % 2 != 0
        || (port - BasePort) / 2 > MaxIndex) {
      unavailableReason = UnsupportedSerial;
      return false;
    }

    index = (port - BasePort) / 2;
    return true;
  }
}

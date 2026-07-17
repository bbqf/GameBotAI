using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace GameBot.Domain.Actions;

/// <summary>
/// Strongly-typed arguments for the ensure-emulator-running action type (feature 070). The target
/// LDPlayer instance is identified by either <see cref="InstanceName"/> or <see cref="InstanceIndex"/>
/// (at least one required); <see cref="AdbSerial"/> is the device serial used for the responsiveness
/// probe. When both identifiers are supplied, the name takes precedence.
/// </summary>
public sealed class EnsureEmulatorRunningArgs {
  public string? InstanceName { get; init; }
  public int? InstanceIndex { get; init; }
  public required string AdbSerial { get; init; }

  public Dictionary<string, object?> ToArgsDictionary() {
    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
      ["adbSerial"] = AdbSerial
    };
    if (!string.IsNullOrWhiteSpace(InstanceName)) dict["instanceName"] = InstanceName;
    if (InstanceIndex is not null) dict["instanceIndex"] = InstanceIndex;
    return dict;
  }

  public static bool TryFrom(PrimitiveEnsureEmulatorRunningAction action, [NotNullWhen(true)] out EnsureEmulatorRunningArgs? args) {
    args = null;
    if (action is null) return false;
    return TryCreate(action.InstanceName, action.InstanceIndex, action.AdbSerial, out args);
  }

  /// <summary>
  /// Builds args from a sequence action payload's parameter dictionary (keys <c>instanceName</c>,
  /// <c>instanceIndex</c>, <c>adbSerial</c>). Values may be raw CLR types or <see cref="JsonElement"/>.
  /// </summary>
  public static bool TryFrom(IReadOnlyDictionary<string, object?> parameters, [NotNullWhen(true)] out EnsureEmulatorRunningArgs? args) {
    args = null;
    if (parameters is null) return false;
    var name = GetString(parameters, "instanceName");
    var index = GetInt(parameters, "instanceIndex");
    var serial = GetString(parameters, "adbSerial");
    return TryCreate(name, index, serial, out args);
  }

  private static bool TryCreate(string? name, int? index, string? serial, [NotNullWhen(true)] out EnsureEmulatorRunningArgs? args) {
    args = null;
    if (string.IsNullOrWhiteSpace(serial)) return false;
    if (string.IsNullOrWhiteSpace(name) && index is null) return false;
    if (index is < 0) return false;
    args = new EnsureEmulatorRunningArgs {
      InstanceName = string.IsNullOrWhiteSpace(name) ? null : name,
      InstanceIndex = index,
      AdbSerial = serial!
    };
    return true;
  }

  private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) {
    if (!parameters.TryGetValue(key, out var raw) || raw is null) return null;
    if (raw is JsonElement je) return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
    return raw.ToString();
  }

  private static int? GetInt(IReadOnlyDictionary<string, object?> parameters, string key) {
    if (!parameters.TryGetValue(key, out var raw) || raw is null) return null;
    switch (raw) {
      case int i: return i;
      case long l: return (int)l;
      case double d: return (int)d;
      case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n): return n;
      case JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ns): return ns;
      case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed): return parsed;
      default: return null;
    }
  }
}

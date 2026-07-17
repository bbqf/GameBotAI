using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace GameBot.Domain.Actions;

/// <summary>
/// Strongly-typed arguments for the connect-to-game action type.
/// <see cref="InstanceName"/>/<see cref="InstanceIndex"/> are optional (feature 071): when supplied,
/// connect-to-game ensures that LDPlayer instance is running/responsive before attaching the session.
/// </summary>
public sealed class ConnectToGameArgs {
  public required string GameId { get; init; }
  public required string AdbSerial { get; init; }
  public string? InstanceName { get; init; }
  public int? InstanceIndex { get; init; }

  public Dictionary<string, object> ToArgsDictionary() {
    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) {
      ["adbSerial"] = AdbSerial,
      ["gameId"] = GameId
    };
    if (!string.IsNullOrWhiteSpace(InstanceName)) dict["instanceName"] = InstanceName;
    if (InstanceIndex is not null) dict["instanceIndex"] = InstanceIndex;
    return dict;
  }

  /// <summary>True when an emulator instance identifier (name or index) is present.</summary>
  public bool HasInstanceIdentifier => !string.IsNullOrWhiteSpace(InstanceName) || InstanceIndex is not null;

  public static bool TryFrom(PrimitiveConnectToGameAction action, [NotNullWhen(true)] out ConnectToGameArgs? args) {
    args = null;
    if (action is null) return false;
    if (!string.Equals(action.Type, PrimitiveActionTypes.ConnectToGame, StringComparison.OrdinalIgnoreCase)) return false;
    if (string.IsNullOrWhiteSpace(action.GameId) || string.IsNullOrWhiteSpace(action.AdbSerial)) return false;

    args = new ConnectToGameArgs {
      GameId = action.GameId,
      AdbSerial = action.AdbSerial,
      InstanceName = string.IsNullOrWhiteSpace(action.InstanceName) ? null : action.InstanceName,
      InstanceIndex = action.InstanceIndex
    };
    return true;
  }

  public static bool TryFrom(InputAction action, string? actionGameId, [NotNullWhen(true)] out ConnectToGameArgs? args) {
    args = null;
    if (action is null) return false;
    if (!string.Equals(action.Type, ActionTypes.ConnectToGame, StringComparison.OrdinalIgnoreCase)) return false;

    var gameId = action.Args.TryGetValue("gameId", out var g) ? g?.ToString() : null;
    if (string.IsNullOrWhiteSpace(gameId)) gameId = actionGameId;
    var serial = action.Args.TryGetValue("adbSerial", out var s) ? s?.ToString() : null;
    if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(serial)) return false;

    var instanceName = GetString(action.Args, "instanceName");
    args = new ConnectToGameArgs {
      GameId = gameId!,
      AdbSerial = serial!,
      InstanceName = string.IsNullOrWhiteSpace(instanceName) ? null : instanceName,
      InstanceIndex = GetInt(action.Args, "instanceIndex")
    };
    return true;
  }

  private static string? GetString(Dictionary<string, object> args, string key) {
    if (!args.TryGetValue(key, out var raw) || raw is null) return null;
    if (raw is JsonElement je) return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
    return raw.ToString();
  }

  private static int? GetInt(Dictionary<string, object> args, string key) {
    if (!args.TryGetValue(key, out var raw) || raw is null) return null;
    switch (raw) {
      case int i: return i;
      case long l: return (int)l;
      case double d: return (int)d;
      case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n): return n;
      case JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ns): return ns;
      case string str when int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed): return parsed;
      default: return null;
    }
  }
}

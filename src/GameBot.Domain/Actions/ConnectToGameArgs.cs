using System.Diagnostics.CodeAnalysis;

namespace GameBot.Domain.Actions;

/// <summary>
/// Strongly-typed arguments for the connect-to-game action type.
/// </summary>
public sealed class ConnectToGameArgs {
  public required string GameId { get; init; }
  public required string AdbSerial { get; init; }

  public Dictionary<string, object> ToArgsDictionary() => new(StringComparer.OrdinalIgnoreCase) {
    ["adbSerial"] = AdbSerial,
    ["gameId"] = GameId
  };

  public static bool TryFrom(InputAction action, string? actionGameId, [NotNullWhen(true)] out ConnectToGameArgs? args) {
    args = null;
    if (action is null) return false;
    if (!string.Equals(action.Type, ActionTypes.ConnectToGame, StringComparison.OrdinalIgnoreCase)) return false;

    var gameId = action.Args.TryGetValue("gameId", out var g) ? g?.ToString() : null;
    if (string.IsNullOrWhiteSpace(gameId)) gameId = actionGameId;
    var serial = action.Args.TryGetValue("adbSerial", out var s) ? s?.ToString() : null;
    if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(serial)) return false;

    args = new ConnectToGameArgs { GameId = gameId!, AdbSerial = serial! };
    return true;
  }
}
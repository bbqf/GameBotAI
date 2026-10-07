using System.Text.Json;

namespace GameBot.Domain.Commands.EnsureGameRunning {
  /// <summary>
  /// Reads the payload of an <c>ensure-game-running</c> action step (feature 129). The only option is
  /// <c>forceRestart</c>. It must be a JSON boolean. The key is optional, and absent means false.
  /// <para>
  /// A saved boolean is a CLR <c>bool</c> before a file write and a <see cref="JsonElement"/> after a
  /// file read. The reader accepts both. Save, load, and run all use this one reader.
  /// </para>
  /// </summary>
  public static class EnsureGameRunningPayload {
    /// <summary>The key of the option in <see cref="SequenceActionPayload.Parameters"/>.</summary>
    public const string ForceRestartKey = "forceRestart";

    /// <summary>The error text for a value that is not true or false.</summary>
    public const string ForceRestartError = "ensure-game-running forceRestart must be true or false";

    /// <summary>
    /// Reads the <c>forceRestart</c> option.
    /// </summary>
    /// <param name="action">The action payload of the step.</param>
    /// <param name="forceRestart">The value. False when the key is absent or the read fails.</param>
    /// <param name="error">A message that names <c>forceRestart</c> on failure. Null on success.</param>
    /// <returns>True when the key is absent or holds a boolean.</returns>
    public static bool TryRead(SequenceActionPayload? action, out bool forceRestart, out string? error) {
      forceRestart = false;
      error = null;
      if (action is null || !action.Parameters.TryGetValue(ForceRestartKey, out var raw)) return true;

      switch (raw) {
        case bool b:
          forceRestart = b;
          return true;
        case JsonElement { ValueKind: JsonValueKind.True }:
          forceRestart = true;
          return true;
        case JsonElement { ValueKind: JsonValueKind.False }:
          forceRestart = false;
          return true;
        default:
          error = ForceRestartError;
          return false;
      }
    }
  }
}

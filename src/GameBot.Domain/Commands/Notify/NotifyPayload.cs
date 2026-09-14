using System;
using System.Globalization;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Commands.Notify {
  /// <summary>
  /// The payload of a <c>notify</c> action step (feature 087, issue #181): an author-written
  /// message and an optional per-step destination.
  /// <para>
  /// Read from <see cref="SequenceActionPayload.Parameters"/> the same way
  /// <c>SelfReschedulePayload</c> reads its own, so authoring, persistence and execution all agree
  /// on one shape.
  /// </para>
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1056:URI-like properties should not be strings",
    Justification = "Url is read verbatim from an authored sequence's stored JSON parameters and is "
                  + "validated at save time. System.Uri would normalize it, so the value would not "
                  + "read back as the author wrote it, and a malformed stored value would throw "
                  + "during parsing instead of producing the actionable save-time message.")]
  public sealed class NotifyPayload {
    /// <summary>Longest accepted message. Long enough for real context, short enough for an alert.</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>The author's message. Never blank — that is a save-time error.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Per-step destination overriding the service default; null when unset.</summary>
    public string? Url { get; private set; }

    /// <summary>
    /// Reads and validates a notify payload.
    /// </summary>
    /// <param name="action">The step's action payload.</param>
    /// <param name="payload">The parsed payload on success; null otherwise.</param>
    /// <param name="error">An actionable message naming the problem; null on success.</param>
    /// <returns>True when the payload is well-formed.</returns>
    public static bool TryRead(SequenceActionPayload? action, out NotifyPayload? payload, out string? error) {
      payload = null;
      error = null;
      if (action is null) {
        error = "action payload is missing";
        return false;
      }

      var message = ReadString(action, "message");
      if (string.IsNullOrWhiteSpace(message)) {
        error = "requires a non-empty 'message'";
        return false;
      }

      if (message!.Length > MaxMessageLength) {
        error = string.Format(
          CultureInfo.InvariantCulture,
          "'message' must be at most {0} characters (was: {1})",
          MaxMessageLength,
          message.Length);
        return false;
      }

      var url = ReadString(action, "url");
      if (!string.IsNullOrWhiteSpace(url) && !IsAbsoluteHttpUrl(url!)) {
        error = $"'url' must be an absolute http or https URL (was: '{url}')";
        return false;
      }

      payload = new NotifyPayload {
        Message = message,
        Url = string.IsNullOrWhiteSpace(url) ? null : url!.Trim()
      };
      return true;
    }

    /// <summary>True for an absolute http/https URL; anything else is not a usable destination.</summary>
    public static bool IsAbsoluteHttpUrl(string value) =>
      Uri.TryCreate(value, UriKind.Absolute, out var uri)
      && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? ReadString(SequenceActionPayload action, string key) {
      if (!action.Parameters.TryGetValue(key, out var raw) || raw is null) return null;
      return raw as string ?? raw.ToString();
    }
  }
}

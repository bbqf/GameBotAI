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

    /// <summary>Longest accepted dedupe key, after trim.</summary>
    public const int MaxDedupeKeyLength = 100;

    /// <summary>The trimmed dedupe key; null when the step has no dedupe fields.</summary>
    public string? DedupeKey { get; private set; }

    /// <summary>Start of a daily window as a local time of day; null when unset.</summary>
    public TimeOnly? Since { get; private set; }

    /// <summary>Length of a sliding window; null when unset.</summary>
    public TimeSpan? Within { get; private set; }

    /// <summary>True when the step has a dedupe key and a window.</summary>
    public bool HasDedupe => DedupeKey is not null;

    /// <summary>The scope identity for a run that is not in a queue.</summary>
    public const string AdhocScope = "adhoc";

    /// <summary>
    /// The start of the dedupe window at <paramref name="now"/>. A <c>since</c> window uses the same
    /// math as the <c>lastRun</c> condition. A <c>within</c> window ends now.
    /// </summary>
    public DateTimeOffset WindowStart(DateTimeOffset now, TimeZoneInfo zone) {
      if (Since is { } since) return GameBot.Domain.Services.LastRunWindow.SinceStart(now, since, zone);
      return now - (Within ?? TimeSpan.Zero);
    }

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

      if (!TryReadDedupe(action, out var dedupeKey, out var since, out var within, out error)) {
        return false;
      }

      payload = new NotifyPayload {
        Message = message,
        Url = string.IsNullOrWhiteSpace(url) ? null : url!.Trim(),
        DedupeKey = dedupeKey,
        Since = since,
        Within = within
      };
      return true;
    }

    // Reads the optional dedupe fields (feature 137). All three fields work together: a key needs
    // exactly one window, and a window needs a key.
    private static bool TryReadDedupe(
        SequenceActionPayload action,
        out string? key,
        out TimeOnly? since,
        out TimeSpan? within,
        out string? error) {
      key = null;
      since = null;
      within = null;
      error = null;

      var hasKey = action.Parameters.TryGetValue("dedupeKey", out var rawKey) && rawKey is not null;
      var hasSince = action.Parameters.TryGetValue("since", out var rawSince) && rawSince is not null;
      var hasWithin = action.Parameters.TryGetValue("within", out var rawWithin) && rawWithin is not null;
      if (!hasKey && !hasSince && !hasWithin) return true;

      if (hasSince && hasWithin) {
        error = "use either 'since' or 'within', not both";
        return false;
      }

      if (hasKey) {
        var text = (rawKey as string ?? rawKey!.ToString())!.Trim();
        if (text.Length == 0) {
          error = "'dedupeKey' must not be blank";
          return false;
        }

        if (text.Length > MaxDedupeKeyLength) {
          error = string.Format(
            CultureInfo.InvariantCulture,
            "'dedupeKey' must be at most {0} characters (was: {1})",
            MaxDedupeKeyLength,
            text.Length);
          return false;
        }

        key = text;
      }

      if (hasKey && !hasSince && !hasWithin) {
        error = "'dedupeKey' requires 'since' or 'within'";
        return false;
      }

      if (!hasKey) {
        error = "'since' and 'within' require 'dedupeKey'";
        return false;
      }

      if (hasSince) {
        var text = (rawSince as string ?? rawSince!.ToString())!.Trim();
        if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
          error = $"'since' must be a time of day in HH:mm format (was: '{text}')";
          return false;
        }

        since = parsed;
      }
      else {
        var text = (rawWithin as string ?? rawWithin!.ToString())!.Trim();
        if (!TryParseDuration(text, out var parsed)) {
          error = $"'within' must be a duration in HH:mm:ss format (was: '{text}')";
          return false;
        }

        if (parsed <= TimeSpan.Zero || parsed > TimeSpan.FromHours(24)) {
          error = "'within' must be from 00:00:01 to 24:00:00";
          return false;
        }

        within = parsed;
      }

      return true;
    }

    // HH:mm:ss with two digits for each part. The format "24:00:00" is valid, so TimeSpan parsing is not used.
    private static bool TryParseDuration(string text, out TimeSpan value) {
      value = TimeSpan.Zero;
      var parts = text.Split(':');
      if (parts.Length != 3) return false;
      foreach (var part in parts) {
        if (part.Length != 2 || !char.IsAsciiDigit(part[0]) || !char.IsAsciiDigit(part[1])) return false;
      }

      var h = int.Parse(parts[0], CultureInfo.InvariantCulture);
      var m = int.Parse(parts[1], CultureInfo.InvariantCulture);
      var s = int.Parse(parts[2], CultureInfo.InvariantCulture);
      if (m > 59 || s > 59) return false;
      value = new TimeSpan(h, m, s);
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

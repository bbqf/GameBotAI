using System;
using System.Globalization;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Parses and validates the relative-offset wire format ("HH:mm:ss") used by relative-timer
/// template entries (feature 059) and the live-schedule endpoint. The offset MUST be non-negative
/// and no greater than <see cref="MaxOffset"/> (24 hours), which bounds it to realistic run
/// durations while still allowing the literal "in 10 min" use case.
/// </summary>
internal static class RelativeOffsetParser {
  /// <summary>Inclusive upper bound for a relative offset.</summary>
  public static readonly TimeSpan MaxOffset = TimeSpan.FromHours(24);

  /// <summary>
  /// Attempts to parse <paramref name="raw"/> as an "HH:mm:ss" duration. On success returns the
  /// parsed <paramref name="offset"/> (<c>&gt;= 0</c> and <c>&lt;= 24:00:00</c>); on failure
  /// returns a human-readable <paramref name="error"/> suitable for an API 400 message.
  /// </summary>
  public static bool TryParse(string? raw, out TimeSpan offset, out string? error) {
    offset = TimeSpan.Zero;
    error = null;

    var trimmed = raw?.Trim();
    if (string.IsNullOrEmpty(trimmed)) {
      error = "offset is required and must be an HH:mm:ss duration (e.g. '00:10:00')";
      return false;
    }

    if (trimmed.StartsWith('-')) {
      error = $"offset '{raw}' must be non-negative";
      return false;
    }

    var parts = trimmed.Split(':');
    if (parts.Length != 3
        || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
        || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
        || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) {
      error = $"offset '{raw}' is not a valid HH:mm:ss duration (e.g. '00:10:00')";
      return false;
    }

    if (minutes > 59 || seconds > 59) {
      error = $"offset '{raw}' is out of range; minutes and seconds must be between 00 and 59";
      return false;
    }

    var candidate = new TimeSpan(hours, minutes, seconds);
    if (candidate > MaxOffset) {
      error = $"offset '{raw}' exceeds the maximum of 24:00:00";
      return false;
    }

    offset = candidate;
    return true;
  }

  /// <summary>Formats a <see cref="TimeSpan"/> offset back to the "HH:mm:ss" wire form.</summary>
  public static string Format(TimeSpan offset) =>
    ((int)offset.TotalHours).ToString("D2", CultureInfo.InvariantCulture)
    + offset.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
}

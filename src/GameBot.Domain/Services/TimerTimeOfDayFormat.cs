using System.Globalization;

namespace GameBot.Domain.Services;

/// <summary>
/// The one rule for the <c>timerTimeOfDay</c> text. The queue-template endpoint and the sequence
/// validator both call this class, so they accept and reject the same strings.
/// </summary>
public static class TimerTimeOfDayFormat {
  /// <summary>The accepted format, for use in error messages.</summary>
  public const string AcceptedFormatText = "HH:mm or HH:mm:ss (24-hour)";

  private static readonly string[] Formats = { "HH:mm", "HH:mm:ss" };

  /// <summary>
  /// Reads a time of day. Only <c>HH:mm</c> and <c>HH:mm:ss</c> (24-hour, two digits for the hour)
  /// are valid. Spaces, AM/PM, and all other forms are not valid.
  /// </summary>
  /// <param name="value">The text to read. Can be null.</param>
  /// <param name="result">The time of day when the text is valid.</param>
  /// <returns>True when the text is valid.</returns>
  public static bool TryParse(string? value, out TimeOnly result) {
    result = default;
    if (value is null) {
      return false;
    }
    return TimeOnly.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
  }

  /// <summary>
  /// Writes a time of day. The result is <c>HH:mm</c> when the seconds are zero. Otherwise it is
  /// <c>HH:mm:ss</c>. Parts of a second are not written.
  /// </summary>
  /// <param name="value">The time of day to write.</param>
  /// <returns>The text form.</returns>
  public static string Format(TimeOnly value) {
    return value.ToString(value.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture);
  }
}

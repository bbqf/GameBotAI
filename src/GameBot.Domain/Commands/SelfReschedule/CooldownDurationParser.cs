using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GameBot.Domain.Commands.SelfReschedule;

/// <summary>
/// Pure helper (feature 068) that extracts a countdown duration from noisy OCR text. Supports
/// <c>hh:mm:ss</c> and <c>mm:ss</c> forms, tolerates surrounding non-duration characters, and
/// normalizes a few safe OCR digit confusions (e.g. <c>O</c>→<c>0</c>). Returns <c>false</c> when
/// no duration token is present or a numeric component overflows.
/// </summary>
public static class CooldownDurationParser {
  // First token of the form  d+:dd(:dd)?  anywhere in the text. Hours are unbounded so an absurd
  // read is caught by the overflow guard below (rather than silently truncated); minutes/seconds
  // are 1-2 digits. Two colon groups => h:m:s, one => m:s.
  private static readonly Regex DurationToken = new(
    @"(\d+)\s*:\s*(\d{1,2})(?:\s*:\s*(\d{1,2}))?",
    RegexOptions.CultureInvariant | RegexOptions.Compiled,
    TimeSpan.FromMilliseconds(200));

  /// <summary>
  /// Attempts to parse the first countdown duration found in <paramref name="ocrText"/>.
  /// </summary>
  public static bool TryParse(string? ocrText, out TimeSpan value) {
    value = default;
    if (string.IsNullOrWhiteSpace(ocrText)) {
      return false;
    }

    var normalized = NormalizeDigits(ocrText);
    var match = DurationToken.Match(normalized);
    if (!match.Success) {
      return false;
    }

    var hasHours = match.Groups[3].Success;
    // h:m:s when three groups matched; otherwise the two groups are m:s.
    var firstText = match.Groups[1].Value;
    var secondText = match.Groups[2].Value;
    var thirdText = hasHours ? match.Groups[3].Value : null;

    long hours, minutes, seconds;
    if (hasHours) {
      if (!TryParseComponent(firstText, out hours)
          || !TryParseComponent(secondText, out minutes)
          || !TryParseComponent(thirdText!, out seconds)) {
        return false;
      }
    }
    else {
      hours = 0;
      if (!TryParseComponent(firstText, out minutes)
          || !TryParseComponent(secondText, out seconds)) {
        return false;
      }
    }

    try {
      // Use a checked total-seconds computation so an implausible read fails rather than wrapping.
      var totalSeconds = checked((hours * 3600L) + (minutes * 60L) + seconds);
      value = TimeSpan.FromSeconds(totalSeconds);
      return true;
    }
    catch (OverflowException) {
      return false;
    }
  }

  private static bool TryParseComponent(string text, out long value) =>
    long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

  // Replaces the common OCR letter/digit confusions that appear inside timer glyphs. Only affects
  // the extracted numeric token because the regex still requires digits + colons around them.
  private static string NormalizeDigits(string text) {
    Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
    for (var i = 0; i < text.Length; i++) {
      buffer[i] = text[i] switch {
        'O' or 'o' => '0',
        'l' or 'I' or '|' => '1',
        _ => text[i]
      };
    }
    return new string(buffer);
  }
}

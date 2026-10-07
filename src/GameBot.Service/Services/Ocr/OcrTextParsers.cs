using System;
using System.Collections.Generic;
using System.Globalization;
using GameBot.Domain.Commands.SelfReschedule;

namespace GameBot.Service.Services.Ocr;

/// <summary>
/// Registry of the parser names that <c>POST /api/ocr/read</c> accepts. Names are compared without case.
/// </summary>
internal static class OcrTextParsers {
  /// <summary>Name of the duration parser. It uses <see cref="CooldownDurationParser"/>.</summary>
  internal const string HhMmSs = "hh:mm:ss";

  /// <summary>The supported parser names.</summary>
  internal static IReadOnlyList<string> Names { get; } = new[] { HhMmSs };

  /// <summary>Returns true when the name is in the registry. The canonical name is given in <paramref name="canonical"/>.</summary>
  internal static bool TryGetName(string? name, out string canonical) {
    foreach (var known in Names) {
      if (string.Equals(known, name?.Trim(), StringComparison.OrdinalIgnoreCase)) {
        canonical = known;
        return true;
      }
    }
    canonical = string.Empty;
    return false;
  }

  /// <summary>Parses the text with the named parser. The name must be a canonical name from the registry.</summary>
  internal static bool TryParse(string canonicalName, string? text, out string value, out double totalSeconds) {
    value = string.Empty;
    totalSeconds = 0;
    if (!string.Equals(canonicalName, HhMmSs, StringComparison.Ordinal)
        || !CooldownDurationParser.TryParse(text, out var span)) {
      return false;
    }
    totalSeconds = span.TotalSeconds;
    var hours = (long)Math.Floor(span.TotalHours);
    value = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, span.Minutes, span.Seconds);
    return true;
  }
}

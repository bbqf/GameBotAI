using System.Text;

namespace GameBot.Domain.Quiz;

/// <summary>
/// Makes quiz text comparable (feature 142, research R3): Unicode form KC, lower case (invariant), each run
/// of characters that are not a letter or a digit becomes one space, and the result is trimmed.
/// </summary>
public static class QuizTextNormalizer {
  /// <summary>Returns the normalized text. Null or white-space text gives an empty string.</summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Globalization", "CA1308:Normalize strings to uppercase",
    Justification = "The stored quiz text is lower case, as the data model says. The text is compared, never parsed.")]
  public static string Normalize(string? text) {
    if (string.IsNullOrWhiteSpace(text)) {
      return string.Empty;
    }

    string composed;
    try {
      composed = text.Normalize(NormalizationForm.FormKC);
    }
    catch (ArgumentException) {
      // Text with an invalid code point: keep it as it is. The rules below still apply.
      composed = text;
    }

    var lower = composed.ToLowerInvariant();
    var builder = new StringBuilder(lower.Length);
    var pendingSpace = false;
    foreach (var rune in lower.EnumerateRunes()) {
      if (Rune.IsLetterOrDigit(rune)) {
        if (pendingSpace && builder.Length > 0) {
          builder.Append(' ');
        }

        pendingSpace = false;
        builder.Append(rune.ToString());
      }
      else {
        pendingSpace = true;
      }
    }

    return builder.ToString();
  }
}

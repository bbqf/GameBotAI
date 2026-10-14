using System.Globalization;

namespace GameBot.Domain.Quiz;

/// <summary>The outcome names of the quiz steps (feature 142).</summary>
public static class QuizOutcomes {
  /// <summary>Lookup: a region has no readable text, or the OCR engine failed. No pick is made.</summary>
  public const string Unreadable = "unreadable";

  /// <summary>Lookup and learn: the quiz store cannot be used.</summary>
  public const string ServiceUnavailable = "service_unavailable";

  /// <summary>Learn: the correct answer is stored.</summary>
  public const string Stored = "stored";

  /// <summary>Learn: no lookup result, or no answer that matches the correct text.</summary>
  public const string SkippedNoMatch = "skipped_no_match";

  /// <summary>Learn: more than one answer matches the correct text.</summary>
  public const string SkippedAmbiguous = "skipped_ambiguous";

  private const string PickPrefix = "pick_";

  /// <summary>The lookup outcome that names the answer position to tap, from 1.</summary>
  public static string Pick(int position) => PickPrefix + position.ToString(CultureInfo.InvariantCulture);

  /// <summary>Reads the position of a <c>pick_N</c> outcome. False for any other text.</summary>
  public static bool TryParsePick(string? outcome, out int position) {
    position = 0;
    if (outcome is null || !outcome.StartsWith(PickPrefix, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var digits = outcome.AsSpan(PickPrefix.Length);
    if (digits.IsEmpty) {
      return false;
    }

    foreach (var c in digits) {
      if (c is < '0' or > '9') {
        return false;
      }
    }

    return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out position) && position >= 1;
  }

  /// <summary>True when <paramref name="outcome"/> is an outcome name of a quiz step.</summary>
  public static bool IsQuizOutcome(string? outcome) {
    if (string.IsNullOrWhiteSpace(outcome)) {
      return false;
    }

    return TryParsePick(outcome, out _)
        || string.Equals(outcome, Unreadable, StringComparison.OrdinalIgnoreCase)
        || string.Equals(outcome, ServiceUnavailable, StringComparison.OrdinalIgnoreCase)
        || string.Equals(outcome, Stored, StringComparison.OrdinalIgnoreCase)
        || string.Equals(outcome, SkippedNoMatch, StringComparison.OrdinalIgnoreCase)
        || string.Equals(outcome, SkippedAmbiguous, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>True for an outcome where the step did its work: <c>pick_N</c> or <c>stored</c>.</summary>
  public static bool IsSuccessful(string? outcome) =>
    TryParsePick(outcome, out _) || string.Equals(outcome, Stored, StringComparison.OrdinalIgnoreCase);
}

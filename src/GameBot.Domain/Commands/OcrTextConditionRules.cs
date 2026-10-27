using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GameBot.Domain.Utils;

namespace GameBot.Domain.Commands;

/// <summary>
/// The field rules and the match rules of an <see cref="OcrTextStepCondition"/>. Save-time validation and
/// run-time evaluation use the same rules, so a condition that saves is a condition that evaluates.
/// </summary>
public static class OcrTextConditionRules {
  /// <summary>The time limit of one <c>pattern</c> match.</summary>
  public static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

  /// <summary>The message when <c>region</c> is absent.</summary>
  public const string RegionRequiredMessage = "ocrText requires region (x, y, width and height).";

  /// <summary>The message when both or neither of <c>expected</c> and <c>pattern</c> are set.</summary>
  public const string ExpectedOrPatternMessage = "ocrText requires exactly one of expected and pattern.";

  /// <summary>
  /// Returns the message tail of each problem of <paramref name="condition"/>, or an empty list when the
  /// condition is correct. The caller adds its own prefix (step label and path). The parser name is not
  /// checked here: only the sequence endpoint checks it.
  /// </summary>
  public static IReadOnlyList<string> Validate(OcrTextStepCondition condition) {
    ArgumentNullException.ThrowIfNull(condition);
    var errors = new List<string>();

    if (condition.Region is null) {
      errors.Add(RegionRequiredMessage);
    }
    else {
      errors.AddRange(condition.Region.Validate("ocrText region"));
    }

    if ((condition.Expected is null) == (condition.Pattern is null)) {
      errors.Add(ExpectedOrPatternMessage);
    }

    // Feature 165: a pattern with a placeholder is not compiled here. The run checks the resolved
    // pattern, and the parameter validation checks the pattern with the sequence defaults.
    if (condition.Pattern is not null
        && !TemplateSubstitutor.ContainsPlaceholder(condition.Pattern)
        && !TryValidatePattern(condition.Pattern, out var problem)) {
      errors.Add($"ocrText pattern is not a valid regular expression: {problem}");
    }

    return errors;
  }

  /// <summary>
  /// Compiles <paramref name="pattern"/> with the same options as <see cref="Matches"/> (feature 165).
  /// </summary>
  /// <param name="pattern">The pattern to compile.</param>
  /// <param name="problem">The reason when the pattern is not valid; otherwise <c>null</c>.</param>
  /// <returns><c>true</c> when the pattern is a valid regular expression; otherwise <c>false</c>.</returns>
  public static bool TryValidatePattern(string pattern, out string? problem) {
    ArgumentNullException.ThrowIfNull(pattern);
    try {
      _ = new Regex(pattern, RegexOptions.CultureInvariant, PatternTimeout);
      problem = null;
      return true;
    }
    catch (ArgumentException ex) {
      problem = ex.Message;
      return false;
    }
  }

  /// <summary>
  /// Compares the text with the condition. <c>expected</c>: ordinal compare of the trimmed text. An empty
  /// text is compared like any text. <c>pattern</c>: a match anywhere in the trimmed text, with a time
  /// limit. A time-out gives false and sets <paramref name="timedOut"/>.
  /// </summary>
  public static bool Matches(OcrTextStepCondition condition, string? text, out bool timedOut) {
    ArgumentNullException.ThrowIfNull(condition);
    timedOut = false;
    var trimmed = (text ?? string.Empty).Trim();

    if (condition.Expected is not null) {
      return string.Equals(trimmed, condition.Expected, StringComparison.Ordinal);
    }

    if (condition.Pattern is null) {
      return false;
    }

    try {
      return Regex.IsMatch(trimmed, condition.Pattern, RegexOptions.CultureInvariant, PatternTimeout);
    }
    catch (RegexMatchTimeoutException) {
      timedOut = true;
      return false;
    }
    catch (ArgumentException) {
      // An invalid pattern that bypassed save-time validation is a plain mismatch.
      return false;
    }
  }
}

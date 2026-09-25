using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using GameBot.Domain.Queues;

namespace GameBot.Domain.Commands;

/// <summary>
/// The field rules and the parsers of a <see cref="LastRunStepCondition"/> (feature 105). Save-time
/// validation and run-time evaluation use the same parsers, so a value that saves is a value that
/// evaluates.
/// </summary>
public static class LastRunConditionRules {
  /// <summary>The message when <c>sequence</c> is absent or blank.</summary>
  public const string SequenceRequiredMessage = "lastRun condition requires sequence ('self' or a sequence id).";

  /// <summary>The message when <c>status</c> is absent or not a known value.</summary>
  public const string StatusInvalidMessage = "lastRun status must be one of success|failure|cancelled.";

  /// <summary>The message when both <c>since</c> and <c>within</c> are set.</summary>
  public const string BothWindowsMessage = "lastRun condition accepts only one of since or within, not both.";

  /// <summary>The message when neither <c>since</c> nor <c>within</c> is set.</summary>
  public const string NoWindowMessage = "lastRun condition requires one of since or within.";

  /// <summary>The message when <c>since</c> is not a time of day in <c>HH:mm</c> format.</summary>
  public const string SinceInvalidMessage = "lastRun since must be a time of day in HH:mm format (00:00 to 23:59).";

  /// <summary>The message when <c>within</c> is not a permitted duration.</summary>
  public const string WithinInvalidMessage = "lastRun within must be a duration more than zero and not more than 366 days, in hh:mm:ss or d.hh:mm:ss format.";

  /// <summary>The pattern of <c>since</c>: two-digit hours 00 to 23 and two-digit minutes.</summary>
  public const string SincePattern = @"^([01]\d|2[0-3]):[0-5]\d$";

  /// <summary>The pattern of <c>within</c>: <c>[d.]h:mm:ss</c>. The hours part can be 24 or more.</summary>
  public const string WithinPattern = @"^(?:\d{1,3}\.)?\d{1,4}:[0-5]\d:[0-5]\d$";

  /// <summary>The largest permitted value of <c>within</c>.</summary>
  public static readonly TimeSpan MaxWithin = TimeSpan.FromDays(366);

  // ECMAScript: \d matches only the ASCII digits 0 to 9, the same as in the published pattern.
  private static readonly Regex SinceRegex = new(SincePattern, RegexOptions.ECMAScript | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

  // The same pattern as WithinPattern, with a group for each part.
  private static readonly Regex WithinRegex = new(@"^(?:(\d{1,3})\.)?(\d{1,4}):([0-5]\d):([0-5]\d)$", RegexOptions.ECMAScript | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

  /// <summary>
  /// Returns the message tail of each problem of <paramref name="condition"/>, or an empty list when
  /// the condition is correct. The caller adds its own prefix (step label and path).
  /// </summary>
  public static IReadOnlyList<string> Validate(LastRunStepCondition condition) {
    ArgumentNullException.ThrowIfNull(condition);
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(condition.Sequence)) {
      errors.Add(SequenceRequiredMessage);
    }

    if (!TryParseStatus(condition.Status, out _)) {
      errors.Add(StatusInvalidMessage);
    }

    if (condition.Since is not null && condition.Within is not null) {
      errors.Add(BothWindowsMessage);
    }
    else if (condition.Since is not null) {
      if (!TryParseSince(condition.Since, out _)) errors.Add(SinceInvalidMessage);
    }
    else if (condition.Within is not null) {
      if (!TryParseWithin(condition.Within, out _)) errors.Add(WithinInvalidMessage);
    }
    else {
      errors.Add(NoWindowMessage);
    }

    return errors;
  }

  /// <summary>Reads <c>success</c>, <c>failure</c> or <c>cancelled</c>, not case-sensitive.</summary>
  public static bool TryParseStatus(string? value, out SequenceRunStatus status) {
    status = default;
    if (string.Equals(value, "success", StringComparison.OrdinalIgnoreCase)) {
      status = SequenceRunStatus.Success;
      return true;
    }

    if (string.Equals(value, "failure", StringComparison.OrdinalIgnoreCase)) {
      status = SequenceRunStatus.Failure;
      return true;
    }

    if (string.Equals(value, "cancelled", StringComparison.OrdinalIgnoreCase)) {
      status = SequenceRunStatus.Cancelled;
      return true;
    }

    return false;
  }

  /// <summary>Reads a time of day in strict <c>HH:mm</c> format (00:00 to 23:59).</summary>
  public static bool TryParseSince(string? value, out TimeOnly since) {
    since = default;
    if (value is null || !SinceRegex.IsMatch(value)) return false;
    return TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out since);
  }

  /// <summary>
  /// Reads a duration in <c>hh:mm:ss</c> or <c>d.hh:mm:ss</c> format. The hours part can be 24 or
  /// more, so <c>24:00:00</c> is 24 hours (not 24 days as with <see cref="TimeSpan.Parse(string)"/>).
  /// The value must be more than zero and not more than <see cref="MaxWithin"/>.
  /// </summary>
  public static bool TryParseWithin(string? value, out TimeSpan within) {
    within = default;
    if (value is null) return false;
    var match = WithinRegex.Match(value);
    if (!match.Success) return false;

    var days = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    var hours = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
    var minutes = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
    var seconds = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
    var parsed = TimeSpan.FromDays(days) + TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
    if (parsed <= TimeSpan.Zero || parsed > MaxWithin) return false;

    within = parsed;
    return true;
  }
}

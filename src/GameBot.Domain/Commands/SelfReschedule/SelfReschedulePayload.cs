using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace GameBot.Domain.Commands.SelfReschedule;

/// <summary>
/// Thin typed reader over a <see cref="SequenceActionPayload"/> of type
/// <c>reschedule-self</c> (feature 065). The dictionary remains the storage; this view parses
/// <c>option</c>, <c>timerTimeOfDay</c>, and <c>timerRelativeOffset</c>. Cross-field validation
/// (mutual exclusivity, ranges) lives in <c>ActionPayloadValidationService</c>; this reader only
/// surfaces parse-level errors (unknown option, malformed timer string).
/// </summary>
public sealed class SelfReschedulePayload {
  /// <summary>Wire key for the chosen schedule option.</summary>
  public const string OptionKey = "option";
  /// <summary>Wire key for the Timer time-of-day value (HH:mm:ss).</summary>
  public const string TimerTimeOfDayKey = "timerTimeOfDay";
  /// <summary>Wire key for the Timer relative-offset value (HH:mm:ss).</summary>
  public const string TimerRelativeOffsetKey = "timerRelativeOffset";

  /// <summary>The chosen schedule option.</summary>
  public required SelfRescheduleOption Option { get; init; }

  /// <summary>Resolved Timer time-of-day, when present.</summary>
  public TimeOnly? TimerTimeOfDay { get; init; }

  /// <summary>Resolved Timer relative offset, when present.</summary>
  public TimeSpan? TimerRelativeOffset { get; init; }

  /// <summary>True when at least one timer field was supplied (regardless of option).</summary>
  public bool HasTimerTimeOfDay { get; init; }

  /// <summary>True when a relative offset was supplied (regardless of option).</summary>
  public bool HasTimerRelativeOffset { get; init; }

  /// <summary>
  /// Attempts to read a <c>reschedule-self</c> payload. Returns <c>false</c> with a human-readable
  /// <paramref name="error"/> when the option is missing/unknown or a timer field is malformed.
  /// </summary>
  public static bool TryRead(SequenceActionPayload payload, out SelfReschedulePayload? result, out string? error) {
    ArgumentNullException.ThrowIfNull(payload);
    result = null;
    error = null;

    var rawOption = ReadString(payload.Parameters, OptionKey);
    if (string.IsNullOrWhiteSpace(rawOption)) {
      error = "option is required";
      return false;
    }
    if (!Enum.TryParse<SelfRescheduleOption>(rawOption, ignoreCase: true, out var option)) {
      error = $"option '{rawOption}' is not a known schedule option (expected one of AtQueueStart, OncePerRun, Timer, EveryStep)";
      return false;
    }

    TimeOnly? timeOfDay = null;
    var hasTimeOfDay = TryReadString(payload.Parameters, TimerTimeOfDayKey, out var rawTimeOfDay);
    if (hasTimeOfDay) {
      if (!TimeOnly.TryParse(rawTimeOfDay, CultureInfo.InvariantCulture, out var parsedTimeOfDay)) {
        error = $"timerTimeOfDay '{rawTimeOfDay}' is not a valid HH:mm:ss time-of-day";
        return false;
      }
      timeOfDay = parsedTimeOfDay;
    }

    TimeSpan? relativeOffset = null;
    var hasRelative = TryReadString(payload.Parameters, TimerRelativeOffsetKey, out var rawRelative);
    if (hasRelative) {
      if (!TimeSpan.TryParse(rawRelative, CultureInfo.InvariantCulture, out var parsedRelative)) {
        error = $"timerRelativeOffset '{rawRelative}' is not a valid HH:mm:ss duration";
        return false;
      }
      relativeOffset = parsedRelative;
    }

    result = new SelfReschedulePayload {
      Option = option,
      TimerTimeOfDay = timeOfDay,
      TimerRelativeOffset = relativeOffset,
      HasTimerTimeOfDay = hasTimeOfDay,
      HasTimerRelativeOffset = hasRelative
    };
    return true;
  }

  private static string? ReadString(IReadOnlyDictionary<string, object?> parameters, string key) {
    TryReadString(parameters, key, out var value);
    return value;
  }

  private static bool TryReadString(IReadOnlyDictionary<string, object?> parameters, string key, out string? value) {
    value = null;
    if (!TryGetIgnoreCase(parameters, key, out var raw) || raw is null) {
      return false;
    }

    switch (raw) {
      case string s:
        value = s;
        return !string.IsNullOrWhiteSpace(s);
      case JsonElement je when je.ValueKind == JsonValueKind.String:
        value = je.GetString();
        return !string.IsNullOrWhiteSpace(value);
      case JsonElement je when je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined:
        return false;
      default:
        value = raw.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }
  }

  private static bool TryGetIgnoreCase(IReadOnlyDictionary<string, object?> parameters, string key, out object? value) {
    if (parameters.TryGetValue(key, out value)) {
      return true;
    }
    foreach (var pair in parameters) {
      if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
        value = pair.Value;
        return true;
      }
    }
    value = null;
    return false;
  }
}

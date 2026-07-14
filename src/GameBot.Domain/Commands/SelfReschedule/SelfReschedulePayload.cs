using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace GameBot.Domain.Commands.SelfReschedule;

/// <summary>
/// Thin typed reader over a <see cref="SequenceActionPayload"/> of type
/// <c>reschedule-self</c> (feature 065). The dictionary remains the storage; this view parses
/// <c>option</c>, <c>timerTimeOfDay</c>, <c>timerRelativeOffset</c>, and the optional
/// <c>ocrOffset</c> spec (feature 068). Cross-field validation (mutual exclusivity, ranges) lives in
/// <c>SequenceStepValidationService.ValidateRescheduleSelfPayload</c>; this reader only surfaces
/// parse-level errors (unknown option, malformed timer/region/duration).
/// </summary>
public sealed class SelfReschedulePayload {
  /// <summary>Wire key for the chosen schedule option.</summary>
  public const string OptionKey = "option";
  /// <summary>Wire key for the Timer time-of-day value (HH:mm:ss).</summary>
  public const string TimerTimeOfDayKey = "timerTimeOfDay";
  /// <summary>Wire key for the Timer relative-offset value (HH:mm:ss).</summary>
  public const string TimerRelativeOffsetKey = "timerRelativeOffset";
  /// <summary>Wire key for the optional OCR-offset spec (feature 068).</summary>
  public const string OcrOffsetKey = "ocrOffset";

  /// <summary>Default lower bound for a parsed OCR duration when <c>min</c> is omitted.</summary>
  public static readonly TimeSpan DefaultOcrMin = TimeSpan.FromSeconds(1);
  /// <summary>Default upper bound for a parsed OCR duration when <c>max</c> is omitted.</summary>
  public static readonly TimeSpan DefaultOcrMax = TimeSpan.FromHours(24);

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

  /// <summary>The OCR-offset spec (feature 068), when configured; otherwise null.</summary>
  public SelfRescheduleOcrOffset? OcrOffset { get; init; }

  /// <summary>True when an <c>ocrOffset</c> spec was supplied.</summary>
  public bool HasOcrOffset => OcrOffset is not null;

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

    SelfRescheduleOcrOffset? ocrOffset = null;
    if (TryGetIgnoreCase(payload.Parameters, OcrOffsetKey, out var rawOcrOffset) && rawOcrOffset is not null
        && !IsJsonNull(rawOcrOffset)) {
      if (!TryReadOcrOffset(rawOcrOffset, out ocrOffset, out error)) {
        return false;
      }
    }

    result = new SelfReschedulePayload {
      Option = option,
      TimerTimeOfDay = timeOfDay,
      TimerRelativeOffset = relativeOffset,
      HasTimerTimeOfDay = hasTimeOfDay,
      HasTimerRelativeOffset = hasRelative,
      OcrOffset = ocrOffset
    };
    return true;
  }

  private static bool IsJsonNull(object value) =>
    value is JsonElement je && je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

  // Parses the nested ocrOffset object. Structural problems (not an object, missing/malformed
  // region, missing/malformed fallback/min/max) are parse-level errors surfaced here; cross-field
  // business rules (option must be Timer, positive region, min < max) live in the validation service.
  private static bool TryReadOcrOffset(object raw, out SelfRescheduleOcrOffset? result, out string? error) {
    result = null;
    error = null;

    if (!TryReadNested(raw, out var reader)) {
      error = "ocrOffset must be an object";
      return false;
    }

    if (!reader.TryGetChild("region", out var regionReader)) {
      error = "ocrOffset.region is required";
      return false;
    }

    if (!regionReader.TryGetInt("x", out var x)
        || !regionReader.TryGetInt("y", out var y)
        || !regionReader.TryGetInt("width", out var width)
        || !regionReader.TryGetInt("height", out var height)) {
      error = "ocrOffset.region requires numeric x, y, width and height";
      return false;
    }

    if (!reader.TryGetString("fallback", out var fallbackRaw) || string.IsNullOrWhiteSpace(fallbackRaw)) {
      error = "ocrOffset.fallback is required";
      return false;
    }
    if (!TimeSpan.TryParse(fallbackRaw, CultureInfo.InvariantCulture, out var fallback)) {
      error = $"ocrOffset.fallback '{fallbackRaw}' is not a valid HH:mm:ss duration";
      return false;
    }

    var min = DefaultOcrMin;
    if (reader.TryGetString("min", out var minRaw) && !string.IsNullOrWhiteSpace(minRaw)) {
      if (!TimeSpan.TryParse(minRaw, CultureInfo.InvariantCulture, out min)) {
        error = $"ocrOffset.min '{minRaw}' is not a valid HH:mm:ss duration";
        return false;
      }
    }

    var max = DefaultOcrMax;
    if (reader.TryGetString("max", out var maxRaw) && !string.IsNullOrWhiteSpace(maxRaw)) {
      if (!TimeSpan.TryParse(maxRaw, CultureInfo.InvariantCulture, out max)) {
        error = $"ocrOffset.max '{maxRaw}' is not a valid HH:mm:ss duration";
        return false;
      }
    }

    result = new SelfRescheduleOcrOffset {
      Region = new OcrOffsetRegion(x, y, width, height),
      Fallback = fallback,
      Min = min,
      Max = max
    };
    return true;
  }

  // ocrOffset arrives either as a JsonElement (persisted / API) or a plain dictionary (in-process
  // authoring). NestedReader normalizes both so parsing is written once.
  private readonly struct NestedReader {
    private readonly JsonElement? _json;
    private readonly IReadOnlyDictionary<string, object?>? _dict;

    private NestedReader(JsonElement? json, IReadOnlyDictionary<string, object?>? dict) {
      _json = json;
      _dict = dict;
    }

    public static bool TryCreate(object raw, out NestedReader reader) {
      switch (raw) {
        case JsonElement je when je.ValueKind == JsonValueKind.Object:
          reader = new NestedReader(je, null);
          return true;
        case IReadOnlyDictionary<string, object?> rod:
          reader = new NestedReader(null, rod);
          return true;
        case IDictionary<string, object?> d:
          reader = new NestedReader(null, new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase));
          return true;
        default:
          reader = default;
          return false;
      }
    }

    public bool TryGetChild(string key, out NestedReader child) {
      if (TryGetRaw(key, out var value) && value is not null && TryReadNested(value, out child)) {
        return true;
      }
      child = default;
      return false;
    }

    public bool TryGetString(string key, out string? value) {
      value = null;
      if (!TryGetRaw(key, out var raw) || raw is null) {
        return false;
      }
      switch (raw) {
        case string s:
          value = s;
          return true;
        case JsonElement je when je.ValueKind == JsonValueKind.String:
          value = je.GetString();
          return true;
        case JsonElement je when je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined:
          return false;
        default:
          value = raw.ToString();
          return value is not null;
      }
    }

    public bool TryGetInt(string key, out int value) {
      value = 0;
      if (!TryGetRaw(key, out var raw) || raw is null) {
        return false;
      }
      switch (raw) {
        case int i:
          value = i;
          return true;
        case long l when l is >= int.MinValue and <= int.MaxValue:
          value = (int)l;
          return true;
        case double dbl when dbl is >= int.MinValue and <= int.MaxValue:
          value = (int)dbl;
          return true;
        case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var ji):
          value = ji;
          return true;
        case JsonElement je when je.ValueKind == JsonValueKind.String:
          return int.TryParse(je.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        case string s:
          return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        default:
          return false;
      }
    }

    private bool TryGetRaw(string key, out object? value) {
      if (_dict is not null) {
        if (_dict.TryGetValue(key, out value)) {
          return true;
        }
        foreach (var pair in _dict) {
          if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
            value = pair.Value;
            return true;
          }
        }
        value = null;
        return false;
      }

      if (_json is { } je) {
        foreach (var prop in je.EnumerateObject()) {
          if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)) {
            value = prop.Value;
            return true;
          }
        }
      }
      value = null;
      return false;
    }
  }

  private static bool TryReadNested(object raw, out NestedReader reader) => NestedReader.TryCreate(raw, out reader);

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

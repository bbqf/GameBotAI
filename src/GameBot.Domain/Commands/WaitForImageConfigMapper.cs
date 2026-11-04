using System;
using System.Collections.Generic;
using System.Text.Json;
using GameBot.Domain.Actions;

namespace GameBot.Domain.Commands;

/// <summary>The four optional whole numbers of a region as read from a payload (feature 177).</summary>
public sealed record RegionFields(int? X, int? Y, int? Width, int? Height);

/// <summary>
/// Feature 177: the one reader of the WaitForImage payload fields (<c>timeoutMs</c>, <c>until</c>,
/// <c>absentCaptures</c>, and <c>detectionTarget</c> with its <c>region</c>). The save path and the gamepack
/// install path both call this class, so a step has the same configuration on both paths. No other code
/// keeps its own copy of these rules.
/// </summary>
public static class WaitForImageConfigMapper {
  private const int DefaultTimeoutMs = 1000;
  private const double DefaultConfidence = 0.8;

  /// <summary>
  /// Builds the configuration of a WaitForImage step from its action payload. Returns null when the action
  /// is not a WaitForImage action or when there is no payload. This method does not throw and does not
  /// check that the image exists.
  /// </summary>
  /// <param name="actionType">The action type.</param>
  /// <param name="payload">The action payload.</param>
  public static WaitForImageConfig? Map(string? actionType, IReadOnlyDictionary<string, object?>? payload) {
    if (payload is null || !string.Equals(actionType, ActionTypes.WaitForImage, StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    var timeoutMs = DefaultTimeoutMs;
    if (payload.TryGetValue("timeoutMs", out var timeoutValue) && TryReadInt32(timeoutValue, out var parsedTimeout)) {
      timeoutMs = parsedTimeout;
    }

    // The save check has already rejected an invalid until or absentCaptures (feature 175). A value that
    // is not readable (for example a {{name}} reference) leaves the field null.
    int? absentCaptures = null;
    if (payload.TryGetValue("absentCaptures", out var absentValue) && TryReadInt32(absentValue, out var parsedAbsent)) {
      absentCaptures = parsedAbsent;
    }

    return new WaitForImageConfig {
      TimeoutMs = timeoutMs,
      DetectionTarget = MapDetectionTarget(payload),
      Until = ReadUntil(payload),
      AbsentCaptures = absentCaptures
    };
  }

  /// <summary>Reads a whole number from an int, a long, a JSON number, or a numeric string.</summary>
  /// <param name="value">The value.</param>
  /// <param name="result">The number, or 0 when the value is not a whole number.</param>
  public static bool TryReadInt32(object? value, out int result) {
    switch (value) {
      case int intValue:
        result = intValue;
        return true;
      case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
        result = (int)longValue;
        return true;
      case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed):
        result = parsed;
        return true;
      case string text when int.TryParse(text, out var parsedText):
        result = parsedText;
        return true;
      default:
        result = 0;
        return false;
    }
  }

  /// <summary>
  /// Reads the optional <c>region</c> of a raw <c>detectionTarget</c> object. Returns false and sets
  /// <paramref name="errors"/> when a field is not a whole number. A missing field stays null, so the caller
  /// can name it with <see cref="PixelRegion.Validate(int?, int?, int?, int?, string)"/>. No region gives
  /// true and a null result.
  /// </summary>
  /// <param name="detectionTarget">The raw detection target.</param>
  /// <param name="region">The four numbers, or null when there is no region.</param>
  /// <param name="errors">The error texts.</param>
  /// <param name="prefix">Text put before each field name in an error.</param>
  public static bool TryReadRegion(JsonElement detectionTarget, out RegionFields? region, out IReadOnlyList<string> errors, string prefix = "region") {
    region = null;
    errors = Array.Empty<string>();
    if (detectionTarget.ValueKind != JsonValueKind.Object
        || !detectionTarget.TryGetProperty("region", out var regionElement)
        || regionElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) {
      return true;
    }

    if (regionElement.ValueKind != JsonValueKind.Object) {
      errors = new[] { $"{prefix} must be an object" };
      return false;
    }

    var problems = new List<string>();
    region = new RegionFields(
      ReadRegionField(regionElement, "x", prefix, problems),
      ReadRegionField(regionElement, "y", prefix, problems),
      ReadRegionField(regionElement, "width", prefix, problems),
      ReadRegionField(regionElement, "height", prefix, problems));
    if (problems.Count > 0) {
      errors = problems;
      return false;
    }

    return true;
  }

  private static int? ReadRegionField(JsonElement regionElement, string name, string prefix, List<string> problems) {
    if (!regionElement.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
    if (field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var value)) return value;
    problems.Add($"{prefix}.{name} must be a whole number");
    return null;
  }

  private static string? ReadUntil(IReadOnlyDictionary<string, object?> payload) {
    if (!payload.TryGetValue("until", out var untilValue)) return null;
    return untilValue is JsonElement { ValueKind: JsonValueKind.String } untilElement
      ? untilElement.GetString()
      : untilValue as string;
  }

  private static DetectionTarget? MapDetectionTarget(IReadOnlyDictionary<string, object?> payload) {
    if (!payload.TryGetValue("detectionTarget", out var value)
        || value is not JsonElement target
        || target.ValueKind != JsonValueKind.Object) {
      return null;
    }

    if (!target.TryGetProperty("referenceImageId", out var idElement) || idElement.ValueKind != JsonValueKind.String) {
      return null;
    }

    var referenceImageId = idElement.GetString();
    if (string.IsNullOrWhiteSpace(referenceImageId)) {
      return null;
    }

    var confidence = target.TryGetProperty("confidence", out var confidenceElement)
                     && confidenceElement.ValueKind == JsonValueKind.Number
                     && confidenceElement.TryGetDouble(out var parsedConfidence)
      ? parsedConfidence
      : DefaultConfidence;
    return new DetectionTarget(referenceImageId, confidence, ReadOffset(target, "offsetX"), ReadOffset(target, "offsetY"), ReadStrategy(target), ReadRegion(target));
  }

  private static int ReadOffset(JsonElement target, string name) =>
    target.TryGetProperty(name, out var element)
    && element.ValueKind == JsonValueKind.Number
    && element.TryGetInt32(out var parsed)
      ? parsed
      : 0;

  private static DetectionSelectionStrategy ReadStrategy(JsonElement target) =>
    target.TryGetProperty("selectionStrategy", out var element)
    && element.ValueKind == JsonValueKind.String
    && string.Equals(element.GetString(), nameof(DetectionSelectionStrategy.FirstMatch), StringComparison.OrdinalIgnoreCase)
      ? DetectionSelectionStrategy.FirstMatch
      : DetectionSelectionStrategy.HighestConfidence;

  // The save check rejects an invalid region before this method runs. The install path adds no check, so
  // a region that is not valid gives no region and no failure.
  private static PixelRegion? ReadRegion(JsonElement target) {
    if (!TryReadRegion(target, out var fields, out _) || fields is not { X: { } x, Y: { } y, Width: { } width, Height: { } height }) {
      return null;
    }

    return PixelRegion.Validate(x, y, width, height).Count == 0 ? new PixelRegion(x, y, width, height) : null;
  }
}

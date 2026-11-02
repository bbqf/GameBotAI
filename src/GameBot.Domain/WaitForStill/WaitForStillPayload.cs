using System.Text.Json;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Quiz;

namespace GameBot.Domain.WaitForStill;

/// <summary>
/// The typed view of a <c>WaitForStill</c> action (feature 174). The dictionary stays the storage. This reader
/// gives the values, and <see cref="Validate"/> gives the save-time errors.
/// </summary>
public sealed class WaitForStillPayload {
  /// <summary>The wire key of the watched region.</summary>
  public const string RegionKey = "region";

  /// <summary>The wire key of the stable time in milliseconds.</summary>
  public const string StableMsKey = "stableMs";

  /// <summary>The wire key of the largest difference that still counts as the same.</summary>
  public const string MaxDiffKey = "maxDiff";

  /// <summary>The wire key of the time limit in milliseconds.</summary>
  public const string TimeoutMsKey = "timeoutMs";

  /// <summary>The stable time when the step has none.</summary>
  public const int DefaultStableMs = 300;

  /// <summary>The largest difference when the step has none.</summary>
  public const double DefaultMaxDiff = 0.01;

  /// <summary>The time limit when the step has none.</summary>
  public const int DefaultTimeoutMs = 5000;

  private static readonly string[] KnownKeys = { RegionKey, StableMsKey, MaxDiffKey, TimeoutMsKey };

  /// <summary>The watched region, or null for the whole screen.</summary>
  public PixelRegion? Region { get; private set; }

  /// <summary>The time in milliseconds that the region must stay the same.</summary>
  public int StableMs { get; private set; } = DefaultStableMs;

  /// <summary>The largest mean difference, from 0 to 1, that counts as the same.</summary>
  public double MaxDiff { get; private set; } = DefaultMaxDiff;

  /// <summary>The time limit in milliseconds.</summary>
  public int TimeoutMs { get; private set; } = DefaultTimeoutMs;

  /// <summary>Reads the payload. False with the first error when the payload is not valid.</summary>
  public static bool TryRead(SequenceActionPayload? action, out WaitForStillPayload? payload, out string? error) {
    var errors = Parse(action, false, out payload);
    error = errors.Count > 0 ? errors[0] : null;
    if (errors.Count > 0) {
      payload = null;
      return false;
    }

    return true;
  }

  /// <summary>
  /// Returns all save-time errors of the payload. The list is empty when the payload is valid. A value that is a
  /// <c>{{name}}</c> parameter reference is not checked.
  /// </summary>
  public static IReadOnlyList<string> Validate(SequenceActionPayload? action) => Parse(action, true, out _);

  private static List<string> Parse(SequenceActionPayload? action, bool allowReferences, out WaitForStillPayload? payload) {
    var errors = new List<string>();
    payload = null;
    if (action is null) {
      errors.Add("action payload is missing");
      return errors;
    }

    foreach (var key in action.Parameters.Keys) {
      if (!KnownKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) {
        errors.Add($"payload field '{key}' is not supported");
      }
    }

    var stableMs = ReadInt(action, StableMsKey, DefaultStableMs, allowReferences, out var stableKnown, errors,
      "stableMs must be an integer of 1 or more");
    var timeoutMs = ReadInt(action, TimeoutMsKey, DefaultTimeoutMs, allowReferences, out var timeoutKnown, errors,
      "timeoutMs must be an integer of 1 or more");
    var maxDiff = ReadMaxDiff(action, allowReferences, errors);
    var region = ReadRegion(action, allowReferences, errors);

    if (stableKnown && timeoutKnown && errors.Count == 0 && stableMs > timeoutMs) {
      errors.Add("stableMs must not be greater than timeoutMs, so that the step can end with still");
    }

    if (errors.Count == 0) {
      payload = new WaitForStillPayload {
        Region = region,
        StableMs = stableMs,
        MaxDiff = maxDiff,
        TimeoutMs = timeoutMs
      };
    }

    return errors;
  }

  // known is false for a skipped parameter reference or an invalid value. An absent value is known (the default).
  private static int ReadInt(SequenceActionPayload action, string key, int fallback, bool allowReferences,
      out bool known, List<string> errors, string message) {
    known = true;
    if (!QuizPayloadReader.TryGet(action, key, out var element)) {
      return fallback;
    }

    if (allowReferences && IsReference(element)) {
      known = false;
      return fallback;
    }

    if (!QuizPayloadReader.TryAsInt(element, out var value) || value < 1) {
      errors.Add(message);
      known = false;
      return fallback;
    }

    return value;
  }

  private static double ReadMaxDiff(SequenceActionPayload action, bool allowReferences, List<string> errors) {
    if (!QuizPayloadReader.TryGet(action, MaxDiffKey, out var element)) {
      return DefaultMaxDiff;
    }

    if (allowReferences && IsReference(element)) {
      return DefaultMaxDiff;
    }

    if (!QuizPayloadReader.TryAsDouble(element, out var value)
        || double.IsNaN(value) || value < 0 || value > 1) {
      errors.Add("maxDiff must be a number from 0 to 1");
      return DefaultMaxDiff;
    }

    return value;
  }

  private static PixelRegion? ReadRegion(SequenceActionPayload action, bool allowReferences, List<string> errors) {
    if (!QuizPayloadReader.TryGet(action, RegionKey, out var element)) {
      return null;
    }

    if (allowReferences && IsReference(element)) {
      return null;
    }

    if (element.ValueKind != JsonValueKind.Object) {
      errors.Add("region must be an object with x, y, width and height");
      return null;
    }

    var skipped = false;
    int? x = ReadRegionField(element, "x", allowReferences, ref skipped, errors);
    int? y = ReadRegionField(element, "y", allowReferences, ref skipped, errors);
    int? width = ReadRegionField(element, "width", allowReferences, ref skipped, errors);
    int? height = ReadRegionField(element, "height", allowReferences, ref skipped, errors);
    if (skipped) {
      return null;
    }

    var before = errors.Count;
    foreach (var problem in PixelRegion.Validate(x, y, width, height, RegionKey)) {
      // A field with a wrong type has its own message already.
      var typeMessage = problem.EndsWith(" is required", StringComparison.Ordinal)
        ? $"{problem[..^" is required".Length]} must be a whole number"
        : null;
      if (typeMessage is null || !errors.Contains(typeMessage)) {
        errors.Add(problem);
      }
    }

    if (errors.Count > before || x is null || y is null || width is null || height is null) {
      return null;
    }

    return new PixelRegion(x.Value, y.Value, width.Value, height.Value);
  }

  private static int? ReadRegionField(JsonElement region, string name, bool allowReferences, ref bool skipped, List<string> errors) {
    foreach (var property in region.EnumerateObject()) {
      if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (allowReferences && IsReference(property.Value)) {
        skipped = true;
        return null;
      }

      if (QuizPayloadReader.TryAsInt(property.Value, out var value)) {
        return value;
      }

      errors.Add($"{RegionKey}.{name} must be a whole number");
      return null;
    }

    return null;
  }

  private static bool IsReference(JsonElement element) =>
    element.ValueKind == JsonValueKind.String && ParameterNameRules.IsWholePlaceholder(element.GetString()?.Trim());
}

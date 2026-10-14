using System.Globalization;
using System.Text.Json;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;

namespace GameBot.Domain.Quiz;

/// <summary>
/// Reads the typed values of a quiz step payload (feature 142). A value can be a <see cref="JsonElement"/>
/// (stored or API input) or a plain .NET value (in-process authoring). Every value is turned into a JSON element first,
/// so the code is written once.
/// </summary>
internal static class QuizPayloadReader {
  public static bool TryGet(SequenceActionPayload action, string key, out JsonElement value) {
    value = default;
    foreach (var pair in action.Parameters) {
      if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) || pair.Value is null) {
        continue;
      }

      var element = pair.Value is JsonElement je ? je : ToElement(pair.Value);
      if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) {
        return false;
      }

      value = element;
      return true;
    }

    return false;
  }

  // A value that JSON cannot hold (NaN, infinity, a type with no converter) becomes its text. The typed
  // readers then reject it with a normal message, so a bad value never throws.
  private static JsonElement ToElement(object value) {
    try {
      return JsonSerializer.SerializeToElement(value);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) {
      return JsonSerializer.SerializeToElement(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }
  }

  /// <summary>Reads a string. Returns null when the key is missing or the value is not a string.</summary>
  public static string? ReadString(SequenceActionPayload action, string key) {
    if (!TryGet(action, key, out var element)) {
      return null;
    }

    return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
  }

  /// <summary>Reads a region object <c>{ x, y, width, height }</c>. False when it is not an object of four numbers.</summary>
  public static bool TryReadRegion(JsonElement element, out OcrOffsetRegion region) {
    region = default;
    if (element.ValueKind != JsonValueKind.Object
        || !TryReadInt(element, "x", out var x)
        || !TryReadInt(element, "y", out var y)
        || !TryReadInt(element, "width", out var width)
        || !TryReadInt(element, "height", out var height)) {
      return false;
    }

    region = new OcrOffsetRegion(x, y, width, height);
    return true;
  }

  /// <summary>Reads an integer. A whole JSON number or a numeric string is accepted.</summary>
  public static bool TryReadInt(JsonElement parent, string key, out int value) {
    value = 0;
    foreach (var property in parent.EnumerateObject()) {
      if (!string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      return TryAsInt(property.Value, out value);
    }

    return false;
  }

  public static bool TryAsInt(JsonElement element, out int value) {
    value = 0;
    switch (element.ValueKind) {
      case JsonValueKind.Number:
        if (element.TryGetInt32(out value)) {
          return true;
        }

        if (element.TryGetDouble(out var d) && Math.Abs(d - Math.Floor(d)) < double.Epsilon && d is >= int.MinValue and <= int.MaxValue) {
          value = (int)d;
          return true;
        }

        return false;
      case JsonValueKind.String:
        return int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
      default:
        return false;
    }
  }

  /// <summary>Reads a number. A JSON number or a numeric string is accepted.</summary>
  public static bool TryAsDouble(JsonElement element, out double value) {
    value = 0;
    switch (element.ValueKind) {
      case JsonValueKind.Number:
        return element.TryGetDouble(out value);
      case JsonValueKind.String:
        return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
      default:
        return false;
    }
  }
}

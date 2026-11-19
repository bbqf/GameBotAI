using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Commands;

/// <summary>
/// The fixed value of a <see cref="ParameterStepCondition"/> (feature 193). The record keeps the JSON kind
/// that the author wrote, so a read gives a JSON number for a JSON number and a JSON string for a JSON string.
/// </summary>
/// <param name="Text">The text of the value. For a JSON number, the raw number text (for example <c>0</c> or <c>-5</c>).</param>
/// <param name="IsNumber">True when the author wrote a JSON number.</param>
[JsonConverter(typeof(ParameterConditionValueJsonConverter))]
public sealed record ParameterConditionValue(string Text, bool IsNumber) {
  /// <summary>Makes a value from a JSON element. Gives <c>null</c> for a JSON kind that is not a string or a number.</summary>
  /// <param name="element">The JSON element.</param>
  public static ParameterConditionValue? FromJsonElement(JsonElement element) => element.ValueKind switch {
    JsonValueKind.String => new ParameterConditionValue(element.GetString() ?? string.Empty, false),
    JsonValueKind.Number => new ParameterConditionValue(element.GetRawText(), true),
    _ => null
  };
}

/// <summary>
/// Reads a JSON string or a JSON number into a <see cref="ParameterConditionValue"/> and writes the same
/// JSON kind (feature 193). All other JSON kinds give a <see cref="JsonException"/>.
/// </summary>
public sealed class ParameterConditionValueJsonConverter : JsonConverter<ParameterConditionValue> {
  /// <inheritdoc />
  public override ParameterConditionValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
    switch (reader.TokenType) {
      case JsonTokenType.String:
        return new ParameterConditionValue(reader.GetString() ?? string.Empty, false);
      case JsonTokenType.Number:
        using (var document = JsonDocument.ParseValue(ref reader)) {
          return new ParameterConditionValue(document.RootElement.GetRawText(), true);
        }
      default:
        throw new JsonException("parameter value must be a string or a number.");
    }
  }

  /// <inheritdoc />
  public override void Write(Utf8JsonWriter writer, ParameterConditionValue value, JsonSerializerOptions options) {
    ArgumentNullException.ThrowIfNull(writer);
    ArgumentNullException.ThrowIfNull(value);
    if (value.IsNumber) {
      writer.WriteRawValue(value.Text, skipInputValidation: false);
    }
    else {
      writer.WriteStringValue(value.Text);
    }
  }
}

/// <summary>
/// The operators, the shape rules, and the compare rules of a <see cref="ParameterStepCondition"/>
/// (feature 193). The save checks and the run use the same rules.
/// </summary>
public static class ParameterConditionRules {
  /// <summary>True when the run value is equal to the value.</summary>
  public const string EqualsOperator = "equals";

  /// <summary>True when the run value is not equal to the value.</summary>
  public const string NotEqualsOperator = "notEquals";

  /// <summary>True when the run value is greater than the value.</summary>
  public const string GreaterThanOperator = "greaterThan";

  /// <summary>True when the run value is less than the value.</summary>
  public const string LessThanOperator = "lessThan";

  /// <summary>The four operator names. Case-sensitive.</summary>
  public static readonly IReadOnlyList<string> Operators =
      [EqualsOperator, NotEqualsOperator, GreaterThanOperator, LessThanOperator];

  /// <summary>The operator names as one text, for the messages.</summary>
  public const string OperatorsText = "equals, notEquals, greaterThan, lessThan";

  /// <summary>The message when <c>name</c> is empty.</summary>
  public const string NameRequiredMessage = "parameter condition requires name.";

  /// <summary>The message when <c>operator</c> is not one of the four operators.</summary>
  public const string OperatorInvalidMessage = "parameter operator must be one of " + OperatorsText + ".";

  /// <summary>The message when <c>value</c> is not present.</summary>
  public const string ValueRequiredMessage = "parameter condition requires value.";

  /// <summary>
  /// Gives the message tail of each shape problem of <paramref name="condition"/>, or an empty list when the
  /// shape is correct. The caller adds its own prefix (step label and path). This check needs no declarations.
  /// </summary>
  /// <param name="condition">The condition to check.</param>
  public static IReadOnlyList<string> ValidateShape(ParameterStepCondition condition) {
    ArgumentNullException.ThrowIfNull(condition);
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(condition.Name)) errors.Add(NameRequiredMessage);
    if (!IsOperator(condition.Operator)) errors.Add(OperatorInvalidMessage);
    if (condition.Value is null) errors.Add(ValueRequiredMessage);
    return errors;
  }

  /// <summary>True when <paramref name="op"/> is one of the four operators (case-sensitive).</summary>
  /// <param name="op">The operator name.</param>
  public static bool IsOperator(string? op) {
    if (op is null) return false;
    foreach (var known in Operators) {
      if (string.Equals(known, op, StringComparison.Ordinal)) return true;
    }

    return false;
  }

  /// <summary>
  /// Parses a whole number. The method trims the text, and permits a leading sign. It uses the invariant culture.
  /// </summary>
  /// <param name="text">The text to parse.</param>
  /// <param name="number">The number, when the parse succeeds.</param>
  /// <returns><c>true</c> when the text is a whole number; otherwise <c>false</c>.</returns>
  public static bool TryParseWhole(string? text, out long number) {
    if (text is null) {
      number = 0;
      return false;
    }

    return long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
  }

  /// <summary>
  /// Compares the run value with the fixed value, and gives the result before <c>negate</c>.
  /// A number compare parses both texts as whole numbers. A text compare is ordinal and case-insensitive,
  /// with no trim.
  /// </summary>
  /// <param name="runValue">The run value of the parameter.</param>
  /// <param name="compareAsNumber">True when the declaration has the type <c>number</c>.</param>
  /// <param name="value">The fixed value of the condition.</param>
  /// <param name="op">One of the four operators.</param>
  /// <exception cref="FormatException">A number compare, and a text is not a whole number.</exception>
  /// <exception cref="ArgumentException">The operator is not one of the four operators.</exception>
  public static bool Compare(string runValue, bool compareAsNumber, ParameterConditionValue value, string op) {
    ArgumentNullException.ThrowIfNull(runValue);
    ArgumentNullException.ThrowIfNull(value);

    int order;
    if (compareAsNumber) {
      if (!TryParseWhole(runValue, out var left)) {
        throw new FormatException($"The run value '{runValue}' is not a whole number.");
      }

      if (!TryParseWhole(value.Text, out var right)) {
        throw new FormatException($"The value '{value.Text}' is not a whole number.");
      }

      order = left.CompareTo(right);
    }
    else {
      order = string.Compare(runValue, value.Text, StringComparison.OrdinalIgnoreCase);
    }

    return op switch {
      EqualsOperator => order == 0,
      NotEqualsOperator => order != 0,
      GreaterThanOperator => order > 0,
      LessThanOperator => order < 0,
      _ => throw new ArgumentException($"Unknown parameter operator '{op}'.", nameof(op))
    };
  }
}

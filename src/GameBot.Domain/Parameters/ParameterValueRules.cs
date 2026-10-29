using System.Globalization;
using System.Linq;

namespace GameBot.Domain.Parameters;

/// <summary>
/// Checks one binding value against the declaration that it fills (feature 168). The result is a
/// message for the person, never an exception. A caller shows it as a warning, because a stale value
/// must not stop a queue start (FR-016).
/// </summary>
public static class ParameterValueRules {
  /// <summary>
  /// Returns a message with a hint when <paramref name="value"/> does not fit <paramref name="declaration"/>,
  /// otherwise <c>null</c>. A <c>null</c> value (inherit) and a value that is a placeholder are not checked.
  /// </summary>
  /// <param name="declaration">The declaration of the parameter.</param>
  /// <param name="value">The technical value of the binding.</param>
  public static string? Check(ParameterDeclaration declaration, string? value) {
    ArgumentNullException.ThrowIfNull(declaration);
    if (value is null) return null;
    if (ParameterNameRules.IsWholePlaceholder(value)) return null;

    var name = declaration.Name;
    switch (declaration.EffectiveKind) {
      case ParameterInputKind.Number:
        return CheckNumber(declaration, value);
      case ParameterInputKind.Choice:
        if (declaration.Options is { Count: > 0 } && declaration.Options.All(o => o.Value != value)) {
          return $"value '{value}' of parameter '{name}' is not one of its options. Select one of the options.";
        }

        return null;
      case ParameterInputKind.Image:
        if (declaration.AllowedImages is { Count: > 0 } && !declaration.AllowedImages.Contains(value)) {
          return $"image '{value}' of parameter '{name}' is not in the list of allowed images. Select one of the allowed images.";
        }

        return null;
      default:
        return null;
    }
  }

  private static string? CheckNumber(ParameterDeclaration declaration, string value) {
    var name = declaration.Name;
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) {
      return $"value '{value}' of parameter '{name}' is not a whole number. Enter a whole number.";
    }

    if (declaration.Min is not null && number < declaration.Min) {
      return $"value {number} of parameter '{name}' is below the minimum {declaration.Min}. Enter a number of {declaration.Min} or more.";
    }

    if (declaration.Max is not null && number > declaration.Max) {
      return $"value {number} of parameter '{name}' is above the maximum {declaration.Max}. Enter a number of {declaration.Max} or less.";
    }

    return null;
  }
}

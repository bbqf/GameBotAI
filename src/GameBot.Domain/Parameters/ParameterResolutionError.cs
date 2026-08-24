namespace GameBot.Domain.Parameters;

/// <summary>
/// Canonical reasons a parameter reference could not be turned into a usable value (feature 078).
/// </summary>
public static class ParameterResolutionReasons {
  /// <summary>No scope layer, and no declared default, could supply the referenced name.</summary>
  public const string Unresolved = "unresolved";

  /// <summary>A value was found but could not be converted to the target field's numeric type.</summary>
  public const string NotANumber = "not_a_number";
}

/// <summary>
/// A single failure to resolve a parameter reference. A step that produces one of these fails and
/// dispatches nothing to the device, so an unresolved placeholder can never reach a real emulator.
/// </summary>
/// <param name="ParameterName">The referenced name, e.g. <c>adbSerial</c>.</param>
/// <param name="FieldPath">Dotted path of the field that contained the reference, e.g. <c>swipe.startX</c>.</param>
/// <param name="Reason">One of <see cref="ParameterResolutionReasons"/>.</param>
/// <param name="OffendingValue">The value that failed conversion; <c>null</c> when nothing was found.</param>
public sealed record ParameterResolutionError(
    string ParameterName,
    string FieldPath,
    string Reason,
    string? OffendingValue = null) {

  /// <summary>
  /// Renders the operator-facing message for this failure, in the fixed form the API contract and
  /// the integration tests match on.
  /// </summary>
  /// <param name="stepLabel">Step id or label the failure is attributed to.</param>
  public string ToMessage(string stepLabel) =>
      Reason == ParameterResolutionReasons.NotANumber
          ? $"Step '{stepLabel}': parameter '{ParameterName}' resolved to '{OffendingValue}', which is not a whole number for field '{FieldPath}'."
          : $"Step '{stepLabel}': parameter '{ParameterName}' used by field '{FieldPath}' could not be resolved from any scope.";
}

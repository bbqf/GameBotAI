using System.Collections.ObjectModel;

namespace GameBot.Domain.Actions;

/// <summary>
/// The action types a sequence step's <c>primitiveAction</c> may use (feature 102, issue #201): the single list behind
/// the sequence step validator, <c>ActionPayloadValidationService</c> and the published OpenAPI
/// <c>PrimitiveAction.type</c> enum, so the three cannot drift apart.
/// </summary>
public static class SequenceActionTypes {
  /// <summary>
  /// Every supported type in canonical spelling: the device primitives of <see cref="PrimitiveActionTypes.All"/> in
  /// their order, then the service-level <see cref="ActionTypes.RescheduleSelf"/> and <see cref="ActionTypes.Notify"/>.
  /// Callers match against it case-insensitively.
  /// </summary>
  public static IReadOnlyList<string> All { get; } = new ReadOnlyCollection<string>(
    PrimitiveActionTypes.All.Concat(new[] { ActionTypes.RescheduleSelf, ActionTypes.Notify }).ToArray());

  /// <summary>
  /// <see cref="All"/> joined with ", " in its order, the form validation messages use after "expected one of".
  /// </summary>
  public static string SupportedValuesText { get; } = string.Join(", ", All);
}

namespace GameBot.Domain.Actions;

public sealed class PrimitiveActionValidationService {
  public static IReadOnlyCollection<string> SupportedActionTypes => PrimitiveActionTypes.All;

  public static IReadOnlyList<string> Validate(PrimitiveActionBase primitiveAction, PrimitiveActionSelectionContext? context = null) {
    ArgumentNullException.ThrowIfNull(primitiveAction);

    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(primitiveAction.Type)) {
      errors.Add("Primitive action type is required.");
      return errors;
    }

    if (!SupportedActionTypes.Contains(primitiveAction.Type, StringComparer.OrdinalIgnoreCase)) {
      errors.Add($"Primitive action type '{primitiveAction.Type}' is not supported.");
      return errors;
    }

    if (context == PrimitiveActionSelectionContext.ExecutionConnect && !string.Equals(primitiveAction.Type, PrimitiveActionTypes.ConnectToGame, StringComparison.OrdinalIgnoreCase)) {
      errors.Add("Execution connect context requires a connect-to-game primitive action.");
    }

    switch (primitiveAction) {
      case PrimitiveTapAction tap:
        if (tap.X is null || tap.Y is null) {
          errors.Add("Tap primitive actions require x and y.");
        }
        if (tap.DelayMs is < 0) {
          errors.Add("Tap primitive action delayMs must be greater than or equal to zero.");
        }
        if (tap.DurationMs is < 0) {
          errors.Add("Tap primitive action durationMs must be greater than or equal to zero.");
        }
        break;
      case PrimitiveSwipeAction swipe:
        if (swipe.X1 is null || swipe.Y1 is null || swipe.X2 is null || swipe.Y2 is null) {
          errors.Add("Swipe primitive actions require x1, y1, x2, and y2.");
        }
        if (swipe.DurationMs is < 0) {
          errors.Add("Swipe primitive action durationMs must be greater than or equal to zero.");
        }
        break;
      case PrimitiveKeyAction key:
        if (string.IsNullOrWhiteSpace(key.Key) && key.KeyCode is null) {
          errors.Add("Key primitive actions require either key or keyCode.");
        }
        if (key.KeyCode is < 0) {
          errors.Add("Key primitive action keyCode must be greater than or equal to zero.");
        }
        break;
      case PrimitiveCommandAction command:
        if (string.IsNullOrWhiteSpace(command.CommandId)) {
          errors.Add("Command primitive actions require commandId.");
        }
        break;
      case PrimitiveConnectToGameAction connect:
        if (string.IsNullOrWhiteSpace(connect.GameId)) {
          errors.Add("Connect-to-game primitive actions require gameId.");
        }
        if (string.IsNullOrWhiteSpace(connect.AdbSerial)) {
          errors.Add("Connect-to-game primitive actions require adbSerial.");
        }
        break;
    }

    return errors;
  }
}

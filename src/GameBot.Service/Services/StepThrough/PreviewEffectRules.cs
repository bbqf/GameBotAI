using System.Collections.Generic;
using GameBot.Domain.Commands;

namespace GameBot.Service.Services.StepThrough;

/// <summary>
/// Decides which command steps a step-through must not run (feature 127, FR-015a). A command step with an
/// outside effect shows its intended effect in a step-through and does not run.
/// <para>
/// Today every command step type only uses the device (tap, swipe, key, wait, game and emulator
/// control), so none of them has an outside effect. The service-level actions with an outside effect,
/// <c>reschedule-self</c> and <c>notify</c>, exist only as sequence action steps, and the stepper previews
/// them there. This class is the one place to change when a command step type gets an outside effect.
/// The list is explicit: a unit test fails for a command step type that is not in the list.
/// </para>
/// </summary>
internal static class PreviewEffectRules {
  /// <summary>The status of a step outcome that the executor previewed and did not run.</summary>
  public const string PreviewedStatus = "previewed";

  /// <summary>For each command step type: true when a step-through previews the step and does not run it.</summary>
  public static IReadOnlyDictionary<CommandStepType, bool> ByType { get; } = new Dictionary<CommandStepType, bool> {
    [CommandStepType.Command] = false,
    [CommandStepType.PrimitiveTap] = false,
    [CommandStepType.WaitForImage] = false,
    [CommandStepType.EnsureGameRunning] = false,
    [CommandStepType.KeyInput] = false,
    [CommandStepType.Swipe] = false,
    [CommandStepType.GoToHomeScreen] = false,
    [CommandStepType.EnsureEmulatorRunning] = false
  };

  /// <summary>
  /// Returns true, with the intended effect, when a step-through must not run <paramref name="step"/>.
  /// A step type that is not in <see cref="ByType"/> runs, as in a real run.
  /// </summary>
  public static bool TryDescribe(CommandStep step, out string effect) {
    ArgumentNullException.ThrowIfNull(step);

    // Feature 129: a restart stops the game on the device. A step-through previews it and does not run it.
    if (step.Type == CommandStepType.EnsureGameRunning && step.EnsureGameRunning?.ForceRestart == true) {
      effect = "would stop the game, start it again, and wait for the foreground";
      return true;
    }

    if (ByType.TryGetValue(step.Type, out var previews) && previews) {
      effect = $"would run a '{step.Type}' step";
      return true;
    }

    effect = string.Empty;
    return false;
  }
}

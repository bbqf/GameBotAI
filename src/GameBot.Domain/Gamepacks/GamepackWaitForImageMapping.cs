using System;
using System.Collections.Generic;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// Feature 177 (B-039): sets the <see cref="SequenceStep.WaitForImage"/> configuration of each inline
/// WaitForImage step in a pack sequence. The pack read keeps only the action payload. The runner reads only
/// the configuration. The walk runs before the take-over hash is computed.
/// </summary>
public static class GamepackWaitForImageMapping {
  /// <summary>Sets the configuration of every WaitForImage step at every depth. The sequence changes in place.</summary>
  /// <param name="sequence">The sequence.</param>
  public static void Apply(CommandSequence sequence) {
    ArgumentNullException.ThrowIfNull(sequence);
    Walk(sequence.Steps);
  }

  private static void Walk(IReadOnlyList<SequenceStep>? steps) {
    if (steps is null) return;
    foreach (var step in steps) {
      if (step.Action is not null && string.Equals(step.Action.Type, ActionTypes.WaitForImage, StringComparison.OrdinalIgnoreCase)) {
        step.WaitForImage = WaitForImageConfigMapper.Map(step.Action.Type, step.Action.Parameters);
      }

      Walk(step.Body);
      Walk(step.ElseBody);
    }
  }
}

using System;
using System.Collections.Generic;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services {
  /// <summary>
  /// Feature 117: finds the Break steps of a sequence. At the start of a run, the runner gives each
  /// Break the default outcome <see cref="BreakOutcomes.NoBreak"/>. Thus a <c>commandOutcome</c>
  /// condition that names a Break that did not run (If branch not taken, or a loop body with zero
  /// iterations) evaluates as <c>no_break</c>. A Break that runs writes its own outcome over the default.
  /// </summary>
  public static class BreakStepIndex {
    /// <summary>
    /// Gets the step ids of all Break steps in <paramref name="steps"/>, in each loop body and in each
    /// If branch at each depth. A Break with an empty step id is not in the set. An id that also names a
    /// step of a different type is not in the set, because the default must not hide a missing outcome
    /// of that step. The set is case-insensitive.
    /// </summary>
    /// <param name="steps">The root steps of the sequence.</param>
    /// <returns>The step ids of the Break steps.</returns>
    public static IReadOnlySet<string> CollectBreakStepIds(IReadOnlyList<SequenceStep> steps) {
      ArgumentNullException.ThrowIfNull(steps);
      var breakIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var otherIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      Collect(steps, breakIds, otherIds);
      breakIds.ExceptWith(otherIds);
      return breakIds;
    }

    /// <summary>
    /// Adds <see cref="BreakOutcomes.NoBreak"/> to <paramref name="outcomes"/> for each Break step of
    /// <paramref name="steps"/>. A value that is already in the map does not change.
    /// </summary>
    /// <param name="outcomes">The outcome map of the run.</param>
    /// <param name="steps">The root steps of the sequence.</param>
    public static void SeedNoBreakOutcomes(IDictionary<string, string> outcomes, IReadOnlyList<SequenceStep> steps) {
      ArgumentNullException.ThrowIfNull(outcomes);
      ArgumentNullException.ThrowIfNull(steps);
      foreach (var id in CollectBreakStepIds(steps)) {
        outcomes.TryAdd(id, BreakOutcomes.NoBreak);
      }
    }

    private static void Collect(IReadOnlyList<SequenceStep>? steps, HashSet<string> breakIds, HashSet<string> otherIds) {
      if (steps is null) return;
      foreach (var step in steps) {
        if (step is null) continue;
        if (!string.IsNullOrWhiteSpace(step.StepId)) {
          (step.StepType == SequenceStepType.Break ? breakIds : otherIds).Add(step.StepId);
        }
        Collect(step.Body, breakIds, otherIds);
        Collect(step.ElseBody, breakIds, otherIds);
      }
    }
  }
}

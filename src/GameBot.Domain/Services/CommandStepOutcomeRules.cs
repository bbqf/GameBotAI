using GameBot.Domain.Actions;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services;

/// <summary>
/// The rules of the <c>commandStepOutcome</c> condition that do not depend on the slot of the condition
/// (feature 167). The validators and the endpoints share these rules, so the messages are the same.
/// </summary>
public static class CommandStepOutcomeRules {
  /// <summary>Gives the start of a message: the step label, and the path when the condition is nested.</summary>
  public static string Prefix(string stepLabel, string path)
    => path == "$" ? $"Step '{stepLabel}' " : $"Step '{stepLabel}' condition at {path}: ";

  /// <summary>Adds the errors for the order and for the expected state of <paramref name="condition"/>.</summary>
  public static void ValidateFields(CommandStepOutcomeStepCondition condition, string stepLabel, string path, ICollection<string> errors) {
    ArgumentNullException.ThrowIfNull(condition);
    ArgumentNullException.ThrowIfNull(errors);
    var prefix = Prefix(stepLabel, path);

    if (condition.CommandStepOrder < 1) {
      errors.Add($"{prefix}commandStepOutcome commandStepOrder must be 1 or more.");
    }

    if (!CommandStepOutcomeStates.IsAllowed(condition.ExpectedState)) {
      errors.Add($"{prefix}commandStepOutcome expectedState must be one of {CommandStepOutcomeStates.AllText}.");
    }
  }

  /// <summary>True when the sequence step runs a command (a command step, or an action step of type command).</summary>
  public static bool RunsCommand(SequenceStep step) {
    ArgumentNullException.ThrowIfNull(step);
    if (step.StepType is SequenceStepType.Loop or SequenceStepType.Break or SequenceStepType.If or SequenceStepType.Conditional) {
      return false;
    }

    if (string.IsNullOrWhiteSpace(step.CommandId)) {
      return false;
    }

    return step.Action is null
      || string.Equals(step.Action.Type, ActionTypes.Command, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>Gives each <c>commandStepOutcome</c> leaf in the condition tree, including the children of a composite.</summary>
  public static IEnumerable<CommandStepOutcomeStepCondition> Collect(SequenceStepCondition? condition) {
    switch (condition) {
      case CommandStepOutcomeStepCondition leaf:
        yield return leaf;
        break;
      case CompositeStepCondition composite:
        foreach (var child in composite.Children) {
          foreach (var found in Collect(child)) {
            yield return found;
          }
        }

        break;
    }
  }

  /// <summary>
  /// Gives the <c>stepRef</c>, order, and state errors of the <c>commandStepOutcome</c> conditions in
  /// <paramref name="steps"/>. A run and a dry run use this check. It does not check the order against
  /// the command: a missing order at run time gives "no outcome recorded". Gives an empty list when no
  /// step has such a condition.
  /// </summary>
  public static IReadOnlyList<string> ValidateReferences(IReadOnlyList<SequenceStep> steps) {
    ArgumentNullException.ThrowIfNull(steps);
    if (!AnyStepHasCondition(steps)) {
      return Array.Empty<string>();
    }

    return new SequenceStepValidationService().Validate(steps)
      .Where(error => error.Contains("commandStepOutcome", StringComparison.Ordinal))
      .ToList();
  }

  private static bool AnyStepHasCondition(IReadOnlyList<SequenceStep> steps) {
    foreach (var step in steps) {
      if (ConditionsOf(step).Any(condition => Collect(condition).Any())) return true;
      if (AnyStepHasCondition(step.Body)) return true;
      if (step.ElseBody is not null && AnyStepHasCondition(step.ElseBody)) return true;
    }

    return false;
  }

  /// <summary>Gives every condition of a step: its condition, break condition, if condition, and loop condition.</summary>
  public static IEnumerable<SequenceStepCondition> ConditionsOf(SequenceStep step) {
    ArgumentNullException.ThrowIfNull(step);
    if (step.Condition is not null) yield return step.Condition;
    if (step.BreakCondition is not null) yield return step.BreakCondition;
    if (step.If?.Condition is not null) yield return step.If.Condition;
    var loopCondition = step.Loop switch {
      WhileLoopConfig whileConfig => whileConfig.Condition,
      RepeatUntilLoopConfig repeatConfig => repeatConfig.Condition,
      _ => null
    };
    if (loopCondition is not null) yield return loopCondition;
  }
}

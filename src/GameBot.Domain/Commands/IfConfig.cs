namespace GameBot.Domain.Commands;

/// <summary>
/// Configuration for an if step (<see cref="SequenceStepType.If"/>). Holds the branch-selection
/// condition; the then branch lives in <see cref="SequenceStep.Body"/> and the optional else
/// branch in <see cref="SequenceStep.ElseBody"/>.
/// </summary>
public sealed class IfConfig {
  /// <summary>
  /// Condition evaluated exactly once when the if step is reached. Uses the same condition
  /// model as <see cref="WhileLoopConfig.Condition"/> (imageVisible or commandOutcome, with
  /// optional negation). True selects the then branch, false the else branch.
  /// </summary>
  public required SequenceStepCondition Condition { get; set; }
}

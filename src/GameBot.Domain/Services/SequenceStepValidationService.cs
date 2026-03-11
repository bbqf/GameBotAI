using GameBot.Domain.Commands;
using System.Linq;

namespace GameBot.Domain.Services;

public sealed class SequenceStepValidationService {
  private readonly StringComparer _stepIdComparer = StringComparer.Ordinal;
  private static readonly HashSet<string> AllowedCommandOutcomeStates = new(StringComparer.OrdinalIgnoreCase) {
    "success",
    "failed",
    "skipped"
  };

  public IReadOnlyList<string> Validate(IReadOnlyList<SequenceStep> steps) {
    ArgumentNullException.ThrowIfNull(steps);

    var errors = new List<string>();
    var seenStepIds = new HashSet<string>(_stepIdComparer);

    for (var index = 0; index < steps.Count; index++) {
      var step = steps[index];
      var stepLabel = string.IsNullOrWhiteSpace(step.StepId) ? $"index:{index}" : step.StepId;

      if (string.IsNullOrWhiteSpace(step.StepId)) {
        errors.Add($"Step at index {index} requires non-empty stepId.");
      }
      else if (!seenStepIds.Add(step.StepId)) {
        errors.Add($"Duplicate step id '{step.StepId}'.");
      }

      if (step.Action is null) {
        errors.Add($"Step '{stepLabel}' requires action payload.");
      }

      if (step.Condition is CommandOutcomeStepCondition commandOutcome) {
        if (string.IsNullOrWhiteSpace(commandOutcome.StepRef)) {
          errors.Add($"Step '{stepLabel}' commandOutcome condition requires stepRef.");
        }
        else {
          var referencedIndex = steps
            .Select((candidate, candidateIndex) => new { candidate.StepId, candidateIndex })
            .FirstOrDefault(candidate => string.Equals(candidate.StepId, commandOutcome.StepRef, StringComparison.Ordinal))
            ?.candidateIndex ?? -1;

          if (referencedIndex < 0) {
            errors.Add($"Step '{stepLabel}' commandOutcome references unknown prior step '{commandOutcome.StepRef}'.");
          }
          else if (referencedIndex >= index) {
            errors.Add($"Step '{stepLabel}' commandOutcome stepRef '{commandOutcome.StepRef}' must reference a prior step.");
          }
        }

        if (string.IsNullOrWhiteSpace(commandOutcome.ExpectedState)
            || !AllowedCommandOutcomeStates.Contains(commandOutcome.ExpectedState)) {
          errors.Add($"Step '{stepLabel}' commandOutcome expectedState must be one of success|failed|skipped.");
        }
      }

      if (step.Condition is ImageVisibleStepCondition imageVisible
          && string.IsNullOrWhiteSpace(imageVisible.ImageId)) {
        errors.Add($"Step '{stepLabel}' imageVisible condition requires imageId.");
      }
    }

    return errors;
  }

  public IReadOnlyList<string> Validate(SequenceFlowGraph graph) {
    ArgumentNullException.ThrowIfNull(graph);

    var errors = new List<string>();
    var seenStepIds = new HashSet<string>(_stepIdComparer);
    foreach (var step in graph.Steps) {
      if (!string.IsNullOrWhiteSpace(step.StepId) && !seenStepIds.Add(step.StepId)) {
        errors.Add($"Duplicate step id '{step.StepId}'.");
      }

      switch (step.StepType) {
        case FlowStepType.Action:
        case FlowStepType.Command:
          if (string.IsNullOrWhiteSpace(step.PayloadRef)) {
            errors.Add($"Step '{step.StepId}' requires payloadRef for step type '{step.StepType}'.");
          }
          break;
        case FlowStepType.Condition:
          if (step.Condition is null) {
            errors.Add($"Condition step '{step.StepId}' requires a condition expression.");
          }
          break;
        case FlowStepType.Terminal:
          if (!string.IsNullOrWhiteSpace(step.PayloadRef)) {
            errors.Add($"Terminal step '{step.StepId}' must not define payloadRef.");
          }
          break;
      }
    }

    return errors;
  }
}

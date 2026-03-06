using GameBot.Domain.Commands;

namespace GameBot.Domain.Services;

public sealed class SequenceStepValidationService {
  private readonly StringComparer _stepIdComparer = StringComparer.Ordinal;

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

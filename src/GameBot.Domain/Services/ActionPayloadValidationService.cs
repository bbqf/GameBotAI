using GameBot.Domain.Actions;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services;

public sealed class ActionPayloadValidationService {
  private readonly HashSet<string> _supportedActionTypes = new(StringComparer.OrdinalIgnoreCase) {
    ActionTypes.Tap,
    ActionTypes.Swipe,
    ActionTypes.Key,
    ActionTypes.ConnectToGame
  };

  public IReadOnlyCollection<string> SupportedActionTypes => _supportedActionTypes;

  public async Task<IReadOnlyList<string>> ValidateAsync(SequenceFlowGraph graph, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(graph);

    var errors = new List<string>();
    foreach (var step in graph.Steps) {
      ct.ThrowIfCancellationRequested();

      if (string.IsNullOrWhiteSpace(step.PayloadRef)) {
        continue;
      }

      if (step.StepType == FlowStepType.Action && step.PayloadRef.Contains(':', StringComparison.Ordinal)) {
        var separatorIndex = step.PayloadRef.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0) {
          errors.Add($"Action step '{step.StepId}' has malformed action payload reference.");
          continue;
        }

        var candidateType = step.PayloadRef[..separatorIndex].Trim();
        if (!_supportedActionTypes.Contains(candidateType)) {
          errors.Add($"Action step '{step.StepId}' references unsupported action type '{candidateType}'.");
        }
      }
    }

    await Task.CompletedTask.ConfigureAwait(false);
    return errors;
  }
}

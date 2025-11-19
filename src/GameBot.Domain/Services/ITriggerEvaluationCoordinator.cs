namespace GameBot.Domain.Services;

public interface ITriggerEvaluationCoordinator {
  Task<int> EvaluateAllAsync(string? gameId = null, CancellationToken ct = default);
}

namespace GameBot.Domain.Actions;

public interface IActionRepository {
  Task<Action> AddAsync(Action action, CancellationToken ct = default);
  Task<Action?> GetAsync(string id, CancellationToken ct = default);
  Task<IReadOnlyList<Action>> ListAsync(string? gameId = null, CancellationToken ct = default);
  Task<Action?> UpdateAsync(Action action, CancellationToken ct = default);
  Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

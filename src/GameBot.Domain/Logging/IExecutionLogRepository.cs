namespace GameBot.Domain.Logging;

public interface IExecutionLogRepository {
  Task AddAsync(ExecutionLogEntry entry, CancellationToken ct = default);

  /// <summary>
  /// Writes the entry, replacing any existing entry with the same <see cref="ExecutionLogEntry.Id"/>
  /// (used to finalize an in-progress root execution without creating a duplicate row).
  /// </summary>
  Task UpsertAsync(ExecutionLogEntry entry, CancellationToken ct = default);

  Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default);

  /// <summary>
  /// Returns the entry identified by <paramref name="rootId"/> together with all descendant
  /// entries (those whose <see cref="ExecutionHierarchyContext.RootExecutionId"/> equals it),
  /// for building a nested execution tree. Empty when the root is unknown.
  /// </summary>
  Task<IReadOnlyList<ExecutionLogEntry>> GetSubtreeAsync(string rootId, CancellationToken ct = default);

  Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default);
  Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}

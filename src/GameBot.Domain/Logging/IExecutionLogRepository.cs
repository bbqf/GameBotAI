namespace GameBot.Domain.Logging;

public interface IExecutionLogRepository
{
  Task AddAsync(ExecutionLogEntry entry, CancellationToken ct = default);
  Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default);
  Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default);
  Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}

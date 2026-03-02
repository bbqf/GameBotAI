using GameBot.Domain.Logging;
using GameBot.Service.Services;

namespace GameBot.Service.Services.ExecutionLog;

internal interface IExecutionLogService {
  Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, string? parentExecutionId, int depth, CancellationToken ct = default);
  Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, ExecutionLogContext context, CancellationToken ct = default);
  Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, string? parentExecutionId, int depth, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default);
  Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default);
  Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default);
  Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default);
  Task<ExecutionLogRetentionPolicy> GetRetentionAsync(CancellationToken ct = default);
  Task<ExecutionLogRetentionPolicy> UpdateRetentionAsync(bool enabled, int? retentionDays, int? cleanupIntervalMinutes, CancellationToken ct = default);
  Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}

internal sealed class ExecutionLogService : IExecutionLogService {
  private readonly IExecutionLogRepository _repository;
  private readonly IExecutionLogRetentionPolicyRepository _retentionRepository;

  public ExecutionLogService(
    IExecutionLogRepository repository,
    IExecutionLogRetentionPolicyRepository retentionRepository) {
    _repository = repository;
    _retentionRepository = retentionRepository;
  }

  public async Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, string? parentExecutionId, int depth, CancellationToken ct = default)
    => await LogCommandExecutionAsync(
      commandId,
      commandName,
      finalStatus,
      primitiveOutcomes,
      new ExecutionLogContext {
        ParentExecutionId = parentExecutionId,
        Depth = depth
      },
      ct).ConfigureAwait(false);

  public async Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, ExecutionLogContext context, CancellationToken ct = default) {
    var retention = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    var now = DateTimeOffset.UtcNow;

    var stepOutcomes = primitiveOutcomes
      .Select(o => new ExecutionStepOutcome(
        o.StepOrder,
        "primitiveTap",
        string.Equals(o.Status, "executed", StringComparison.OrdinalIgnoreCase) ? "executed" : "not_executed",
        o.Status,
        o.Reason))
      .ToList();

    var details = new List<ExecutionDetailItem>();
    foreach (var outcome in primitiveOutcomes) {
      if (string.Equals(outcome.Status, "executed", StringComparison.OrdinalIgnoreCase) && outcome.ResolvedPoint is not null) {
        details.Add(new ExecutionDetailItem(
          "tap",
          $"Tap executed at ({outcome.ResolvedPoint.X},{outcome.ResolvedPoint.Y}).",
          new Dictionary<string, object?> {
            ["x"] = outcome.ResolvedPoint.X,
            ["y"] = outcome.ResolvedPoint.Y,
            ["confidence"] = outcome.DetectionConfidence
          },
          "normal"));
      }
      else {
        details.Add(new ExecutionDetailItem(
          "step",
          $"Step {outcome.StepOrder} was not executed: {outcome.Reason ?? outcome.Status}",
          new Dictionary<string, object?> {
            ["reasonCode"] = outcome.Status,
            ["reason"] = outcome.Reason
          },
          "normal"));
      }
    }

    var entry = new ExecutionLogEntry {
      TimestampUtc = now,
      ExecutionType = "command",
      FinalStatus = NormalizeStatus(finalStatus),
      ObjectRef = new ExecutionObjectReference("command", commandId, commandName),
      Navigation = ExecutionNavigationBuilder.Build("command", commandId, context),
      Hierarchy = ExecutionHierarchyBuilder.Build(context),
      Summary = TrimSummary($"Command '{commandName}' {NormalizeStatus(finalStatus)} with {stepOutcomes.Count} tracked step outcomes."),
      Details = TrimDetails(ExecutionLogSanitizer.SanitizeDetails(details)),
      StepOutcomes = stepOutcomes,
      RetentionExpiresUtc = retention.Enabled ? now.AddDays(Math.Max(1, retention.RetentionDays)) : DateTimeOffset.MaxValue
    };

    await _repository.AddAsync(entry, ct).ConfigureAwait(false);
  }

  public async Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, string? parentExecutionId, int depth, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default)
    => await LogSequenceExecutionAsync(
      sequenceId,
      sequenceName,
      finalStatus,
      summary,
      new ExecutionLogContext {
        ParentExecutionId = parentExecutionId,
        Depth = depth
      },
      details,
      ct).ConfigureAwait(false);

  public async Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) {
    var retention = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    var now = DateTimeOffset.UtcNow;

    var entry = new ExecutionLogEntry {
      TimestampUtc = now,
      ExecutionType = "sequence",
      FinalStatus = NormalizeStatus(finalStatus),
      ObjectRef = new ExecutionObjectReference("sequence", sequenceId, sequenceName),
      Navigation = ExecutionNavigationBuilder.Build("sequence", sequenceId, context),
      Hierarchy = ExecutionHierarchyBuilder.Build(context),
      Summary = TrimSummary(summary),
      Details = TrimDetails(ExecutionLogSanitizer.SanitizeDetails(details?.ToList() ?? new List<ExecutionDetailItem>())),
      StepOutcomes = Array.Empty<ExecutionStepOutcome>(),
      RetentionExpiresUtc = retention.Enabled ? now.AddDays(Math.Max(1, retention.RetentionDays)) : DateTimeOffset.MaxValue
    };

    await _repository.AddAsync(entry, ct).ConfigureAwait(false);
  }

  public Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default)
  {
    var normalized = NormalizeQuery(query);
    return _repository.QueryAsync(normalized, ct);
  }

  public Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default)
    => _repository.GetAsync(id, ct);

  public Task<ExecutionLogRetentionPolicy> GetRetentionAsync(CancellationToken ct = default)
    => _retentionRepository.GetAsync(ct);

  public async Task<ExecutionLogRetentionPolicy> UpdateRetentionAsync(bool enabled, int? retentionDays, int? cleanupIntervalMinutes, CancellationToken ct = default) {
    var current = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    var updated = new ExecutionLogRetentionPolicy {
      Enabled = enabled,
      RetentionDays = retentionDays ?? current.RetentionDays,
      CleanupIntervalMinutes = cleanupIntervalMinutes ?? current.CleanupIntervalMinutes,
      UpdatedAtUtc = DateTimeOffset.UtcNow
    };
    return await _retentionRepository.SaveAsync(updated, ct).ConfigureAwait(false);
  }

  public async Task<int> CleanupExpiredAsync(CancellationToken ct = default) {
    var policy = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    if (!policy.Enabled) return 0;
    return await _repository.DeleteExpiredAsync(DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
  }

  private static string NormalizeStatus(string status)
    => string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ? "success" : "failure";

  private static string TrimSummary(string summary) {
    var text = string.IsNullOrWhiteSpace(summary) ? "Execution completed." : summary.Trim();
    return text.Length <= 240 ? text : text[..240];
  }

  private static IReadOnlyList<ExecutionDetailItem> TrimDetails(IReadOnlyList<ExecutionDetailItem> details) {
    if (details.Count <= 10) return details;
    var trimmed = details.Take(9).ToList();
    trimmed.Add(new ExecutionDetailItem("meta", "Additional details were truncated.", null, "normal"));
    return trimmed;
  }

  private static ExecutionLogQuery NormalizeQuery(ExecutionLogQuery? query)
  {
    var source = query ?? new ExecutionLogQuery();

    var sortByRaw = source.SortBy?.Trim();
    var normalizedSortBy = sortByRaw?.ToUpperInvariant() switch
    {
      "TIMESTAMP" => "timestamp",
      "OBJECTNAME" => "objectName",
      "STATUS" => "status",
      _ => "timestamp"
    };

    var normalizedDirection = string.Equals(source.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
      ? "asc"
      : "desc";

    return new ExecutionLogQuery
    {
      SortBy = normalizedSortBy,
      SortDirection = normalizedDirection,
      FilterTimestamp = source.FilterTimestamp,
      FilterObjectName = source.FilterObjectName,
      FilterStatus = source.FilterStatus,
      PageToken = source.PageToken,
      FromUtc = source.FromUtc,
      ToUtc = source.ToUtc,
      FinalStatus = source.FinalStatus,
      ObjectType = source.ObjectType,
      ObjectId = source.ObjectId,
      PageSize = source.PageSize <= 0 ? 50 : source.PageSize,
      Cursor = source.Cursor
    };
  }
}

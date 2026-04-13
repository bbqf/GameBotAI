using GameBot.Domain.Logging;
using GameBot.Service.Services;
using System.Text.Json;

namespace GameBot.Service.Services.ExecutionLog;

internal sealed record ExecutionLogRelatedProjection(
  string Label,
  string TargetType,
  string TargetId,
  bool IsAvailable,
  string? UnavailableReason);

internal sealed record ExecutionLogStepProjection(
  string SequenceId,
  string SequenceLabel,
  string? StepId,
  string StepLabel,
  string StepName,
  string Status,
  string Message,
  ExecutionLogStepDeepLinkProjection DeepLink,
  ConditionEvaluationTrace? ConditionTrace);

internal sealed record ExecutionLogStepDeepLinkProjection(
  string SequenceId,
  string? StepId,
  string SequenceLabel,
  string StepLabel,
  string ResolutionStatus,
  string DirectPath,
  string? FallbackRoute);

internal sealed record ExecutionLogDetailProjection(
  string Summary,
  IReadOnlyList<ExecutionLogRelatedProjection> RelatedObjects,
  bool HasSnapshot,
  string? SnapshotCaption,
  IReadOnlyList<ExecutionLogStepProjection> StepOutcomes);

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
    var sanitizedDetails = ExecutionLogSanitizer.SanitizeDetails(details?.ToList() ?? new List<ExecutionDetailItem>());
    // Compute step outcomes before trimming so all loop iterations are captured,
    // including "Break triggered" entries that would otherwise be truncated.
    var stepOutcomes = BuildSequenceStepOutcomes(sequenceId, sequenceName, context, sanitizedDetails);
    var trimmedDetails = TrimDetails(sanitizedDetails);

    var entry = new ExecutionLogEntry {
      TimestampUtc = now,
      ExecutionType = "sequence",
      FinalStatus = NormalizeStatus(finalStatus),
      ObjectRef = new ExecutionObjectReference("sequence", sequenceId, sequenceName),
      Navigation = ExecutionNavigationBuilder.Build("sequence", sequenceId, context),
      Hierarchy = ExecutionHierarchyBuilder.Build(context),
      Summary = TrimSummary(summary),
      Details = trimmedDetails,
      StepOutcomes = stepOutcomes,
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

  internal static ExecutionLogDetailProjection BuildDetailProjection(ExecutionLogEntry entry)
  {
    var summary = string.IsNullOrWhiteSpace(entry.Summary) ? "Execution completed." : entry.Summary;

    var relatedObjects = new List<ExecutionLogRelatedProjection>
    {
      new(
        entry.ObjectRef.DisplayNameSnapshot,
        entry.ObjectRef.ObjectType,
        entry.ObjectRef.ObjectId,
        true,
        null)
    };

    if (!string.IsNullOrWhiteSpace(entry.Navigation.ParentPath))
    {
      relatedObjects.Add(new ExecutionLogRelatedProjection(
        "Parent execution",
        "execution",
        entry.Hierarchy.ParentExecutionId ?? string.Empty,
        !string.IsNullOrWhiteSpace(entry.Hierarchy.ParentExecutionId),
        string.IsNullOrWhiteSpace(entry.Hierarchy.ParentExecutionId) ? "Parent execution is unavailable." : null));
    }

    var hasSnapshot = entry.Details.Any(detail =>
      string.Equals(detail.Kind, "snapshot", StringComparison.OrdinalIgnoreCase) ||
      (detail.Attributes?.ContainsKey("imageUrl") ?? false));

    var stepOutcomes = entry.StepOutcomes
      .Select(step => new ExecutionLogStepProjection(
        ResolveSequenceId(entry, step),
        ResolveSequenceLabel(entry, step),
        step.StepId,
        ResolveStepLabel(step),
        string.IsNullOrWhiteSpace(step.StepType) ? $"Step {step.StepOrder}" : step.StepType,
        step.Outcome,
        step.ReasonText ?? step.ReasonCode ?? "Step completed.",
        BuildDeepLink(entry, step),
        step.ConditionTrace))
      .ToArray();

    return new ExecutionLogDetailProjection(
      summary,
      relatedObjects,
      hasSnapshot,
      hasSnapshot ? "Snapshot captured during execution." : null,
      stepOutcomes);
  }

  private static string NormalizeStatus(string status)
    => string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ? "success" : "failure";

  private static List<ExecutionStepOutcome> BuildSequenceStepOutcomes(
    string sequenceId,
    string sequenceName,
    ExecutionLogContext context,
    IReadOnlyList<ExecutionDetailItem> details) {
    var results = new List<ExecutionStepOutcome>();
    var stepOrder = 1;

    foreach (var detail in details.Where(detail => string.Equals(detail.Kind, "step", StringComparison.OrdinalIgnoreCase))) {
      var attributes = detail.Attributes;
      var order = TryGetInt(attributes, "stepOrder") ?? stepOrder;
      stepOrder = Math.Max(stepOrder, order + 1);

      var status = TryGetString(attributes, "actionOutcome") ?? TryGetString(attributes, "status") ?? "executed";
      var stepType = TryGetString(attributes, "stepType") ?? "step";
      var reasonCode = TryGetString(attributes, "reasonCode");
      var resolvedSequenceId = TryGetString(attributes, "sequenceId") ?? context.SequenceId ?? sequenceId;
      var resolvedSequenceLabel = TryGetString(attributes, "sequenceLabel") ?? context.SequenceLabel ?? sequenceName;
      var resolvedStepId = TryGetString(attributes, "stepId") ?? context.StepId;
      var resolvedStepLabel = TryGetString(attributes, "stepLabel") ?? context.StepLabel;
      var conditionTrace = TryGetConditionTrace(attributes, "conditionTrace")
                           ?? BuildConditionTraceFromAttributes(attributes);

      results.Add(new ExecutionStepOutcome(
        order,
        stepType,
        status,
        reasonCode,
        detail.Message,
        resolvedSequenceId,
        resolvedStepId,
        resolvedSequenceLabel,
        resolvedStepLabel,
        conditionTrace));
    }

    return results;
  }

  private static ExecutionLogStepDeepLinkProjection BuildDeepLink(ExecutionLogEntry entry, ExecutionStepOutcome step) {
    var sequenceId = ResolveSequenceId(entry, step);
    var sequenceLabel = ResolveSequenceLabel(entry, step);
    var stepLabel = ResolveStepLabel(step);
    if (string.IsNullOrWhiteSpace(sequenceId)) {
      return new ExecutionLogStepDeepLinkProjection(
        string.Empty,
        step.StepId,
        sequenceLabel,
        stepLabel,
        "sequence_missing",
        "/authoring/sequences",
        "/authoring/sequences");
    }

    if (string.IsNullOrWhiteSpace(step.StepId)) {
      var fallbackRoute = $"/authoring/sequences/{sequenceId}";
      return new ExecutionLogStepDeepLinkProjection(
        sequenceId,
        null,
        sequenceLabel,
        stepLabel,
        "step_missing",
        fallbackRoute,
        fallbackRoute);
    }

    return new ExecutionLogStepDeepLinkProjection(
      sequenceId,
      step.StepId,
      sequenceLabel,
      stepLabel,
      "resolved",
      $"/authoring/sequences/{sequenceId}?stepId={Uri.EscapeDataString(step.StepId)}",
      null);
  }

  private static string ResolveSequenceId(ExecutionLogEntry entry, ExecutionStepOutcome step)
    => string.IsNullOrWhiteSpace(step.SequenceId) ? entry.ObjectRef.ObjectId : step.SequenceId;

  private static string ResolveSequenceLabel(ExecutionLogEntry entry, ExecutionStepOutcome step)
    => string.IsNullOrWhiteSpace(step.SequenceLabel) ? entry.ObjectRef.DisplayNameSnapshot : step.SequenceLabel;

  private static string ResolveStepLabel(ExecutionStepOutcome step)
    => string.IsNullOrWhiteSpace(step.StepLabel)
      ? (string.IsNullOrWhiteSpace(step.StepType) ? $"Step {step.StepOrder}" : step.StepType)
      : step.StepLabel;

  private static string? TryGetString(Dictionary<string, object?>? attributes, string key) {
    if (attributes is null || !attributes.TryGetValue(key, out var value) || value is null) {
      return null;
    }

    return value switch {
      string s => s,
      _ => value.ToString()
    };
  }

  private static int? TryGetInt(Dictionary<string, object?>? attributes, string key) {
    if (attributes is null || !attributes.TryGetValue(key, out var value) || value is null) {
      return null;
    }

    return value switch {
      int intValue => intValue,
      long longValue => (int)longValue,
      JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
      string text when int.TryParse(text, out var parsed) => parsed,
      _ => null
    };
  }

  private static ConditionEvaluationTrace? TryGetConditionTrace(Dictionary<string, object?>? attributes, string key) {
    if (attributes is null || !attributes.TryGetValue(key, out var value) || value is null) {
      return null;
    }

    if (value is ConditionEvaluationTrace trace) {
      return trace;
    }

    if (value is JsonElement element && element.ValueKind == JsonValueKind.Object) {
      var finalResult = element.TryGetProperty("finalResult", out var finalResultElement) && finalResultElement.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? finalResultElement.GetBoolean()
        : false;
      var selectedBranch = element.TryGetProperty("selectedBranch", out var selectedBranchElement) && selectedBranchElement.ValueKind == JsonValueKind.String
        ? selectedBranchElement.GetString() ?? "none"
        : "none";
      var failureReason = element.TryGetProperty("failureReason", out var failureReasonElement) && failureReasonElement.ValueKind == JsonValueKind.String
        ? failureReasonElement.GetString()
        : null;

      return new ConditionEvaluationTrace(
        finalResult,
        selectedBranch,
        failureReason,
        Array.Empty<Dictionary<string, object?>>(),
        Array.Empty<Dictionary<string, object?>>());
    }

    return null;
  }

  private static ConditionEvaluationTrace? BuildConditionTraceFromAttributes(Dictionary<string, object?>? attributes) {
    var raw = TryGetString(attributes, "conditionResult");
    if (string.IsNullOrWhiteSpace(raw)) {
      return null;
    }

    var value = raw.Trim();
    if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) {
      return new ConditionEvaluationTrace(true, "true", null, Array.Empty<Dictionary<string, object?>>(), Array.Empty<Dictionary<string, object?>>());
    }

    if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) {
      return new ConditionEvaluationTrace(false, "false", null, Array.Empty<Dictionary<string, object?>>(), Array.Empty<Dictionary<string, object?>>());
    }

    if (string.Equals(value, "error", StringComparison.OrdinalIgnoreCase)) {
      return new ConditionEvaluationTrace(false, "error", "condition-evaluation-error", Array.Empty<Dictionary<string, object?>>(), Array.Empty<Dictionary<string, object?>>());
    }

    return null;
  }

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

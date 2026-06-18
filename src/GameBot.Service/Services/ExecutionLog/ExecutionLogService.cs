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
  string? CommandName,
  string StepType,
  string Status,
  string Message,
  WaitForImageDetailAttributes? DetailAttributes,
  ExecutionLogStepDeepLinkProjection DeepLink,
  ConditionEvaluationTrace? ConditionTrace,
  int? AppliedDelayMs);

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

internal sealed record ExecutionTreeNodeProjection(
  string NodeKind,
  string? ExecutionId,
  int Order,
  string Label,
  string Status,
  string? Message,
  int? AppliedDelayMs,
  string? CommandName,
  WaitForImageDetailAttributes? DetailAttributes,
  ConditionEvaluationTrace? ConditionTrace,
  ExecutionLogStepDeepLinkProjection? DeepLink,
  IReadOnlyList<ExecutionTreeNodeProjection> Children) {
  /// <summary>
  /// When this node corresponds to a recorded execution (queue/sequence/command), the moment it ran.
  /// Null for primitive step nodes, which have no independently recorded execution time.
  /// </summary>
  public DateTimeOffset? TimestampUtc { get; init; }
}

internal sealed record ExecutionSubtreeProjection(
  string ExecutionId,
  string FinalStatus,
  ExecutionTreeNodeProjection Root);

internal interface IExecutionLogService {
  Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, string? parentExecutionId, int depth, CancellationToken ct = default);
  Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, ExecutionLogContext context, CancellationToken ct = default);
  Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, string? parentExecutionId, int depth, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default);
  Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default);
  Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, CancellationToken ct = default);
  Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, ExecutionLogContext parentContext, CancellationToken ct = default);
  Task LogSequenceFinalizeAsync(string executionId, string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default);
  Task<string> LogQueueStartAsync(string queueId, string queueName, CancellationToken ct = default);
  Task LogQueueFinalizeAsync(string executionId, string queueId, string queueName, string finalStatus, string summary, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default);
  Task<ExecutionSubtreeProjection?> GetSubtreeAsync(string executionId, CancellationToken ct = default);
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
      .Select(o => {
        var stepType = string.IsNullOrWhiteSpace(o.StepType) ? "primitiveTap" : o.StepType!;
        var outcome = string.Equals(stepType, "waitForImage", StringComparison.OrdinalIgnoreCase)
          ? o.Status
          : (string.Equals(o.Status, "executed", StringComparison.OrdinalIgnoreCase) ? "executed" : "not_executed");

        return new ExecutionStepOutcome(
          o.StepOrder,
          stepType,
          outcome,
          o.Status,
          o.Reason) {
          DetailAttributes = BuildWaitForImageDetailAttributes(o)
        };
      })
      .ToList();

    var details = new List<ExecutionDetailItem>();
    foreach (var outcome in primitiveOutcomes) {
      if (string.Equals(outcome.StepType, "waitForImage", StringComparison.OrdinalIgnoreCase)) {
        details.Add(new ExecutionDetailItem(
          "step",
          $"Wait for image step ended with {outcome.Reason ?? outcome.Status}.",
          BuildWaitForImageDetailAttributeMap(outcome),
          "normal"));
      }
      else if (string.Equals(outcome.Status, "executed", StringComparison.OrdinalIgnoreCase) && outcome.ResolvedPoint is not null) {
        // Report both the pre-jitter target (ResolvedPoint) and the post-jitter executed point
        // when they differ; keep the original single-point text when no jitter was applied.
        var executed = outcome.ExecutedPoint;
        var text = executed is not null && executed != outcome.ResolvedPoint
          ? $"Tap targeted ({outcome.ResolvedPoint.X},{outcome.ResolvedPoint.Y}), executed at ({executed.X},{executed.Y})."
          : $"Tap executed at ({outcome.ResolvedPoint.X},{outcome.ResolvedPoint.Y}).";
        details.Add(new ExecutionDetailItem(
          "tap",
          text,
          new Dictionary<string, object?> {
            ["x"] = outcome.ResolvedPoint.X,
            ["y"] = outcome.ResolvedPoint.Y,
            ["executedX"] = (executed ?? outcome.ResolvedPoint).X,
            ["executedY"] = (executed ?? outcome.ResolvedPoint).Y,
            ["confidence"] = outcome.DetectionConfidence
          },
          "normal"));
      }
      else if (string.Equals(outcome.Status, "executed", StringComparison.OrdinalIgnoreCase) && outcome.ExecutedSwipe is not null) {
        var target = outcome.TargetSwipe;
        var swipe = outcome.ExecutedSwipe;
        var text = target is not null && target != swipe
          ? $"Swipe targeted ({target.Start.X},{target.Start.Y})->({target.End.X},{target.End.Y}), executed ({swipe.Start.X},{swipe.Start.Y})->({swipe.End.X},{swipe.End.Y})."
          : $"Swipe executed ({swipe.Start.X},{swipe.Start.Y})->({swipe.End.X},{swipe.End.Y}).";
        details.Add(new ExecutionDetailItem(
          "swipe",
          text,
          new Dictionary<string, object?> {
            ["targetX1"] = (target ?? swipe).Start.X,
            ["targetY1"] = (target ?? swipe).Start.Y,
            ["targetX2"] = (target ?? swipe).End.X,
            ["targetY2"] = (target ?? swipe).End.Y,
            ["executedX1"] = swipe.Start.X,
            ["executedY1"] = swipe.Start.Y,
            ["executedX2"] = swipe.End.X,
            ["executedY2"] = swipe.End.Y
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

  public async Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, CancellationToken ct = default) {
    var retention = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    var now = DateTimeOffset.UtcNow;
    var id = Guid.NewGuid().ToString("N");
    var context = new ExecutionLogContext { SequenceId = sequenceId, SequenceLabel = sequenceName };
    var entry = new ExecutionLogEntry {
      Id = id,
      TimestampUtc = now,
      ExecutionType = "sequence",
      FinalStatus = "running",
      ObjectRef = new ExecutionObjectReference("sequence", sequenceId, sequenceName),
      Navigation = ExecutionNavigationBuilder.Build("sequence", sequenceId, context),
      Hierarchy = new ExecutionHierarchyContext(id, null, 0, null),
      Summary = TrimSummary($"Sequence '{sequenceName}' running."),
      RetentionExpiresUtc = retention.Enabled ? now.AddDays(Math.Max(1, retention.RetentionDays)) : DateTimeOffset.MaxValue
    };
    await _repository.AddAsync(entry, ct).ConfigureAwait(false);
    return id;
  }

  public async Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, ExecutionLogContext parentContext, CancellationToken ct = default) {
    var retention = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    var now = DateTimeOffset.UtcNow;
    var id = Guid.NewGuid().ToString("N");
    var context = new ExecutionLogContext { SequenceId = sequenceId, SequenceLabel = sequenceName };
    // Nest the sequence under its parent (e.g. a queue run): parent/root come from the caller,
    // falling back to a self-root when no parent is provided (standalone behavior).
    var hierarchy = new ExecutionHierarchyContext(
      string.IsNullOrWhiteSpace(parentContext?.RootExecutionId) ? id : parentContext!.RootExecutionId!,
      string.IsNullOrWhiteSpace(parentContext?.ParentExecutionId) ? null : parentContext!.ParentExecutionId,
      Math.Max(0, parentContext?.Depth ?? 0),
      parentContext?.SequenceIndex);
    var entry = new ExecutionLogEntry {
      Id = id,
      TimestampUtc = now,
      ExecutionType = "sequence",
      FinalStatus = "running",
      ObjectRef = new ExecutionObjectReference("sequence", sequenceId, sequenceName),
      Navigation = ExecutionNavigationBuilder.Build("sequence", sequenceId, context),
      Hierarchy = hierarchy,
      Summary = TrimSummary($"Sequence '{sequenceName}' running."),
      RetentionExpiresUtc = retention.Enabled ? now.AddDays(Math.Max(1, retention.RetentionDays)) : DateTimeOffset.MaxValue
    };
    await _repository.AddAsync(entry, ct).ConfigureAwait(false);
    return id;
  }

  public async Task LogSequenceFinalizeAsync(string executionId, string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) {
    var retention = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    var existing = await _repository.GetAsync(executionId, ct).ConfigureAwait(false);
    var timestamp = existing?.TimestampUtc ?? DateTimeOffset.UtcNow;
    var sanitizedDetails = ExecutionLogSanitizer.SanitizeDetails(details?.ToList() ?? new List<ExecutionDetailItem>());
    var stepOutcomes = BuildSequenceStepOutcomes(sequenceId, sequenceName, context, sanitizedDetails);
    var trimmedDetails = TrimDetails(sanitizedDetails);
    // Preserve the parent hierarchy established at start (e.g. a sequence nested under a queue run)
    // rather than forcing the entry back to a root.
    var hierarchy = existing?.Hierarchy ?? new ExecutionHierarchyContext(executionId, null, 0, null);

    var entry = new ExecutionLogEntry {
      Id = executionId,
      TimestampUtc = timestamp,
      ExecutionType = "sequence",
      FinalStatus = NormalizeStatus(finalStatus),
      ObjectRef = new ExecutionObjectReference("sequence", sequenceId, sequenceName),
      Navigation = ExecutionNavigationBuilder.Build("sequence", sequenceId, context),
      Hierarchy = hierarchy,
      Summary = TrimSummary(summary),
      Details = trimmedDetails,
      StepOutcomes = stepOutcomes,
      RetentionExpiresUtc = retention.Enabled ? timestamp.AddDays(Math.Max(1, retention.RetentionDays)) : DateTimeOffset.MaxValue
    };

    await _repository.UpsertAsync(entry, ct).ConfigureAwait(false);
  }

  public async Task<string> LogQueueStartAsync(string queueId, string queueName, CancellationToken ct = default) {
    var retention = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    var now = DateTimeOffset.UtcNow;
    var id = Guid.NewGuid().ToString("N");
    var entry = new ExecutionLogEntry {
      Id = id,
      TimestampUtc = now,
      ExecutionType = "queue",
      FinalStatus = "running",
      ObjectRef = new ExecutionObjectReference("queue", queueId, queueName),
      Navigation = ExecutionNavigationBuilder.Build("queue", queueId, new ExecutionLogContext()),
      Hierarchy = new ExecutionHierarchyContext(id, null, 0, null),
      Summary = TrimSummary($"Queue '{queueName}' running."),
      RetentionExpiresUtc = retention.Enabled ? now.AddDays(Math.Max(1, retention.RetentionDays)) : DateTimeOffset.MaxValue
    };
    await _repository.AddAsync(entry, ct).ConfigureAwait(false);
    return id;
  }

  public async Task LogQueueFinalizeAsync(string executionId, string queueId, string queueName, string finalStatus, string summary, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) {
    var retention = await _retentionRepository.GetAsync(ct).ConfigureAwait(false);
    var existing = await _repository.GetAsync(executionId, ct).ConfigureAwait(false);
    var timestamp = existing?.TimestampUtc ?? DateTimeOffset.UtcNow;
    var trimmedDetails = TrimDetails(ExecutionLogSanitizer.SanitizeDetails(details?.ToList() ?? new List<ExecutionDetailItem>()));
    var entry = new ExecutionLogEntry {
      Id = executionId,
      TimestampUtc = timestamp,
      ExecutionType = "queue",
      FinalStatus = NormalizeStatus(finalStatus),
      ObjectRef = new ExecutionObjectReference("queue", queueId, queueName),
      Navigation = ExecutionNavigationBuilder.Build("queue", queueId, new ExecutionLogContext()),
      Hierarchy = existing?.Hierarchy ?? new ExecutionHierarchyContext(executionId, null, 0, null),
      Summary = TrimSummary(summary),
      Details = trimmedDetails,
      RetentionExpiresUtc = retention.Enabled ? timestamp.AddDays(Math.Max(1, retention.RetentionDays)) : DateTimeOffset.MaxValue
    };
    await _repository.UpsertAsync(entry, ct).ConfigureAwait(false);
  }

  public async Task<ExecutionSubtreeProjection?> GetSubtreeAsync(string executionId, CancellationToken ct = default) {
    if (string.IsNullOrWhiteSpace(executionId)) return null;
    var entries = await _repository.GetSubtreeAsync(executionId, ct).ConfigureAwait(false);
    var root = entries.FirstOrDefault(e => string.Equals(e.Id, executionId, StringComparison.Ordinal))
               ?? await _repository.GetAsync(executionId, ct).ConfigureAwait(false);
    if (root is null) return null;

    return BuildSubtree(root, entries);
  }

  internal static ExecutionSubtreeProjection BuildSubtree(ExecutionLogEntry root, IReadOnlyList<ExecutionLogEntry> all) {
    var node = BuildTreeNode(root, all);
    return new ExecutionSubtreeProjection(root.Id, NormalizeStatus(root.FinalStatus), node);
  }

  private static ExecutionTreeNodeProjection BuildTreeNode(ExecutionLogEntry entry, IReadOnlyList<ExecutionLogEntry> all) {
    var nodeKind = (entry.ExecutionType ?? string.Empty).ToLowerInvariant() switch {
      "sequence" => "sequence",
      "queue" => "queue",
      _ => "command"
    };
    var children = new List<ExecutionTreeNodeProjection>();

    var directChildren = all
      .Where(e => string.Equals(e.Hierarchy.ParentExecutionId, entry.Id, StringComparison.Ordinal))
      .OrderBy(e => e.Hierarchy.SequenceIndex ?? int.MaxValue)
      .ThenBy(e => e.TimestampUtc)
      .ToList();
    // Correlate command-backed steps to their linked child execution entries by command id,
    // preserving invocation order so repeated commands map to the right child.
    var childrenByCommandId = directChildren
      .GroupBy(e => e.ObjectRef.ObjectId, StringComparer.Ordinal)
      .ToDictionary(g => g.Key, g => new Queue<ExecutionLogEntry>(g), StringComparer.Ordinal);

    if (entry.StepOutcomes.Count > 0) {
      foreach (var step in entry.StepOutcomes.OrderBy(s => s.StepOrder)) {
        ExecutionLogEntry? childEntry = null;
        if (!string.IsNullOrWhiteSpace(step.CommandId)
            && childrenByCommandId.TryGetValue(step.CommandId!, out var queue)
            && queue.Count > 0) {
          childEntry = queue.Dequeue();
        }

        if (childEntry is not null) {
          var childNode = BuildTreeNode(childEntry, all);
          children.Add(childNode with {
            Order = step.StepOrder,
            Label = string.IsNullOrWhiteSpace(step.CommandName) ? childNode.Label : step.CommandName!,
            Status = MapStepStatus(step.Outcome),
            Message = step.ReasonText ?? step.ReasonCode,
            AppliedDelayMs = step.AppliedDelayMs,
            CommandName = step.CommandName,
            DeepLink = BuildDeepLink(entry, step)
          });
        }
        else {
          children.Add(BuildStepNode(entry, step));
        }
      }

      // Defensive: surface any child executions that were not matched to a step.
      foreach (var leftover in childrenByCommandId.Values.SelectMany(q => q)) {
        children.Add(BuildTreeNode(leftover, all));
      }
    }
    else {
      foreach (var childEntry in directChildren) {
        children.Add(BuildTreeNode(childEntry, all));
      }
    }

    return new ExecutionTreeNodeProjection(
      nodeKind,
      entry.Id,
      entry.Hierarchy.SequenceIndex ?? 0,
      entry.ObjectRef.DisplayNameSnapshot,
      NormalizeStatus(entry.FinalStatus),
      string.IsNullOrWhiteSpace(entry.Summary) ? null : entry.Summary,
      null,
      null,
      null,
      null,
      null,
      children) {
      TimestampUtc = entry.TimestampUtc
    };
  }

  private static ExecutionTreeNodeProjection BuildStepNode(ExecutionLogEntry entry, ExecutionStepOutcome step)
    => new(
      MapStepKind(step.StepType),
      null,
      step.StepOrder,
      ResolveStepLabel(step),
      step.ConditionTrace is not null ? (step.ConditionTrace.FinalResult ? "success" : "failure") : MapStepStatus(step.Outcome),
      step.ReasonText ?? step.ReasonCode,
      step.AppliedDelayMs,
      step.CommandName,
      step.DetailAttributes,
      step.ConditionTrace,
      BuildDeepLink(entry, step),
      Array.Empty<ExecutionTreeNodeProjection>());

  private static string MapStepKind(string? stepType)
    => (stepType ?? string.Empty).ToLowerInvariant() switch {
      "waitforimage" => "wait",
      "condition" => "condition",
      "loop" => "loop",
      "primitivetap" or "tap" => "tap",
      "command" => "command",
      _ => "step"
    };

  private static string MapStepStatus(string? outcome)
    => (outcome ?? string.Empty).ToLowerInvariant() switch {
      "executed" or "success" or "image_detected" or "true" => "success",
      "skipped" => "skipped",
      "not_executed" => "not_executed",
      "running" => "running",
      _ => "failure"
    };

  public Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default) {
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

  internal static ExecutionLogDetailProjection BuildDetailProjection(ExecutionLogEntry entry) {
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

    if (!string.IsNullOrWhiteSpace(entry.Navigation.ParentPath)) {
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

    var storedOrDerivedStepOutcomes = entry.StepOutcomes.Count > 0
      ? entry.StepOutcomes
      : BuildSequenceStepOutcomes(
        entry.ObjectRef.ObjectId,
        entry.ObjectRef.DisplayNameSnapshot,
        new ExecutionLogContext {
          SequenceId = entry.ObjectRef.ObjectId,
          SequenceLabel = entry.ObjectRef.DisplayNameSnapshot
        },
        entry.Details);

    var stepOutcomes = storedOrDerivedStepOutcomes
      .Select(step => new ExecutionLogStepProjection(
        ResolveSequenceId(entry, step),
        ResolveSequenceLabel(entry, step),
        step.StepId,
        ResolveStepLabel(step),
        ResolveStepLabel(step),
        step.CommandName,
        step.StepType,
        step.Outcome,
        step.ReasonText ?? step.ReasonCode ?? "Step completed.",
        step.DetailAttributes,
        BuildDeepLink(entry, step),
        step.ConditionTrace,
        step.AppliedDelayMs))
      .ToArray();

    return new ExecutionLogDetailProjection(
      summary,
      relatedObjects,
      hasSnapshot,
      hasSnapshot ? "Snapshot captured during execution." : null,
      stepOutcomes);
  }

  private static string NormalizeStatus(string status) {
    if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)) return "success";
    if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase)) return "running";
    return "failure";
  }

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
      var resolvedCommandName = TryGetString(attributes, "commandName");
      var resolvedCommandId = TryGetString(attributes, "commandId");
      var appliedDelayMs = TryGetInt(attributes, "appliedDelayMs");
      var conditionTrace = TryGetConditionTrace(attributes, "conditionTrace")
                           ?? BuildConditionTraceFromAttributes(attributes);
      var detailAttributes = BuildWaitForImageDetailAttributes(attributes, stepType, reasonCode);

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
        conditionTrace,
        appliedDelayMs) {
        CommandName = resolvedCommandName,
        CommandId = resolvedCommandId,
        DetailAttributes = detailAttributes
      });
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

  private static double? TryGetDouble(Dictionary<string, object?>? attributes, string key) {
    if (attributes is null || !attributes.TryGetValue(key, out var value) || value is null) {
      return null;
    }

    return value switch {
      double doubleValue => doubleValue,
      float floatValue => floatValue,
      decimal decimalValue => (double)decimalValue,
      JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var parsed) => parsed,
      string text when double.TryParse(text, out var parsed) => parsed,
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

  private static WaitForImageDetailAttributes? BuildWaitForImageDetailAttributes(PrimitiveTapStepOutcome outcome) {
    if (!string.Equals(outcome.StepType, "waitForImage", StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    return new WaitForImageDetailAttributes(
      outcome.TimeoutMs,
      outcome.EffectiveTimeoutMs,
      outcome.ReferenceImageId,
      outcome.ConfiguredConfidence,
      outcome.Reason,
      outcome.ImageLoadStatus);
  }

  private static WaitForImageDetailAttributes? BuildWaitForImageDetailAttributes(
    Dictionary<string, object?>? attributes,
    string? stepType,
    string? reasonCode) {
    if (!string.Equals(stepType, "waitForImage", StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    return new WaitForImageDetailAttributes(
      TryGetInt(attributes, "timeoutMs"),
      TryGetInt(attributes, "effectiveTimeoutMs"),
      TryGetString(attributes, "referenceImageId"),
      TryGetDouble(attributes, "confidence"),
      TryGetString(attributes, "exitCondition") ?? reasonCode,
      TryGetString(attributes, "imageLoadStatus"));
  }

  private static Dictionary<string, object?> BuildWaitForImageDetailAttributeMap(PrimitiveTapStepOutcome outcome) {
    return new Dictionary<string, object?> {
      ["stepOrder"] = outcome.StepOrder,
      ["stepType"] = outcome.StepType,
      ["status"] = outcome.Status,
      ["actionOutcome"] = outcome.Reason,
      ["reasonCode"] = outcome.Reason,
      ["timeoutMs"] = outcome.TimeoutMs,
      ["effectiveTimeoutMs"] = outcome.EffectiveTimeoutMs,
      ["referenceImageId"] = outcome.ReferenceImageId,
      ["confidence"] = outcome.ConfiguredConfidence,
      ["exitCondition"] = outcome.Reason,
      ["imageLoadStatus"] = outcome.ImageLoadStatus
    };
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

  private static ExecutionLogQuery NormalizeQuery(ExecutionLogQuery? query) {
    var source = query ?? new ExecutionLogQuery();

    var sortByRaw = source.SortBy?.Trim();
    var normalizedSortBy = sortByRaw?.ToUpperInvariant() switch {
      "TIMESTAMP" => "timestamp",
      "OBJECTNAME" => "objectName",
      "STATUS" => "status",
      _ => "timestamp"
    };

    var normalizedDirection = string.Equals(source.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
      ? "asc"
      : "desc";

    return new ExecutionLogQuery {
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
      Cursor = source.Cursor,
      RootsOnly = source.RootsOnly
    };
  }
}

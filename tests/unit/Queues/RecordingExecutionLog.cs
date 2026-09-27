using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Logging;
using GameBot.Domain.Services;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// The shared execution-log fake of the queue tests. It was a private nested class of
/// <c>QueueExecutionServiceTests</c>. Feature 106 moved it here, so that the liveness gate and watch
/// tests can use it too, and added the records of the sequence entries and the device fault entries.
/// </summary>
internal sealed class RecordingExecutionLog : IExecutionLogService {
  public int QueueStarts { get; private set; }
  public string? FinalStatus { get; private set; }
  public string? Summary { get; private set; }
  public int QueueFinalizes { get; private set; }
  // Any sequence/command-level log write. Idle-pause must add none (FR-007a/SC-007).
  public int SequenceOrCommandLogCalls { get; private set; }

  /// <summary>
  /// Clock used to stamp queue-root entries (feature 084), so a rotation test can age a run segment
  /// past the 24h bound by advancing the same fake clock the service reads.
  /// </summary>
  public TimeProvider Clock { get; set; } = TimeProvider.System;

  /// <summary>Every rotation the engine performed, oldest first: (closed segment, continuation).</summary>
  public List<(string From, string To)> Rotations { get; } = new();

  /// <summary>The root id the run's terminating finalize targeted.</summary>
  public string? FinalizedExecutionId { get; private set; }

  /// <summary>Feature 106: each sequence entry written with a context (the gate entries of a held firing).</summary>
  public List<(string SequenceId, string FinalStatus, string Summary, ExecutionLogContext Context)> SequenceEntries { get; } = new();

  /// <summary>Feature 106: each device fault entry of the liveness watch.</summary>
  public List<(string RootExecutionId, string QueueId, string Reason)> DeviceFaults { get; } = new();

  private readonly Dictionary<string, DateTimeOffset> _rootStarts = new(StringComparer.Ordinal);

  public Task<string> LogQueueStartAsync(string queueId, string queueName, CancellationToken ct = default) {
    QueueStarts++;
    var id = Guid.NewGuid().ToString("N");
    lock (_rootStarts) { _rootStarts[id] = Clock.GetUtcNow(); }
    return Task.FromResult(id);
  }
  public Task LogQueueFinalizeAsync(string executionId, string queueId, string queueName, string finalStatus, string summary, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) {
    QueueFinalizes++;
    FinalStatus = finalStatus;
    Summary = summary;
    FinalizedExecutionId = executionId;
    return Task.CompletedTask;
  }

  public Task<string> LogQueueRotateAsync(string currentRootId, string queueId, string queueName, CancellationToken ct = default) {
    var continuationId = Guid.NewGuid().ToString("N");
    lock (_rootStarts) {
      _rootStarts[continuationId] = Clock.GetUtcNow();
      Rotations.Add((currentRootId, continuationId));
    }
    return Task.FromResult(continuationId);
  }

  public Task LogQueueDeviceFaultAsync(string rootExecutionId, string queueId, string queueName, string reason, CancellationToken ct = default) {
    lock (DeviceFaults) { DeviceFaults.Add((rootExecutionId, queueId, reason)); }
    return Task.CompletedTask;
  }

  // Unused by the queue engine in these tests.
  public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, string? parentExecutionId, int depth, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
  public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, ExecutionLogContext context, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
  public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, string? parentExecutionId, int depth, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
  public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) {
    SequenceOrCommandLogCalls++;
    lock (SequenceEntries) { SequenceEntries.Add((sequenceId, finalStatus, summary, context)); }
    return Task.CompletedTask;
  }
  public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.FromResult(Guid.NewGuid().ToString("N")); }
  public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, ExecutionLogContext parentContext, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.FromResult(Guid.NewGuid().ToString("N")); }
  public Task LogSequenceFinalizeAsync(string executionId, string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
  public Task<ExecutionSubtreeProjection?> GetSubtreeAsync(string executionId, CancellationToken ct = default) => Task.FromResult<ExecutionSubtreeProjection?>(null);
  public Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default) => Task.FromResult(new ExecutionLogPage(Array.Empty<ExecutionLogEntry>(), null));
  public Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default) {
    DateTimeOffset startedAt;
    lock (_rootStarts) {
      if (!_rootStarts.TryGetValue(id, out startedAt)) return Task.FromResult<ExecutionLogEntry?>(null);
    }
    return Task.FromResult<ExecutionLogEntry?>(new ExecutionLogEntry {
      Id = id,
      TimestampUtc = startedAt,
      ExecutionType = "queue",
      FinalStatus = "running",
      ObjectRef = new ExecutionObjectReference("queue", "q", "Queue"),
      Navigation = new ExecutionNavigationContext("/queues/q", null),
      Hierarchy = new ExecutionHierarchyContext(id, null, 0, null)
    });
  }
  public Task<ExecutionLogRetentionPolicy> GetRetentionAsync(CancellationToken ct = default) => Task.FromResult(new ExecutionLogRetentionPolicy());
  public Task<ExecutionLogRetentionPolicy> UpdateRetentionAsync(bool enabled, int? retentionDays, int? cleanupIntervalMinutes, CancellationToken ct = default) => Task.FromResult(new ExecutionLogRetentionPolicy());
  public Task<int> CleanupExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
}

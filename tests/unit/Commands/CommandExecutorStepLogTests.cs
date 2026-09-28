using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 112 (issue #222): each single step call that passes the session check writes exactly one
/// execution-log entry, also for a timeout, a cancellation and an error. These tests use step types that
/// run on all platforms.
/// </summary>
public sealed class CommandExecutorStepLogTests {
  private static CommandExecutor NewExecutor(StepLogFakeSessions sessions, StepLogRecordingLog log) =>
    new(new SessionResolutionFakeCommands(),
        sessions,
        new SessionResolutionFakeTriggers(),
        new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
        NullLogger<CommandExecutor>.Instance,
        new SessionContextCache(),
        log);

  private static CommandStep KeyStep() => new() {
    Type = CommandStepType.KeyInput,
    Order = 0,
    KeyInput = new KeyInputConfig { Key = "HOME" }
  };

  private static CommandStep LongWaitStep(int timeoutMs) => new() {
    Type = CommandStepType.WaitForImage,
    Order = 0,
    WaitForImage = new WaitForImageConfig { TimeoutMs = timeoutMs }
  };

  [Fact]
  public async Task ForceExecuteStepWritesOneEntryWithTheOutcome() {
    var log = new StepLogRecordingLog();
    var exec = NewExecutor(new StepLogFakeSessions("sess-1"), log);

    var result = await exec.ForceExecuteStepAsync("sess-1", KeyStep(), TimeSpan.FromSeconds(10));

    result.Accepted.Should().Be(1);
    var record = log.Records.Should().ContainSingle().Subject;
    record.SessionId.Should().Be("sess-1");
    record.StepType.Should().Be("KeyInput");
    record.Outcome.Status.Should().Be("executed");
    record.Accepted.Should().Be(1);
    record.StartedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    record.DurationMs.Should().BeGreaterThanOrEqualTo(0);
  }

  [Fact]
  public async Task ForceExecuteStepWithoutSessionIdUsesTheOnlyRunningSessionInTheEntry() {
    var log = new StepLogRecordingLog();
    var exec = NewExecutor(new StepLogFakeSessions("only"), log);

    await exec.ForceExecuteStepAsync(null, KeyStep(), CancellationToken.None);

    log.Records.Should().ContainSingle().Which.SessionId.Should().Be("only");
  }

  [Fact]
  public async Task ForceExecuteStepTimeoutWritesOneTimeoutEntryAndThrowsTimeoutException() {
    var log = new StepLogRecordingLog();
    var exec = NewExecutor(new StepLogFakeSessions("sess-1"), log);

    var act = async () => await exec.ForceExecuteStepAsync("sess-1", LongWaitStep(5000), TimeSpan.FromMilliseconds(100));

    await act.Should().ThrowAsync<TimeoutException>().WithMessage("step_execution_timeout");
    var record = log.Records.Should().ContainSingle().Subject;
    record.StepType.Should().Be("WaitForImage");
    record.Outcome.Status.Should().Be("timeout");
    record.Accepted.Should().Be(0);
  }

  [Fact]
  public async Task ForceExecuteStepCallerCancellationWritesOneCancelledEntry() {
    var log = new StepLogRecordingLog();
    var exec = NewExecutor(new StepLogFakeSessions("sess-1"), log);
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

    var act = async () => await exec.ForceExecuteStepAsync("sess-1", LongWaitStep(5000), TimeSpan.FromSeconds(10), cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
    var record = log.Records.Should().ContainSingle().Subject;
    record.Outcome.Status.Should().Be("cancelled");
    record.Accepted.Should().Be(0);
  }

  [Fact]
  public async Task ForceExecuteStepErrorWritesOneFailedEntryAndRethrows() {
    var log = new StepLogRecordingLog();
    var exec = NewExecutor(new StepLogFakeSessions("sess-1"), log);
    var step = new CommandStep { Type = CommandStepType.Command, TargetId = "cmd-x", Order = 0 };

    var act = async () => await exec.ForceExecuteStepAsync("sess-1", step, TimeSpan.FromSeconds(10));

    await act.Should().ThrowAsync<InvalidOperationException>();
    var record = log.Records.Should().ContainSingle().Subject;
    record.StepType.Should().Be("Command");
    record.Outcome.Status.Should().Be("failed");
    record.Outcome.Reason.Should().Be("step_exception: InvalidOperationException");
  }

  [Fact]
  public async Task ForceExecuteStepLogWriteFailureDoesNotChangeTheResult() {
    var log = new StepLogRecordingLog { FailWrites = true };
    var exec = NewExecutor(new StepLogFakeSessions("sess-1"), log);

    var result = await exec.ForceExecuteStepAsync("sess-1", KeyStep(), TimeSpan.FromSeconds(10));

    result.Accepted.Should().Be(1);
    result.StepOutcomes.Should().ContainSingle().Which.Status.Should().Be("executed");
  }

  [Fact]
  public async Task ForceExecuteStepSessionErrorWritesNoEntry() {
    var log = new StepLogRecordingLog();
    var exec = NewExecutor(new StepLogFakeSessions("sess-1"), log);

    var act = async () => await exec.ForceExecuteStepAsync("unknown", KeyStep(), TimeSpan.FromSeconds(10));

    await act.Should().ThrowAsync<KeyNotFoundException>();
    log.Records.Should().BeEmpty();
  }
}

/// <summary>A session fake whose sessions run and accept each input.</summary>
internal sealed class StepLogFakeSessions : ISessionManager {
  private readonly List<EmulatorSession> _sessions;

  public StepLogFakeSessions(params string[] runningIds) {
    _sessions = runningIds
      .Select(id => new EmulatorSession { Id = id, GameId = "g", Status = SessionStatus.Running, DeviceSerial = $"dev-{id}" })
      .ToList();
  }

  public int ActiveCount => _sessions.Count;
  public bool CanCreateSession => true;
  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
  public EmulatorSession? GetSession(string id) => _sessions.Find(s => s.Id == id);
  public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions;
  public bool StopSession(string id) => _sessions.RemoveAll(s => s.Id == id) > 0;
  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(actions.Count());
  public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));
  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

/// <summary>An execution-log fake that records each single step record. It can fail each write.</summary>
internal sealed class StepLogRecordingLog : IExecutionLogService {
  public List<StepExecutionLogRecord> Records { get; } = new();
  public bool FailWrites { get; init; }

  public Task LogStepExecutionAsync(StepExecutionLogRecord record, CancellationToken ct = default) {
    if (FailWrites) throw new InvalidOperationException("log_write_failed");
    Records.Add(record);
    return Task.CompletedTask;
  }

  public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, string? parentExecutionId, int depth, CancellationToken ct = default) => Task.CompletedTask;
  public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, ExecutionLogContext context, CancellationToken ct = default) => Task.CompletedTask;
  public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, string? parentExecutionId, int depth, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;
  public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;
  public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid().ToString("N"));
  public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, ExecutionLogContext parentContext, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid().ToString("N"));
  public Task LogSequenceFinalizeAsync(string executionId, string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;
  public Task<string> LogQueueStartAsync(string queueId, string queueName, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid().ToString("N"));
  public Task LogQueueFinalizeAsync(string executionId, string queueId, string queueName, string finalStatus, string summary, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;
  public Task<string> LogQueueRotateAsync(string currentRootId, string queueId, string queueName, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid().ToString("N"));
  public Task LogQueueDeviceFaultAsync(string rootExecutionId, string queueId, string queueName, string reason, CancellationToken ct = default) => Task.CompletedTask;
  public Task<ExecutionSubtreeProjection?> GetSubtreeAsync(string executionId, CancellationToken ct = default) => Task.FromResult<ExecutionSubtreeProjection?>(null);
  public Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default) => Task.FromResult(new ExecutionLogPage(Array.Empty<ExecutionLogEntry>(), null));
  public Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<ExecutionLogEntry?>(null);
  public Task<ExecutionLogRetentionPolicy> GetRetentionAsync(CancellationToken ct = default) => throw new NotSupportedException();
  public Task<ExecutionLogRetentionPolicy> UpdateRetentionAsync(bool enabled, int? retentionDays, int? cleanupIntervalMinutes, CancellationToken ct = default) => throw new NotSupportedException();
  public Task<int> CleanupExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
}

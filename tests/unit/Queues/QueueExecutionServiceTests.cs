using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code):
// CA2007 ConfigureAwait, CA1861 constant array args, CA1859 concrete-type params.
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

public sealed class QueueExecutionServiceTests {
  // ── Fakes ─────────────────────────────────────────────────────────────

  private sealed class FakeQueueRepository : IQueueRepository {
    private readonly Dictionary<string, ExecutionQueue> _items = new(StringComparer.Ordinal);
    public void Add(ExecutionQueue q) => _items[q.Id] = q;
    public Task<ExecutionQueue?> GetAsync(string id) => Task.FromResult(_items.TryGetValue(id, out var q) ? q : null);
    public Task<IReadOnlyList<ExecutionQueue>> ListAsync() => Task.FromResult((IReadOnlyList<ExecutionQueue>)_items.Values.ToList());
    public Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) { _items[queue.Id] = queue; return Task.FromResult(queue); }
    public Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) { _items[queue.Id] = queue; return Task.FromResult(queue); }
    public Task<bool> DeleteAsync(string id) => Task.FromResult(_items.Remove(id));
  }

  private sealed class FakeTemplateRepository : IQueueTemplateRepository {
    private readonly Dictionary<string, QueueTemplate> _items = new(StringComparer.Ordinal);
    public int GetCalls { get; private set; }
    public void Add(QueueTemplate t) => _items[t.Id] = t;
    public Task<QueueTemplate?> GetAsync(string id) { GetCalls++; return Task.FromResult(_items.TryGetValue(id, out var t) ? t : null); }
    public Task<IReadOnlyList<QueueTemplate>> ListAsync() => Task.FromResult((IReadOnlyList<QueueTemplate>)_items.Values.ToList());
    public Task<QueueTemplate?> FindByNameAsync(string name) => Task.FromResult<QueueTemplate?>(null);
    public Task<QueueTemplate> CreateAsync(QueueTemplate item) { _items[item.Id] = item; return Task.FromResult(item); }
    public Task<QueueTemplate> UpdateAsync(QueueTemplate item) { _items[item.Id] = item; return Task.FromResult(item); }
    public Task<bool> DeleteAsync(string id) => Task.FromResult(_items.Remove(id));
  }

  private sealed class FakeSequenceExecution : ISequenceExecutionService {
    public List<string> Executed { get; } = new();
    public Func<string, CancellationToken, Task<SequenceExecutionResult>>? Handler { get; set; }

    public Task<SequenceExecutionResult> ExecuteAsync(string sequenceId, string? sessionId, ExecutionLogContext? parentContext, CancellationToken ct = default) {
      lock (Executed) { Executed.Add(sequenceId); }
      if (Handler is not null) return Handler(sequenceId, ct);
      return Task.FromResult(Success(sequenceId));
    }

    public static SequenceExecutionResult Success(string id) { var r = SequenceExecutionResult.Start(id); r.Complete(); return r; }
    public static SequenceExecutionResult Failure(string id) { var r = SequenceExecutionResult.Start(id); r.Fail("failed"); return r; }
  }

  private sealed class FakeSessionManager : ISessionManager {
    private EmulatorSession? _session;
    public bool Connected { get; set; }
    public Exception? CreateThrows { get; set; }
    public List<string> Stopped { get; } = new();
    // Inputs sent via SendInputsAsync, recorded so idle-pause tests can assert a HOME key was sent.
    public List<InputAction> Inputs { get; } = new();
    // When set, SendInputsAsync throws — used to prove backgrounding failure is non-fatal (FR-011).
    public Exception? SendThrows { get; set; }

    public int HomeCount {
      get {
        lock (Inputs) {
          return Inputs.Count(a => a.Type == "key"
            && a.Args.TryGetValue("keyCode", out var v) && v is int k && k == 3);
        }
      }
    }

    public int ActiveCount => _session is null ? 0 : 1;
    public bool CanCreateSession => true;

    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) {
      if (CreateThrows is not null) throw CreateThrows;
      _session = new EmulatorSession { Id = Guid.NewGuid().ToString("N"), GameId = gameIdOrPath, DeviceSerial = preferredDeviceSerial, Status = SessionStatus.Running };
      Connected = true;
      return _session;
    }

    public EmulatorSession? GetSession(string id) => Connected && _session is not null && _session.Id == id ? _session : null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => _session is null ? Array.Empty<EmulatorSession>() : new[] { _session };
    public bool StopSession(string id) { Stopped.Add(id); Connected = false; return true; }
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) {
      var list = actions.ToList();
      lock (Inputs) { Inputs.AddRange(list); }
      if (SendThrows is not null) throw SendThrows;
      return Task.FromResult(list.Count);
    }
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  // Records foreground (resume) calls so idle-pause tests can assert the game is brought back, and
  // captures how many sequences had executed at the first call to prove the foreground precedes the
  // due sequence. Optionally throws to prove foregrounding failure is non-fatal (FR-011).
  private sealed class FakeEnsureGameRunning : IEnsureGameRunningActionHandler {
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public Exception? Throws { get; set; }
    public Func<int>? ExecutedCountProvider { get; set; }
    public int ExecutedCountAtFirstCall { get; private set; } = -1;

    public Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default) {
      if (ExecutedCountAtFirstCall < 0 && ExecutedCountProvider is not null) {
        ExecutedCountAtFirstCall = ExecutedCountProvider();
      }
      Interlocked.Increment(ref _calls);
      if (Throws is not null) throw Throws;
      return Task.FromResult(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning));
    }
  }

  private sealed class RecordingExecutionLog : IExecutionLogService {
    public int QueueStarts { get; private set; }
    public string? FinalStatus { get; private set; }
    public string? Summary { get; private set; }
    public int QueueFinalizes { get; private set; }
    // Any sequence/command-level log write. Idle-pause must add none (FR-007a/SC-007).
    public int SequenceOrCommandLogCalls { get; private set; }

    public Task<string> LogQueueStartAsync(string queueId, string queueName, CancellationToken ct = default) {
      QueueStarts++;
      return Task.FromResult(Guid.NewGuid().ToString("N"));
    }
    public Task LogQueueFinalizeAsync(string executionId, string queueId, string queueName, string finalStatus, string summary, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) {
      QueueFinalizes++;
      FinalStatus = finalStatus;
      Summary = summary;
      return Task.CompletedTask;
    }

    // Unused by the queue engine in these tests.
    public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, string? parentExecutionId, int depth, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
    public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, ExecutionLogContext context, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
    public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, string? parentExecutionId, int depth, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
    public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
    public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.FromResult(Guid.NewGuid().ToString("N")); }
    public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, ExecutionLogContext parentContext, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.FromResult(Guid.NewGuid().ToString("N")); }
    public Task LogSequenceFinalizeAsync(string executionId, string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) { SequenceOrCommandLogCalls++; return Task.CompletedTask; }
    public Task<ExecutionSubtreeProjection?> GetSubtreeAsync(string executionId, CancellationToken ct = default) => Task.FromResult<ExecutionSubtreeProjection?>(null);
    public Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default) => Task.FromResult(new ExecutionLogPage(Array.Empty<ExecutionLogEntry>(), null));
    public Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<ExecutionLogEntry?>(null);
    public Task<ExecutionLogRetentionPolicy> GetRetentionAsync(CancellationToken ct = default) => Task.FromResult(new ExecutionLogRetentionPolicy());
    public Task<ExecutionLogRetentionPolicy> UpdateRetentionAsync(bool enabled, int? retentionDays, int? cleanupIntervalMinutes, CancellationToken ct = default) => Task.FromResult(new ExecutionLogRetentionPolicy());
    public Task<int> CleanupExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
  }

  // ── Harness ───────────────────────────────────────────────────────────

  private sealed class Harness {
    public FakeQueueRepository Queues { get; } = new();
    public FakeTemplateRepository Templates { get; } = new();
    public FakeSequenceExecution Sequences { get; } = new();
    public FakeSessionManager Sessions { get; } = new();
    public RecordingExecutionLog Log { get; } = new();
    public QueueRuntimeStore Runtime { get; } = new();
    public FakeTimeProvider? Clock { get; }
    public QueueRunRegistry Registry { get; } = new();
    public FakeEnsureGameRunning EnsureGame { get; } = new();
    public SelfRescheduleCoordinator Coordinator { get; }
    public QueueExecutionService Service { get; }

    public Harness(FakeTimeProvider? clock = null) {
      Clock = clock;
      Coordinator = new SelfRescheduleCoordinator(Registry, clock);
      Service = new QueueExecutionService(Queues, Runtime, Templates, Sequences, Sessions, Log, NullLogger<QueueExecutionService>.Instance, Registry, timeProvider: clock, ensureGameRunning: EnsureGame);
    }

    public ExecutionQueue AddQueue(string id, IReadOnlyList<string> sequenceIds, bool cycle = false, bool linkTemplate = true) {
      var templateId = $"tpl-{id}";
      var template = new QueueTemplate { Id = templateId, Name = $"T-{id}" };
      foreach (var sid in sequenceIds) template.Entries.Add(new QueueTemplateEntry { SequenceId = sid });
      Templates.Add(template);
      var queue = new ExecutionQueue { Id = id, Name = $"Q-{id}", EmulatorSerial = "emu-1", CycleExecution = cycle, LinkedTemplateId = linkTemplate ? templateId : null };
      Queues.Add(queue);
      return queue;
    }
  }

  private static async Task WaitUntilStoppedAsync(IQueueExecutionService svc, string id, int timeoutMs = 5000) {
    var sw = Stopwatch.StartNew();
    while (svc.IsRunning(id) && sw.ElapsedMilliseconds < timeoutMs) {
      await Task.Delay(10).ConfigureAwait(false);
    }
  }

  private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000) {
    var sw = Stopwatch.StartNew();
    while (!condition() && sw.ElapsedMilliseconds < timeoutMs) {
      await Task.Delay(10).ConfigureAwait(false);
    }
  }

  // ── US1: run to completion ────────────────────────────────────────────

  [Fact] // T012
  public async Task StartRunsLinkedTemplateSequencesInOrder() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B", "C" });

    var outcome = await h.Service.StartAsync("q1");
    outcome.Should().Be(QueueStartOutcome.Started);
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B", "C");
  }

  [Fact] // Regression: a started queue's runtime entries mirror its linked template so GET (and the UI) show them
  public async Task StartMaterializesLinkedTemplateEntriesIntoRuntimeStore() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B", "C" });
    // Block the first sequence so the queue stays running while we inspect its runtime entries.
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A") { await Task.Delay(Timeout.Infinite, ct); }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 1);

    h.Runtime.GetEntries("q1").Select(e => e.SequenceId).Should().Equal("A", "B", "C");

    await h.Service.StopAsync("q1");
  }

  [Fact] // T013
  public async Task CompletedFullRunWritesOneSuccessEntryAndDisconnects() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B" });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Log.QueueStarts.Should().Be(1);
    h.Log.QueueFinalizes.Should().Be(1);
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("completed full run");
    h.Sessions.Stopped.Should().HaveCount(1);
    h.Runtime.GetStatus("q1").Should().Be(QueueExecutionStatus.Stopped);
  }

  // ── US2: stop / guards / concurrency ───────────────────────────────────

  [Fact] // T019
  public async Task StopAbortsPromptlyDisconnectsAndWritesStoppedManually() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B", "C" });
    // First sequence blocks until cancelled; later ones must never run.
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A") { await Task.Delay(Timeout.Infinite, ct); }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 1);

    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Should().Equal("A"); // aborted before B/C
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("stopped manually");
    h.Sessions.Stopped.Should().HaveCount(1);
    h.Service.IsRunning("q1").Should().BeFalse();
    h.Runtime.GetStatus("q1").Should().Be(QueueExecutionStatus.Stopped);
  }

  [Fact] // T019 — teardown resilience: a throwing disconnect still finalizes
  public async Task StopStillFinalizesWhenDisconnectThrows() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    var sessions = new ThrowingDisconnectSessions();
    var service = new QueueExecutionService(h.Queues, h.Runtime, h.Templates, h.Sequences, sessions, h.Log, NullLogger<QueueExecutionService>.Instance, h.Registry);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    h.Log.QueueFinalizes.Should().Be(1);
    h.Runtime.GetStatus("q1").Should().Be(QueueExecutionStatus.Stopped);
    service.IsRunning("q1").Should().BeFalse();
  }

  private sealed class ThrowingDisconnectSessions : ISessionManager {
    private EmulatorSession? _session;
    public int ActiveCount => 1;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) {
      _session = new EmulatorSession { Id = "s", GameId = gameIdOrPath, Status = SessionStatus.Running };
      return _session;
    }
    public EmulatorSession? GetSession(string id) => _session;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => _session is null ? Array.Empty<EmulatorSession>() : new[] { _session };
    public bool StopSession(string id) => throw new InvalidOperationException("disconnect boom");
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  [Fact] // T020
  public async Task StopOnNotRunningIsNoOpAndSecondStartIsRejected() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });

    // No-op stop when not running.
    await h.Service.StopAsync("q1");
    h.Log.QueueFinalizes.Should().Be(0);

    // Block so the queue stays running, then a second start is rejected.
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(Timeout.Infinite, ct); return FakeSequenceExecution.Success(id); };
    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Service.IsRunning("q1"));

    (await h.Service.StartAsync("q1")).Should().Be(QueueStartOutcome.AlreadyRunning);

    await h.Service.StopAsync("q1");
  }

  [Fact] // T020 — concurrent runs on the same emulator are allowed (FR-013/SC-009)
  public async Task TwoQueuesOnSameEmulatorRunConcurrently() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    h.AddQueue("q2", new[] { "B" });
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(Timeout.Infinite, ct); return FakeSequenceExecution.Success(id); };

    (await h.Service.StartAsync("q1")).Should().Be(QueueStartOutcome.Started);
    (await h.Service.StartAsync("q2")).Should().Be(QueueStartOutcome.Started);
    await WaitForAsync(() => h.Service.IsRunning("q1") && h.Service.IsRunning("q2"));

    h.Service.IsRunning("q1").Should().BeTrue();
    h.Service.IsRunning("q2").Should().BeTrue();

    await h.Service.StopAsync("q1");
    await h.Service.StopAsync("q2");
  }

  [Fact] // T-not-found
  public async Task StartUnknownQueueReturnsNotFound() {
    var h = new Harness();
    (await h.Service.StartAsync("nope")).Should().Be(QueueStartOutcome.NotFound);
  }

  // ── US3: cycle ──────────────────────────────────────────────────────────

  [Fact] // T025
  public async Task CycleExecutionRepeatsWithoutReloadingTemplate() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B" }, cycle: true);
    // Pace each sequence so the loop does not spin; honor cancellation.
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(15, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 4); // two full passes
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Take(4).Should().Equal("A", "B", "A", "B");
    h.Templates.GetCalls.Should().Be(1); // template loaded once, reused across cycles
    h.Log.Summary.Should().Contain("stopped manually");
  }

  [Fact] // T026
  public async Task CycleWithEmptyTemplateCompletesWithoutBusyLoop() {
    var h = new Harness();
    h.AddQueue("q1", Array.Empty<string>(), cycle: true);

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().BeEmpty();
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("completed full run");
  }

  // ── US4: failure outcomes ───────────────────────────────────────────────

  [Fact] // T028
  public async Task NoLinkedTemplateFailsWithNoTemplateToRun() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" }, linkTemplate: false);

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().BeEmpty();
    h.Log.FinalStatus.Should().Be("failure");
    h.Log.Summary.Should().Contain("no template to run");
  }

  [Fact] // T029
  public async Task EmulatorUnavailableFailsWithNoSequencesExecuted() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });
    h.Sessions.CreateThrows = new InvalidOperationException("no_adb_devices");

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().BeEmpty();
    h.Log.FinalStatus.Should().Be("failure");
    h.Log.Summary.Should().Contain("emulator could not be reached");
    h.Sessions.Stopped.Should().BeEmpty(); // never connected → nothing to disconnect
  }

  [Fact] // T030
  public async Task PerSequenceFailureIsNonFatalAndRunCompletes() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B", "C" });
    h.Sequences.Handler = (id, ct) => Task.FromResult(
      id == "B" ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B", "C"); // continued past the failure
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("completed full run");
    h.Log.Summary.Should().Contain("1 failed");
  }

  [Fact] // T030 — a throwing (stale/unresolved) sequence is also non-fatal
  public async Task ThrowingSequenceIsNonFatalAndRunCompletes() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B" });
    h.Sequences.Handler = (id, ct) => id == "A"
      ? throw new InvalidOperationException("boom")
      : Task.FromResult(FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact] // T031
  public async Task ConnectionLostMidRunFailsTheRun() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A", "B" });
    // Drop the session right after the first sequence runs.
    h.Sequences.Handler = (id, ct) => {
      if (id == "A") h.Sessions.Connected = false;
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A"); // B never runs
    h.Log.FinalStatus.Should().Be("failure");
    h.Log.Summary.Should().Contain("connection lost");
  }

  // ── US2 (spec): EveryStep scheduling ─────────────────────────────────────

  private static ExecutionQueue AddQueueWithEntries(Harness h, string id, QueueTemplateEntry[] entries, bool cycle = false, bool pauseWhenIdle = false, int idleThresholdSeconds = 30) {
    var templateId = $"tpl-{id}";
    var template = new QueueTemplate { Id = templateId, Name = $"T-{id}" };
    foreach (var e in entries) template.Entries.Add(e);
    h.Templates.Add(template);
    var queue = new ExecutionQueue { Id = id, Name = $"Q-{id}", EmulatorSerial = "emu-1", CycleExecution = cycle, LinkedTemplateId = templateId, PauseWhenIdle = pauseWhenIdle, IdleThresholdSeconds = idleThresholdSeconds };
    h.Queues.Add(queue);
    return queue;
  }

  private static QueueTemplateEntry OncePerRun(string id) => new() { SequenceId = id, ScheduleType = ScheduleType.OncePerRun };
  private static QueueTemplateEntry EveryStep(string id) => new() { SequenceId = id, ScheduleType = ScheduleType.EveryStep };
  private static QueueTemplateEntry AtQueueStart(string id) => new() { SequenceId = id, ScheduleType = ScheduleType.AtQueueStart };
  private static QueueTemplateEntry TimerEntry(string id, TimeOnly time) => new() { SequenceId = id, ScheduleType = ScheduleType.Timer, TimerTimeOfDay = time };
  private static QueueTemplateEntry RelativeTimer(string id, TimeSpan offset) => new() { SequenceId = id, ScheduleType = ScheduleType.Timer, TimerRelativeOffset = offset };

  private static readonly DateTimeOffset FakeStart = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public async Task EveryStepRunsAfterEachOncePerRunStepAndAfterLast() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B"), EveryStep("C") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-006/FR-007: C fires after A and after B
    h.Sequences.Executed.Should().Equal("A", "C", "B", "C");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task EveryStepDoesNotCountTowardExecutedInSummary() {
    var h = new Harness();
    // 2 OncePerRun + 1 EveryStep; executed count should be 2
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B"), EveryStep("C") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-008/SC-002: summary reflects 2 sequences, not 4
    h.Log.Summary.Should().Contain("2 sequence(s) executed");
    h.Log.Summary.Should().NotContain("4 sequence");
  }

  [Fact]
  public async Task NoOncePerRunOnlyEveryStepRunsOnce() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { EveryStep("C") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-009: EveryStep runs exactly once when there are no OncePerRun entries
    h.Sequences.Executed.Should().Equal("C");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task MultipleEveryStepRunInTemplateOrderAfterEachStep() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), EveryStep("C1"), EveryStep("C2") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "C1", "C2");
  }

  [Fact]
  public async Task EveryStepFailureIsNonFatalRunContinues() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B"), EveryStep("C") });
    h.Sequences.Handler = (id, ct) =>
      Task.FromResult(id == "C" ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "C", "B", "C");
    h.Log.FinalStatus.Should().Be("success"); // FR-010: EveryStep failure is non-fatal
    h.Log.Summary.Should().Contain("2 failed");
  }

  [Fact]
  public async Task EveryStepAlsoRunsInCyclicMode() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), EveryStep("C") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(10, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 4); // two full cycles: A,C,A,C
    await h.Service.StopAsync("q1");

    var first4 = h.Sequences.Executed.Take(4).ToList();
    first4.Should().Equal("A", "C", "A", "C");
  }

  // ── US3 (spec): Timer scheduling ─────────────────────────────────────────

  [Fact]
  public async Task TimerPastDueFiresBeforeOncePerRunOnFirstIteration() {
    var h = new Harness();
    // TimeOnly.MinValue (00:00) has always passed today
    AddQueueWithEntries(h, "q1", new[] { TimerEntry("T", TimeOnly.MinValue), OncePerRun("A") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-011/FR-016: T fires first (at iteration boundary), then A
    h.Sequences.Executed[0].Should().Be("T");
    h.Sequences.Executed[1].Should().Be("A");
  }

  [Fact]
  public async Task TimerNotYetDueNeverFiresInNonCyclicRun() {
    var h = new Harness();
    // 23:59 has not yet passed (test doesn't run at midnight)
    AddQueueWithEntries(h, "q1", new[] { TimerEntry("T", new TimeOnly(23, 59)), OncePerRun("A") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-012/SC-004: T never executes
    h.Sequences.Executed.Should().Equal("A");
    h.Sequences.Executed.Should().NotContain("T");
  }

  [Fact]
  public async Task TimerFiresAtMostOncePerCalendarDayAcrossCycles() {
    var h = new Harness();
    // Timer always due; with 3 cycles it must fire exactly once (once per calendar day)
    AddQueueWithEntries(h, "q1", new[] { TimerEntry("T", TimeOnly.MinValue), OncePerRun("A") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(10, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 4); // T + A + A + A (at minimum)
    await h.Service.StopAsync("q1");

    // T must appear exactly once (fired on first iteration, skipped on subsequent same-day iterations)
    h.Sequences.Executed.Count(id => id == "T").Should().Be(1);
  }

  [Fact]
  public async Task MultipleSimultaneousDueTimersAllFireBeforeRegularSteps() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      TimerEntry("T1", TimeOnly.MinValue),
      TimerEntry("T2", TimeOnly.MinValue),
      OncePerRun("A")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-013/SC-005: T1 and T2 both fire, then A; their relative order is unspecified
    h.Sequences.Executed.Should().Contain("T1");
    h.Sequences.Executed.Should().Contain("T2");
    h.Sequences.Executed.Should().Contain("A");
    // Both timers execute before A
    var t1Idx = h.Sequences.Executed.IndexOf("T1");
    var t2Idx = h.Sequences.Executed.IndexOf("T2");
    var aIdx = h.Sequences.Executed.IndexOf("A");
    t1Idx.Should().BeLessThan(aIdx);
    t2Idx.Should().BeLessThan(aIdx);
  }

  [Fact]
  public async Task TimerFailureIsNonFatalRunContinues() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { TimerEntry("T", TimeOnly.MinValue), OncePerRun("A") });
    h.Sequences.Handler = (id, ct) =>
      Task.FromResult(id == "T" ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Contain("T");
    h.Sequences.Executed.Should().Contain("A");
    h.Log.FinalStatus.Should().Be("success"); // FR-015: timer failure non-fatal
  }

  [Fact]
  public async Task TimerAndEveryStepCombinedOrderIsCorrect() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      TimerEntry("T", TimeOnly.MinValue),
      OncePerRun("A"),
      EveryStep("C")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-016: T fires first (timer boundary), then A (OncePerRun), then C (EveryStep)
    h.Sequences.Executed.Should().Equal("T", "A", "C");
  }

  // ── US1 (feature 059): relative-offset timers ────────────────────────────

  [Fact] // T007 — zero offset fires at first boundary and COUNTS toward executed (FR-016a)
  public async Task RelativeTimerZeroOffsetFiresAtFirstBoundaryAndCountsTowardExecuted() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { RelativeTimer("T", TimeSpan.Zero), OncePerRun("A") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // Relative timer fires before the OncePerRun step, then A.
    h.Sequences.Executed.Should().Equal("T", "A");
    // FR-016a: both the relative firing and the once-per-run step count → 2 executed.
    h.Log.Summary.Should().Contain("2 sequence(s) executed");
  }

  [Fact] // T007 — does not fire before the offset elapses, then fires exactly once after
  public async Task RelativeTimerFiresOncePerRunOnlyAfterOffsetElapses() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { RelativeTimer("T", TimeSpan.FromMinutes(10)), OncePerRun("A") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(10, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    // Several cycles elapse with the clock un-advanced: T must NOT fire yet (FR-005 anchor).
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    h.Sequences.Executed.Should().NotContain("T");

    // Advance past the offset → T fires at the next boundary, exactly once.
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitForAsync(() => h.Sequences.Executed.Contains("T"));
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 5);
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Count(id => id == "T").Should().Be(1);
  }

  [Fact] // T007 — recomputes fresh on every run (fires again on a second run)
  public async Task RelativeTimerRecomputesOnEachRun() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { RelativeTimer("T", TimeSpan.Zero), OncePerRun("A") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");
    h.Sequences.Executed.Should().Equal("T", "A");

    // A fresh run re-anchors to the new run start and fires again.
    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");
    h.Sequences.Executed.Should().Equal("T", "A", "T", "A");
  }

  [Fact] // T007 — a failed relative firing is non-fatal, counted in failed, run continues (FR-016)
  public async Task RelativeTimerFailureIsNonFatalAndStillCounts() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { RelativeTimer("T", TimeSpan.Zero), OncePerRun("A") });
    h.Sequences.Handler = (id, ct) =>
      Task.FromResult(id == "T" ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("T", "A"); // run continued past the failure
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("2 sequence(s) executed"); // FR-016a: counted even though it failed
    h.Log.Summary.Should().Contain("1 failed");
  }

  // ── US2 (feature 059): live relative scheduling ──────────────────────────

  [Fact] // T013 — scheduling against a queue with no active run is rejected
  public void ScheduleRelativeWhenNotRunningReturnsNotRunning() {
    var h = new Harness();
    h.AddQueue("q1", new[] { "A" });

    var result = h.Service.ScheduleRelative("q1", "L", TimeSpan.FromMinutes(1));

    result.Outcome.Should().Be(LiveScheduleOutcome.NotRunning);
  }

  [Fact] // T013 — a live schedule fires once after its offset, counts toward executed, then clears
  public async Task LiveScheduleFiresOnceAfterOffsetAndCountsTowardExecuted() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(10, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Service.IsRunning("q1"));

    var result = h.Service.ScheduleRelative("q1", "L", TimeSpan.FromMinutes(5));
    result.Outcome.Should().Be(LiveScheduleOutcome.Scheduled);
    result.ExpectedFireAt.Should().Be(FakeStart + TimeSpan.FromMinutes(5));

    // Not due yet.
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    h.Sequences.Executed.Should().NotContain("L");

    // Advance past the offset → L fires once at the next boundary, then is removed.
    clock.Advance(TimeSpan.FromMinutes(5));
    await WaitForAsync(() => h.Sequences.Executed.Contains("L"));
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 5);
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Count(id => id == "L").Should().Be(1);
  }

  [Fact] // T013 — re-scheduling the same sequence replaces the pending one (most-recent-wins, FR-011)
  public async Task LiveScheduleMostRecentWinsPerSequence() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(10, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Service.IsRunning("q1"));

    // First a far-future schedule, then replace it with an immediately-due one.
    h.Service.ScheduleRelative("q1", "L", TimeSpan.FromHours(12));
    h.Service.ScheduleRelative("q1", "L", TimeSpan.Zero);

    await WaitForAsync(() => h.Sequences.Executed.Contains("L"));
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 4);
    await h.Service.StopAsync("q1");

    // The 12-hour schedule was replaced; L fired once (from the zero-offset re-schedule).
    h.Sequences.Executed.Count(id => id == "L").Should().Be(1);
  }

  [Fact] // T013 — a failed live firing is non-fatal and the run continues (FR-016)
  public async Task LiveScheduleFailedFiringIsNonFatalAndRunContinues() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => {
      await Task.Delay(10, ct);
      return id == "L" ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Service.IsRunning("q1"));
    h.Service.ScheduleRelative("q1", "L", TimeSpan.Zero);

    await WaitForAsync(() => h.Sequences.Executed.Contains("L"));
    // The run keeps going after the failed firing.
    var aAfterFailure = h.Sequences.Executed.Count(id => id == "A");
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") > aAfterFailure);
    h.Service.IsRunning("q1").Should().BeTrue();

    await h.Service.StopAsync("q1");
    h.Sequences.Executed.Count(id => id == "L").Should().Be(1);
  }

  // ── T025: relative-timer edge cases ──────────────────────────────────────

  [Fact] // feature 059 fix: a non-cyclic run stays alive waiting for a pending relative timer; an
         // offset that never elapses before the run is stopped never fires.
  public async Task RelativeTimerThatNeverElapsesNeverFiresWhenStoppedFirst() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { RelativeTimer("T", TimeSpan.FromHours(12)), OncePerRun("A") });

    await h.Service.StartAsync("q1");
    // A runs immediately; the run then waits for the 12h timer rather than completing.
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    h.Service.IsRunning("q1").Should().BeTrue();
    h.Sequences.Executed.Should().NotContain("T");

    await h.Service.StopAsync("q1");
    h.Sequences.Executed.Should().Equal("A");
    h.Sequences.Executed.Should().NotContain("T");
    h.Log.Summary.Should().Contain("stopped manually");
  }

  [Fact] // feature 059 fix: a non-cyclic run waits for a relative timer and fires it once the offset
         // elapses, then completes (the original bug: such timers never fired on a non-cyclic queue).
  public async Task NonCyclicRunWaitsForRelativeTimerThenFiresAndCompletes() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) });

    await h.Service.StartAsync("q1");
    // A runs immediately; T is not yet due, so the run stays alive instead of completing.
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    h.Sequences.Executed.Should().NotContain("T");
    h.Service.IsRunning("q1").Should().BeTrue();

    // Once the offset elapses, T fires at the next poll boundary and the run completes.
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Contain("T");
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("2 sequence(s) executed"); // A (once-per-run) + T (relative) = 2
  }

  [Fact] // multiple simultaneously-due relative timers all fire before the regular steps (FR-015)
  public async Task MultipleRelativeTimersAllFireBeforeRegularSteps() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] {
      RelativeTimer("T1", TimeSpan.Zero),
      RelativeTimer("T2", TimeSpan.Zero),
      OncePerRun("A")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("T1", "T2", "A");
  }

  // ── US1 (feature 060): at-queue-start scheduling ─────────────────────────

  [Fact] // T003 — at-queue-start runs before timer evaluation AND before the first OncePerRun step
  public async Task AtQueueStartRunsBeforeTimersAndBeforeFirstOncePerRunStep() {
    var h = new Harness();
    // Timer is past-due (00:00), so it would otherwise fire first at the iteration boundary.
    AddQueueWithEntries(h, "q1", new[] {
      OncePerRun("A"),
      TimerEntry("T", TimeOnly.MinValue),
      AtQueueStart("S")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-003: S runs before the timer and before the OncePerRun step.
    h.Sequences.Executed.Should().Equal("S", "T", "A");
  }

  [Fact] // T003 — multiple at-queue-start entries run in template order
  public async Task MultipleAtQueueStartRunInTemplateOrderBeforeRegularSteps() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      AtQueueStart("S1"),
      AtQueueStart("S2"),
      OncePerRun("A")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-014: at-queue-start entries run in their template order, all before regular steps.
    h.Sequences.Executed.Should().Equal("S1", "S2", "A");
  }

  [Fact] // T003 — each at-queue-start firing counts toward executed
  public async Task AtQueueStartCountsTowardExecuted() {
    var h = new Harness();
    // 2 at-queue-start + 1 once-per-run → executed total should be 3.
    AddQueueWithEntries(h, "q1", new[] {
      AtQueueStart("S1"),
      AtQueueStart("S2"),
      OncePerRun("A")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-015: at-queue-start firings count toward the executed total.
    h.Log.Summary.Should().Contain("3 sequence(s) executed");
  }

  [Fact] // T004 — at-queue-start runs once per run on a cycling queue (not per cycle)
  public async Task AtQueueStartRunsOncePerRunOnCyclingQueue() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { AtQueueStart("S"), OncePerRun("A") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(10, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 3); // several cycles
    await h.Service.StopAsync("q1");

    // FR-004: S fires exactly once even though A cycles repeatedly.
    h.Sequences.Executed.Count(id => id == "S").Should().Be(1);
    h.Sequences.Executed[0].Should().Be("S");
  }

  [Fact] // T004 — a failing at-queue-start sequence is non-fatal
  public async Task AtQueueStartFailureIsNonFatalRunContinues() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { AtQueueStart("S"), OncePerRun("A") });
    h.Sequences.Handler = (id, ct) =>
      Task.FromResult(id == "S" ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-007: the failed at-queue-start firing is counted but the run continues to completion.
    h.Sequences.Executed.Should().Equal("S", "A");
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("2 sequence(s) executed"); // counted even though it failed
    h.Log.Summary.Should().Contain("1 failed");
  }

  [Fact] // T004 — a template with only at-queue-start entries runs them once and completes
  public async Task OnlyAtQueueStartTemplateRunsOnceAndCompletes() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { AtQueueStart("S1"), AtQueueStart("S2") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-008/SC-003: both entries fire once, then the run completes cleanly (no busy-loop).
    h.Sequences.Executed.Should().Equal("S1", "S2");
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("completed full run");
  }

  [Fact] // T004 — only-at-queue-start template completes even when cycling is enabled
  public async Task OnlyAtQueueStartTemplateCompletesEvenWhenCycling() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { AtQueueStart("S") }, cycle: true);

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // No OncePerRun/EveryStep/Timer work means nothing to cycle: S fires once and the run ends.
    h.Sequences.Executed.Should().Equal("S");
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("completed full run");
  }

  // ── US2 (feature 060): "After Every Step" narrow-trigger guarantee ────────

  [Fact] // T008 — EveryStep fires ONLY after OncePerRun steps, never after at-start or timer firings
  public async Task EveryStepFiresOnlyAfterOncePerRunNotAfterAtQueueStartOrTimer() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      AtQueueStart("S"),
      TimerEntry("T", TimeOnly.MinValue),
      OncePerRun("A"),
      EveryStep("C")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // FR-005/FR-006: order is S (at-start), T (timer), A (once-per-run), C (every-step after A only).
    // C must NOT appear right after S or right after T.
    h.Sequences.Executed.Should().Equal("S", "T", "A", "C");
    // EveryStep fired exactly once — only after the single OncePerRun step.
    h.Sequences.Executed.Count(id => id == "C").Should().Be(1);
  }

  // ── Polish (feature 060): regression — unchanged OncePerRun/Timer behavior ─

  [Fact] // T019 — default (no scheduleType) entry behaves as OncePerRun; ordering/counting unchanged
  public async Task DefaultScheduleTypeStillBehavesAsOncePerRun() {
    var h = new Harness();
    // No at-queue-start entries: pure OncePerRun + Timer should behave exactly as before.
    AddQueueWithEntries(h, "q1", new[] { TimerEntry("T", TimeOnly.MinValue), OncePerRun("A"), OncePerRun("B") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // Timer fires at the boundary, then OncePerRun steps in order; only the two steps are counted.
    h.Sequences.Executed.Should().Equal("T", "A", "B");
    h.Log.Summary.Should().Contain("2 sequence(s) executed");
  }

  // ── Feature 065: self-reschedule run-loop draining ───────────────────────

  [Fact] // T022 — OncePerRun reschedule fires before the cycle ends and counts toward executed.
  public async Task SelfRescheduleOncePerRunFiresWithinRunAndCountsExecuted() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") }); // non-cycling → deterministic
    var scheduled = false;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && !scheduled) {
        scheduled = true;
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null);
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // A runs, schedules R, then R fires before the cycle ends (FR-007).
    h.Sequences.Executed.Should().Equal("A", "R");
    h.Log.Summary.Should().Contain("2 sequence(s) executed"); // both count
  }

  [Fact] // T022a — two accepted OncePerRun reschedules produce two independent firings.
  public async Task TwoOncePerRunReschedulesProduceTwoFirings() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") });
    var scheduled = false;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && !scheduled) {
        scheduled = true;
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null);
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null);
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Count(id => id == "R").Should().Be(2);
  }

  [Fact] // T033/T041 — AtQueueStart (cycling) fires at the start of the next cycle.
  public async Task SelfRescheduleAtQueueStartFiresAtNextCycleStart() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") }, cycle: true);
    var scheduled = false;
    h.Sequences.Handler = async (id, ct) => {
      await Task.Delay(10, ct);
      if (id == "A" && !scheduled) {
        scheduled = true;
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.AtQueueStart, null, null);
      }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("R"));
    await h.Service.StopAsync("q1");

    // R fires at the top of cycle 2, i.e. right after the first A and before the second A.
    h.Sequences.Executed.Take(2).Should().Equal("A", "R");
  }

  [Fact] // T032/T040 — EveryStep injection fires after each subsequent step; idempotent (loop-safe).
  public async Task SelfRescheduleEveryStepFiresAfterEachStepAndIsLoopSafe() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") }, cycle: true);
    var scheduled = false;
    h.Sequences.Handler = async (id, ct) => {
      await Task.Delay(10, ct);
      if (id == "A" && !scheduled) {
        scheduled = true;
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.EveryStep, null, null);
      }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "R") >= 2);
    await h.Service.StopAsync("q1");

    // R fires repeatedly (after each A), and the registration never stacked beyond one entry.
    h.Sequences.Executed.Count(id => id == "R").Should().BeGreaterThanOrEqualTo(2);
    h.Registry.TryGet("q1", out _).Should().BeFalse(); // run cleaned up
  }

  [Fact] // T039 — Timer reschedule does not fire before due, then fires once after the offset elapses.
  public async Task SelfRescheduleTimerFiresOnceAfterOffsetElapses() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") }); // non-cycling; stays alive for the timer
    var scheduled = false;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && !scheduled) {
        scheduled = true;
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    // Wait until the Timer firing is actually registered on the run handle before advancing the
    // clock — otherwise, under load, "A" (and thus scheduling) could lag the advance and the
    // resolved FireAt would be computed from the already-advanced clock (ordering race).
    await WaitForAsync(() => h.Registry.TryGet("q1", out var handle) && handle.HasPendingTimerFirings, 10000);
    // Not due yet: the run stays alive but R has not fired.
    h.Service.IsRunning("q1").Should().BeTrue();
    h.Sequences.Executed.Should().NotContain("R");

    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Count(id => id == "R").Should().Be(1);
    h.Log.Summary.Should().Contain("2 sequence(s) executed"); // A + R count
  }

  [Fact] // T049 — a Timer reschedule never due before stop is abandoned; the run is not failed.
  public async Task SelfRescheduleTimerNeverDueIsAbandonedWithoutFailing() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") });
    var scheduled = false;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && !scheduled) {
        scheduled = true;
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromHours(12));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    h.Service.IsRunning("q1").Should().BeTrue(); // stays alive waiting for the 12h timer

    await h.Service.StopAsync("q1"); // stop before the timer becomes due

    h.Sequences.Executed.Should().NotContain("R"); // abandoned, never fired (FR-015)
    h.Log.FinalStatus.Should().Be("success"); // run not marked failed
    h.Log.Summary.Should().Contain("stopped manually");
  }

  // ── Feature 073: idle-pause the game during queue gaps (US1) ──────────────
  // Setup pattern: a non-cyclic queue with one OncePerRun step and a distant relative timer. The
  // step runs immediately; the run then stays alive waiting for the timer, which is exactly the idle
  // gap. The fake clock controls when the timer becomes due, so the pause/resume boundary is
  // deterministic while the poll loop itself runs in real time.

  [Fact] // T006(a) — enabled + gap > threshold: HOME sent once, game foregrounded before the due sequence
  public async Task IdlePauseEnabledBacksGameOutThenForegroundsBeforeDueSequence() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    h.EnsureGame.ExecutedCountProvider = () => h.Sequences.Executed.Count;
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) }, pauseWhenIdle: true);

    await h.Service.StartAsync("q1");
    // A runs, then the run idle-pauses toward the 10-min relative timer: HOME is sent.
    await WaitForAsync(() => h.Sessions.HomeCount >= 1);
    h.Registry.TryGet("q1", out var handle).Should().BeTrue();
    handle.IsIdlePaused.Should().BeTrue();
    handle.IdlePausedUntil.Should().Be(FakeStart + TimeSpan.FromMinutes(10));
    h.EnsureGame.Calls.Should().Be(0); // not resumed yet

    // Advance to the due time → the pause ends, the game is foregrounded, then T fires.
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sessions.HomeCount.Should().Be(1);                   // backed out exactly once
    h.EnsureGame.Calls.Should().Be(1);                     // foregrounded on resume
    h.EnsureGame.ExecutedCountAtFirstCall.Should().Be(1);  // only A had run → foreground precedes T
    h.Sequences.Executed.Should().Equal("A", "T");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact] // T006(b) — gap at or under the threshold: no background/foreground (FR-014)
  public async Task IdlePauseSkippedWhenGapAtOrUnderThreshold() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    // 10s gap vs 30s threshold → too short to pause.
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromSeconds(10)) }, pauseWhenIdle: true, idleThresholdSeconds: 30);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(300); // let several poll cycles run; none should pause
    h.Sessions.HomeCount.Should().Be(0);
    h.EnsureGame.Calls.Should().Be(0);

    clock.Advance(TimeSpan.FromSeconds(10));
    await WaitUntilStoppedAsync(h.Service, "q1");
    h.Sequences.Executed.Should().Equal("A", "T");
    h.Sessions.HomeCount.Should().Be(0);
  }

  [Fact] // T006(c) — PauseWhenIdle false: game untouched during a long gap (FR-009/SC-004)
  public async Task IdlePauseDisabledLeavesGameUntouched() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) }, pauseWhenIdle: false);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(300);
    h.Sessions.HomeCount.Should().Be(0);
    h.EnsureGame.Calls.Should().Be(0);

    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");
    h.Sessions.HomeCount.Should().Be(0);
    h.Sequences.Executed.Should().Equal("A", "T");
  }

  [Fact] // T006(d) — a pause longer than the 4-min watchdog is not cancelled or failed (SC-005)
  public async Task IdlePauseLongerThanWatchdogIsNotCancelled() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    // 25-minute gap, far beyond the 4-min per-sequence watchdog.
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(25)) }, pauseWhenIdle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sessions.HomeCount >= 1);
    clock.Advance(TimeSpan.FromMinutes(25));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "T");
    h.Log.FinalStatus.Should().Be("success"); // never a watchdog failure
  }

  [Fact] // T006(e) — backgrounding and foregrounding failures are non-fatal (FR-011)
  public async Task IdlePauseBackgroundAndForegroundFailuresAreNonFatal() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    h.Sessions.SendThrows = new InvalidOperationException("home boom");
    h.EnsureGame.Throws = new InvalidOperationException("foreground boom");
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) }, pauseWhenIdle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sessions.HomeCount >= 1); // HOME attempted (recorded, then threw)
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.EnsureGame.Calls.Should().Be(1);              // foreground attempted (threw) but non-fatal
    h.Sequences.Executed.Should().Equal("A", "T");  // the due sequence still ran
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact] // T006(f) — stopping during a pause aborts promptly and never foregrounds (SC-006/FR-012)
  public async Task StopDuringIdlePauseAbortsPromptlyWithoutForegrounding() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) }, pauseWhenIdle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sessions.HomeCount >= 1);
    h.Registry.TryGet("q1", out var handle).Should().BeTrue();
    handle.IsIdlePaused.Should().BeTrue();

    await h.Service.StopAsync("q1"); // stop mid-pause

    h.Service.IsRunning("q1").Should().BeFalse();
    h.EnsureGame.Calls.Should().Be(0);   // never foregrounded on stop
    h.Sequences.Executed.Should().Equal("A");
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("stopped manually");
  }

  [Fact] // T006(g) — an idle-pause cycle writes no sequence/command execution-log entries (FR-007a/SC-007)
  public async Task IdlePauseWritesNoExecutionLogEntries() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) }, pauseWhenIdle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sessions.HomeCount >= 1);
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    // Exactly one queue-start + one finalize; entering/holding/leaving the pause logged nothing.
    h.Log.QueueStarts.Should().Be(1);
    h.Log.QueueFinalizes.Should().Be(1);
    h.Log.SequenceOrCommandLogCalls.Should().Be(0);
  }

  [Fact] // T006(h) — resume is decided within one poll interval of the due instant (SC-002)
  public async Task IdlePauseResumesWithinOnePollIntervalOfDueTime() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) }, pauseWhenIdle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sessions.HomeCount >= 1);

    clock.Advance(TimeSpan.FromMinutes(10)); // now due
    // Poll interval is 250 ms; the resume decision (foreground) must land well within a second.
    await WaitForAsync(() => h.EnsureGame.Calls >= 1, 2000);
    h.EnsureGame.Calls.Should().BeGreaterThanOrEqualTo(1);

    await WaitUntilStoppedAsync(h.Service, "q1");
  }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Deterministic projection tests for <see cref="QueueMonitorService"/> (feature 072). The projection
/// is a pure function of (linked template, hand-built run handle, fixed clock), so every schedule kind,
/// ordering rule, current-highlight, cycling marker, nothing-scheduled and last-outcome case is asserted
/// without spinning the queue engine.
/// </summary>
public sealed class QueueMonitorServiceTests {
  private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

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
    public void Add(QueueTemplate t) => _items[t.Id] = t;
    public Task<QueueTemplate?> GetAsync(string id) => Task.FromResult(_items.TryGetValue(id, out var t) ? t : null);
    public Task<IReadOnlyList<QueueTemplate>> ListAsync() => Task.FromResult((IReadOnlyList<QueueTemplate>)_items.Values.ToList());
    public Task<QueueTemplate?> FindByNameAsync(string name) => Task.FromResult<QueueTemplate?>(null);
    public Task<QueueTemplate> CreateAsync(QueueTemplate item) { _items[item.Id] = item; return Task.FromResult(item); }
    public Task<QueueTemplate> UpdateAsync(QueueTemplate item) { _items[item.Id] = item; return Task.FromResult(item); }
    public Task<bool> DeleteAsync(string id) => Task.FromResult(_items.Remove(id));
  }

  private sealed class FakeSequenceRepository : ISequenceRepository {
    private readonly Dictionary<string, CommandSequence> _items = new(StringComparer.Ordinal);
    public void Add(string id, string name) => _items[id] = new CommandSequence { Id = id, Name = name };
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult(_items.TryGetValue(id, out var s) ? s : null);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult((IReadOnlyList<CommandSequence>)_items.Values.ToList());
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) { _items[sequence.Id] = sequence; return Task.FromResult(sequence); }
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) { _items[sequence.Id] = sequence; return Task.FromResult(sequence); }
    public Task<bool> DeleteAsync(string id) => Task.FromResult(_items.Remove(id));
  }

  private sealed class FakeExecutionLog : IExecutionLogService {
    public List<ExecutionLogEntry> Entries { get; } = new();
    public Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default)
      => Task.FromResult(new ExecutionLogPage(Entries, null));

    // Unused by the monitor projection.
    public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, string? parentExecutionId, int depth, CancellationToken ct = default) => Task.CompletedTask;
    public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, ExecutionLogContext context, CancellationToken ct = default) => Task.CompletedTask;
    public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, string? parentExecutionId, int depth, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid().ToString("N"));
    public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, ExecutionLogContext parentContext, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid().ToString("N"));
    public Task LogSequenceFinalizeAsync(string executionId, string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> LogQueueStartAsync(string queueId, string queueName, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid().ToString("N"));
    public Task LogQueueFinalizeAsync(string executionId, string queueId, string queueName, string finalStatus, string summary, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExecutionSubtreeProjection?> GetSubtreeAsync(string executionId, CancellationToken ct = default) => Task.FromResult<ExecutionSubtreeProjection?>(null);
    public Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<ExecutionLogEntry?>(null);
    public Task<ExecutionLogRetentionPolicy> GetRetentionAsync(CancellationToken ct = default) => Task.FromResult(new ExecutionLogRetentionPolicy());
    public Task<ExecutionLogRetentionPolicy> UpdateRetentionAsync(bool enabled, int? retentionDays, int? cleanupIntervalMinutes, CancellationToken ct = default) => Task.FromResult(new ExecutionLogRetentionPolicy());
    public Task<int> CleanupExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
  }

  // ── Harness ───────────────────────────────────────────────────────────

  private sealed class Harness {
    public FakeQueueRepository Queues { get; } = new();
    public FakeTemplateRepository Templates { get; } = new();
    public FakeSequenceRepository Sequences { get; } = new();
    public FakeExecutionLog Log { get; } = new();
    public QueueRunRegistry Registry { get; } = new();
    public FakeTimeProvider Clock { get; } = new(Now);
    public QueueMonitorService Service { get; }

    public Harness() {
      Service = new QueueMonitorService(Registry, Queues, Templates, Sequences, Log, Clock);
    }

    public QueueTemplate Template { get; } = new() { Id = "tpl", Name = "T" };

    public void AddQueue(bool cycle = false, bool linkTemplate = true) {
      Templates.Add(Template);
      Queues.Add(new ExecutionQueue {
        Id = "q1", Name = "Q1", EmulatorSerial = "emu", CycleExecution = cycle,
        LinkedTemplateId = linkTemplate ? "tpl" : null
      });
    }

    public QueueRunHandle StartHandle(bool cycle = false, DateTimeOffset? runStartedAt = null) {
      var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource() };
      handle.CycleExecution = cycle;
      handle.RunStartedAt = runStartedAt ?? Now;
      Registry.TryAdd("q1", handle);
      return handle;
    }

    public void Entry(string sequenceId, string name, ScheduleType type = ScheduleType.OncePerRun, TimeOnly? tod = null, TimeSpan? rel = null) {
      Sequences.Add(sequenceId, name);
      Template.Entries.Add(new QueueTemplateEntry { SequenceId = sequenceId, ScheduleType = type, TimerTimeOfDay = tod, TimerRelativeOffset = rel });
    }
  }

  // ── T011: OncePerRun spine ─────────────────────────────────────────────

  [Fact]
  public async Task OncePerRunSpineIsTemplateOrderWithNextAndUpNextLabels() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Entry("B", "Bravo");
    h.Entry("C", "Charlie");
    h.StartHandle();

    var snap = await h.Service.BuildAsync("q1");

    snap.Running.Should().BeTrue();
    snap.Upcoming.Select(i => i.SequenceId).Should().Equal("A", "B", "C");
    snap.Upcoming.Select(i => i.Reason).Should().AllBe("Once per run");
    snap.Upcoming[0].RelativeLabel.Should().Be("next");
    snap.Upcoming[1].RelativeLabel.Should().Be("up next");
    snap.Upcoming[2].RelativeLabel.Should().Be("up next");
    snap.Upcoming.Select(i => i.Order).Should().Equal(0, 1, 2);
  }

  [Fact]
  public async Task EveryStepIsSurfacedOnceAsAfterEveryStep() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Entry("C", "Chore", ScheduleType.EveryStep);
    h.StartHandle();

    var snap = await h.Service.BuildAsync("q1");

    var everyStep = snap.Upcoming.Where(i => i.ScheduleKind == ScheduleKind.EveryStep).ToList();
    everyStep.Should().ContainSingle();
    everyStep[0].Reason.Should().Be("After Every Step");
  }

  [Fact]
  public async Task TimerTimeOfDayResolvesNextEligibleTodayOrTomorrow() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("Ahead", "Later today", ScheduleType.Timer, tod: new TimeOnly(15, 0));   // now 12:00 → today 15:00
    h.Entry("Passed", "Tomorrow", ScheduleType.Timer, tod: new TimeOnly(9, 0));      // now 12:00 → tomorrow 09:00
    h.StartHandle();

    var snap = await h.Service.BuildAsync("q1");

    var ahead = snap.Upcoming.Single(i => i.SequenceId == "Ahead");
    ahead.ExpectedAt.Should().Be(new DateTimeOffset(2026, 1, 1, 15, 0, 0, TimeSpan.Zero));
    ahead.Reason.Should().Be("At 15:00");
    var passed = snap.Upcoming.Single(i => i.SequenceId == "Passed");
    passed.ExpectedAt.Should().Be(new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero));
  }

  [Fact]
  public async Task TimerRelativeResolvesRunStartPlusOffset() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("R", "Relative", ScheduleType.Timer, rel: TimeSpan.FromMinutes(30));
    h.StartHandle(runStartedAt: Now);

    var snap = await h.Service.BuildAsync("q1");

    var r = snap.Upcoming.Single(i => i.SequenceId == "R");
    r.ScheduleKind.Should().Be(ScheduleKind.TimerRelative);
    r.ExpectedAt.Should().Be(Now + TimeSpan.FromMinutes(30));
  }

  [Fact]
  public async Task LiveScheduleUsesExactFireAt() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Sequences.Add("L", "Live one");
    var handle = h.StartHandle();
    var fireAt = Now + TimeSpan.FromMinutes(5);
    handle.PendingLiveSchedules["L"] = fireAt;

    var snap = await h.Service.BuildAsync("q1");

    var live = snap.Upcoming.Single(i => i.SequenceId == "L");
    live.ScheduleKind.Should().Be(ScheduleKind.LiveSchedule);
    live.Reason.Should().Be("Scheduled live");
    live.ExpectedAt.Should().Be(fireAt);
  }

  [Fact]
  public async Task SelfRescheduleTimerFiringUsesExactFireAt() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Sequences.Add("R", "Rescheduled");
    var handle = h.StartHandle();
    var fireAt = Now + TimeSpan.FromMinutes(7);
    handle.AddTimerFiring(new SelfRescheduleEntry("f1", "R", SelfRescheduleOption.Timer, fireAt));

    var snap = await h.Service.BuildAsync("q1");

    var item = snap.Upcoming.Single(i => i.SequenceId == "R");
    item.ScheduleKind.Should().Be(ScheduleKind.SelfReschedule);
    item.ExpectedAt.Should().Be(fireAt);
  }

  [Fact]
  public async Task TimedItemsAreSortedAscendingByExpectedAt() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha"); // OncePerRun spine keeps the idle "waiting" override from firing
    h.Sequences.Add("L", "Live");
    h.Sequences.Add("R", "Resched");
    h.Entry("T", "Timer", ScheduleType.Timer, tod: new TimeOnly(13, 0)); // today 13:00
    var handle = h.StartHandle();
    handle.PendingLiveSchedules["L"] = Now + TimeSpan.FromMinutes(90); // 13:30
    handle.AddTimerFiring(new SelfRescheduleEntry("f1", "R", SelfRescheduleOption.Timer, Now + TimeSpan.FromMinutes(10))); // 12:10

    var snap = await h.Service.BuildAsync("q1");

    // Only the timed items, in ascending ExpectedAt order: R (12:10) < T (13:00) < L (13:30).
    snap.Upcoming.Where(i => i.ExpectedAt is not null).Select(i => i.SequenceId).Should().Equal("R", "T", "L");
  }

  [Fact]
  public async Task CurrentReflectsCurrentSequenceIdAndIsExcludedFromUpcoming() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Entry("B", "Bravo");
    var handle = h.StartHandle();
    handle.SetCurrentSequence("A", Now);

    var snap = await h.Service.BuildAsync("q1");

    snap.Current.Should().NotBeNull();
    snap.Current!.SequenceId.Should().Be("A");
    snap.Current.RelativeLabel.Should().Be("now");
    snap.Upcoming.Select(i => i.SequenceId).Should().Equal("B"); // A excluded
    snap.Upcoming[0].RelativeLabel.Should().Be("next");
  }

  [Fact]
  public async Task CyclingMarksOncePerRunAndEveryStepAsRepeating() {
    var h = new Harness();
    h.AddQueue(cycle: true);
    h.Entry("A", "Alpha");
    h.Entry("C", "Chore", ScheduleType.EveryStep);
    h.StartHandle(cycle: true);

    var snap = await h.Service.BuildAsync("q1");

    snap.Upcoming.Should().OnlyContain(i => i.Repeats);
  }

  [Fact]
  public async Task NonCyclingLeavesRepeatsFalse() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Entry("C", "Chore", ScheduleType.EveryStep);
    h.StartHandle();

    var snap = await h.Service.BuildAsync("q1");

    snap.Upcoming.Should().OnlyContain(i => !i.Repeats);
  }

  [Fact]
  public async Task StaleSequenceReferenceIsListedWithNullNameAndStaleTrue() {
    var h = new Harness();
    h.AddQueue();
    // Template references "A" but no sequence with that id exists in the library.
    h.Template.Entries.Add(new QueueTemplateEntry { SequenceId = "A", ScheduleType = ScheduleType.OncePerRun });
    h.StartHandle();

    var snap = await h.Service.BuildAsync("q1");

    snap.Upcoming.Should().ContainSingle();
    snap.Upcoming[0].SequenceName.Should().BeNull();
    snap.Upcoming[0].Stale.Should().BeTrue();
  }

  // ── T020: US3 idle / empty / not-running ───────────────────────────────

  [Fact]
  public async Task RunningWithNoWorkOfAnyKindIsNothingScheduled() {
    var h = new Harness();
    h.AddQueue(); // linked template with zero entries
    h.StartHandle();

    var snap = await h.Service.BuildAsync("q1");

    snap.NothingScheduled.Should().BeTrue();
    snap.Upcoming.Should().BeEmpty();
    snap.Current.Should().BeNull();
  }

  [Fact]
  public async Task RunningWithOnlyPendingLiveScheduleIsNotNothingScheduled() {
    var h = new Harness();
    h.AddQueue(); // empty template
    h.Sequences.Add("L", "Live");
    var handle = h.StartHandle();
    handle.PendingLiveSchedules["L"] = Now + TimeSpan.FromMinutes(5);

    var snap = await h.Service.BuildAsync("q1");

    snap.NothingScheduled.Should().BeFalse(); // regression guard: live-only run is not "nothing scheduled"
    snap.Upcoming.Should().ContainSingle(i => i.SequenceId == "L");
  }

  [Fact]
  public async Task LonePendingFutureTimerIsLabelledWaiting() {
    var h = new Harness();
    h.AddQueue(); // no OncePerRun spine
    h.Entry("T", "Timer", ScheduleType.Timer, tod: new TimeOnly(15, 0)); // future today
    h.StartHandle();

    var snap = await h.Service.BuildAsync("q1");

    var timer = snap.Upcoming.Single(i => i.SequenceId == "T");
    timer.ExpectedAt.Should().Be(new DateTimeOffset(2026, 1, 1, 15, 0, 0, TimeSpan.Zero));
    timer.RelativeLabel.Should().Be("waiting");
  }

  [Fact]
  public async Task NotRunningWithPriorFinalizedRunPopulatesLastOutcome() {
    var h = new Harness();
    h.AddQueue();
    // No handle registered → not running.
    h.Log.Entries.Add(new ExecutionLogEntry {
      ExecutionType = "queue",
      FinalStatus = "success",
      Summary = "Queue 'Q1' completed full run: 3 sequence(s) executed.",
      ObjectRef = new ExecutionObjectReference("queue", "q1", "Q1"),
      Navigation = new ExecutionNavigationContext("/x", null),
      Hierarchy = new ExecutionHierarchyContext("root", null, 0, null)
    });

    var snap = await h.Service.BuildAsync("q1");

    snap.Running.Should().BeFalse();
    snap.LastOutcome.Should().NotBeNull();
    snap.LastOutcome!.Status.Should().Be("success");
    snap.LastOutcome.Summary.Should().Contain("completed full run");
  }

  [Fact]
  public async Task NotRunningWithNoHistoryHasNullLastOutcome() {
    var h = new Harness();
    h.AddQueue();

    var snap = await h.Service.BuildAsync("q1");

    snap.Running.Should().BeFalse();
    snap.LastOutcome.Should().BeNull();
  }

  // ── Feature 073: idle-pause current item ──────────────────────────────────

  [Fact] // T011 — an idle-paused handle projects a synthetic "Idle Pause" current item with the resume time
  public async Task IdlePausedHandleProjectsSyntheticCurrentItem() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    var handle = h.StartHandle();
    var resumeAt = Now + TimeSpan.FromMinutes(20);
    handle.EnterIdlePause(resumeAt);

    var snap = await h.Service.BuildAsync("q1");

    snap.Current.Should().NotBeNull();
    snap.Current!.SequenceId.Should().BeEmpty();
    snap.Current.SequenceName.Should().Be("Idle Pause");
    snap.Current.ScheduleKind.Should().Be(ScheduleKind.IdlePause);
    snap.Current.ExpectedAt.Should().Be(resumeAt);
    snap.Current.RelativeLabel.Should().Be("paused");
    snap.Current.Stale.Should().BeFalse();
    snap.Current.Reason.Should().Contain("resumes at");
    // The spine entry still appears as upcoming — the pause is the current item, not the plan.
    snap.Upcoming.Select(i => i.SequenceId).Should().Contain("A");
  }

  [Fact] // T011 — a real executing sequence wins over the idle-pause projection (invariant)
  public async Task RealCurrentSequenceWinsOverIdlePause() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    var handle = h.StartHandle();
    handle.SetCurrentSequence("A", Now);
    handle.EnterIdlePause(Now + TimeSpan.FromMinutes(20)); // ignored while a sequence is executing

    var snap = await h.Service.BuildAsync("q1");

    snap.Current.Should().NotBeNull();
    snap.Current!.SequenceId.Should().Be("A");
    snap.Current.ScheduleKind.Should().NotBe(ScheduleKind.IdlePause);
  }

  [Fact] // T014 — the idle-pause schedule kind serializes to the exact wire string "IdlePause"
  public void IdlePauseScheduleKindWireStringIsExact() {
    ScheduleKind.IdlePause.ToString().Should().Be("IdlePause");
  }
}

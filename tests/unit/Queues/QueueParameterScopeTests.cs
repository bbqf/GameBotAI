using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 078 (US1): the scope a queue run hands to each firing. This is the mechanism that lets one
/// command and one sequence serve N emulator instances — the queue supplies its own serial, so three
/// queues that already differ by serial need no new configuration at all.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  private static readonly string[] SingleDailyEntry = { "Daily" };
  private static readonly string[] TwoEntries = { "A", "B" };

  private static async Task RunAndWaitAsync(Harness h, string queueId) {
    (await h.Service.StartAsync(queueId).ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    var sw = Stopwatch.StartNew();
    while (h.Service.IsRunning(queueId) && sw.ElapsedMilliseconds < 5000) {
      await Task.Delay(10).ConfigureAwait(false);
    }
  }

  [Fact] // FR-010 / SC-002: the motivating case, with zero new configuration.
  public async Task EachQueueSuppliesItsOwnEmulatorSerialToTheSameSequence() {
    var h = new Harness();

    // ONE template and ONE sequence, shared by two queues that differ only by serial.
    var template = new QueueTemplate { Id = "tpl-shared", Name = "Shared" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "Daily" });
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q5558", Name = "PNS 5558", EmulatorSerial = "emulator-5558", LinkedTemplateId = "tpl-shared"
    });
    h.Queues.Add(new ExecutionQueue {
      Id = "q5560", Name = "PNS 5560", EmulatorSerial = "emulator-5560", LinkedTemplateId = "tpl-shared"
    });

    await RunAndWaitAsync(h, "q5558").ConfigureAwait(false);
    await RunAndWaitAsync(h, "q5560").ConfigureAwait(false);

    h.Sequences.Scopes.Should().HaveCount(2);
    Resolve(h, 0, ParameterNameRules.QueueEmulatorSerial).Should().Be("emulator-5558");
    Resolve(h, 1, ParameterNameRules.QueueEmulatorSerial).Should().Be("emulator-5560");
  }

  [Fact] // FR-010: instance name, index and linked game are exposed too.
  public async Task QueueInstanceAndGameAreExposedAsBuiltIns() {
    var h = new Harness();
    var template = new QueueTemplate { Id = "tpl-q", Name = "T" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "Daily" });
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5558", EmulatorInstanceName = "PNS-1",
      EmulatorInstanceIndex = 3, LinkedGameId = "game-7", LinkedTemplateId = "tpl-q"
    });

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    Resolve(h, 0, ParameterNameRules.QueueInstanceName).Should().Be("PNS-1");
    Resolve(h, 0, ParameterNameRules.QueueInstanceIndex).Should().Be("3");
    Resolve(h, 0, ParameterNameRules.QueueGameId).Should().Be("game-7");
  }

  [Fact] // FR-011: an unset queue field is absent from scope, not an empty value.
  public async Task UnsetQueueFieldsAreAbsentFromTheFiringScope() {
    var h = new Harness();
    h.AddQueue("q", SingleDailyEntry);

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    h.Sequences.Scopes[0].Scope.TryResolve(ParameterNameRules.QueueGameId, out _).Should().BeFalse();
    h.Sequences.Scopes[0].Scope.TryResolve(ParameterNameRules.QueueInstanceName, out _).Should().BeFalse();
  }

  [Fact] // FR-012: entry values override the queue built-ins for that entry's firing.
  public async Task TemplateEntryValueOverridesTheQueueBuiltIn() {
    var h = new Harness();
    var template = new QueueTemplate { Id = "tpl-q", Name = "T" };
    var entry = new QueueTemplateEntry { SequenceId = "Daily" };
    entry.ParameterValues.Add(new ParameterBinding {
      Name = ParameterNameRules.QueueEmulatorSerial, Value = "emulator-override"
    });
    template.Entries.Add(entry);
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5558", LinkedTemplateId = "tpl-q"
    });

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    Resolve(h, 0, ParameterNameRules.QueueEmulatorSerial).Should().Be("emulator-override");
  }

  [Fact] // FR-012 / FR-012a: two entries on one sequence hold independent values.
  public async Task TwoEntriesReferencingOneSequenceHoldIndependentValues() {
    var h = new Harness();
    var template = new QueueTemplate { Id = "tpl-q", Name = "T" };
    var first = new QueueTemplateEntry { SequenceId = "Daily" };
    first.ParameterValues.Add(new ParameterBinding { Name = "slot", Value = "one" });
    var second = new QueueTemplateEntry { SequenceId = "Daily" };
    second.ParameterValues.Add(new ParameterBinding { Name = "slot", Value = "two" });
    template.Entries.Add(first);
    template.Entries.Add(second);
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5558", LinkedTemplateId = "tpl-q"
    });

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    h.Sequences.Scopes.Should().HaveCount(2);
    Resolve(h, 0, "slot").Should().Be("one");
    Resolve(h, 1, "slot").Should().Be("two");
  }

  [Fact] // FR-012a: an ad-hoc name the sequence never declares still enters the run scope.
  public async Task AdHocEntryValueEntersTheRunScope() {
    var h = new Harness();
    var template = new QueueTemplate { Id = "tpl-q", Name = "T" };
    var entry = new QueueTemplateEntry { SequenceId = "Daily" };
    entry.ParameterValues.Add(new ParameterBinding { Name = "adbSerial", Value = "emulator-9999" });
    template.Entries.Add(entry);
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5558", LinkedTemplateId = "tpl-q"
    });

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    Resolve(h, 0, "adbSerial").Should().Be("emulator-9999");
  }

  [Fact] // FR-032 / SC-007: a template with no parameter values behaves exactly as before.
  public async Task UnparametrizedRunGetsOnlyTheQueueBuiltIns() {
    var h = new Harness();
    h.AddQueue("q", TwoEntries);

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    h.Sequences.Executed.Should().Equal("A", "B");
    foreach (var (_, scope) in h.Sequences.Scopes) {
      scope.Describe().Should().OnlyContain(e => e.Name.StartsWith("queue.", System.StringComparison.Ordinal));
    }
  }

  private static string? Resolve(Harness h, int firingIndex, string name) =>
      h.Sequences.Scopes[firingIndex].Scope.TryResolve(name, out var value) ? value.Text : null;

  // ── Feature 116 (#249): a run that reschedule-self books keeps the scope of the run that booked it ──

  /// <summary>A template entry with the parameter value <c>slot = <paramref name="value"/></c>.</summary>
  private static QueueTemplateEntry WithSlot(QueueTemplateEntry entry, string value) {
    entry.ParameterValues.Add(new ParameterBinding { Name = "slot", Value = value });
    return entry;
  }

  /// <summary>The scope of the most recent firing, the same scope that the real service gives to <c>ScheduleSelf</c>.</summary>
  private static ParameterScope CurrentScope(Harness h) {
    lock (h.Sequences.Executed) { return h.Sequences.Scopes[^1].Scope; }
  }

  private static int FiringCount(Harness h, string sequenceId) {
    lock (h.Sequences.Executed) { return h.Sequences.Executed.Count(id => id == sequenceId); }
  }

  /// <summary>The scopes of all firings of <paramref name="sequenceId"/>.</summary>
  private static List<ParameterScope> ScopesOf(Harness h, string sequenceId) {
    lock (h.Sequences.Executed) {
      return h.Sequences.Scopes.Where(s => s.SequenceId == sequenceId).Select(s => s.Scope).ToList();
    }
  }

  /// <summary>Asserts that <paramref name="scope"/> resolves <c>slot</c> to <paramref name="expected"/> from the entry layer.</summary>
  private static void ShouldResolveSlotFromEntry(ParameterScope scope, string expected) {
    scope.TryResolve("slot", out var value).Should().BeTrue("the booked run must keep the entry layer");
    value.Text.Should().Be(expected);
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  public static TheoryData<string> SelfRescheduleBookingVariants() => new() {
    "OncePerRun", "EveryStep", "AtQueueStartCycling", "AtQueueStartNotCycling", "TimerOffset", "TimerTimeOfDay"
  };

  [Theory] // T005 (feature 116) — FR-001/FR-002/FR-003: the booked run resolves the entry value, for each variant.
  [MemberData(nameof(SelfRescheduleBookingVariants))]
  public async Task BookedRunResolvesTheEntryValueOfTheRunThatBookedIt(string variant) {
    ArgumentNullException.ThrowIfNull(variant);
    var clock = variant.StartsWith("Timer", StringComparison.Ordinal) ? new FakeTimeProvider(FakeStart) : null;
    var h = new Harness(clock);
    var cycling = variant == "AtQueueStartCycling";
    AddQueueWithEntries(h, "q1", new[] { WithSlot(OncePerRun("A"), "one") }, cycle: cycling);
    var booked = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref booked, 1) == 0) {
        var scope = CurrentScope(h);
        _ = variant switch {
          "OncePerRun" => h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null, scope),
          "EveryStep" => h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.EveryStep, null, null, scope),
          "AtQueueStartCycling" or "AtQueueStartNotCycling" =>
            h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.AtQueueStart, null, null, scope),
          "TimerOffset" => h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10), scope),
          "TimerTimeOfDay" => h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, new TimeOnly(14, 0), null, scope),
          _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown variant.")
        };
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    if (clock is not null) {
      await WaitForAsync(() => h.Registry.TryGet("q1", out var handle) && handle.HasPendingTimerFirings, 10000).ConfigureAwait(false);
      clock.Advance(TimeSpan.FromHours(2)); // 10 minutes and 14:00 (the clock starts at 12:00 UTC) are both due.
    }
    await WaitForAsync(() => FiringCount(h, "R") >= 1, 10000).ConfigureAwait(false);
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    var bookedScopes = ScopesOf(h, "R");
    bookedScopes.Should().NotBeEmpty();
    ShouldResolveSlotFromEntry(bookedScopes[0], "one");
  }

  [Fact] // T005 (feature 116, analyze C1) — a booking from an EveryStep template entry keeps the scope of that entry.
  public async Task BookingFromAnEveryStepEntryKeepsTheScopeOfThatEntry() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), WithSlot(EveryStep("E"), "one") });
    var booked = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "E" && Interlocked.Exchange(ref booked, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null, CurrentScope(h));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await RunAndWaitAsync(h, "q1").ConfigureAwait(false);

    ScopesOf(h, "R").Should().ContainSingle();
    ShouldResolveSlotFromEntry(ScopesOf(h, "R")[0], "one");
  }

  [Fact] // T013 (feature 116) — FR-004/SC-002: each run of a Timer chain resolves the entry value.
  public async Task TimerChainKeepsTheEntryValueForEachGeneration() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { WithSlot(OncePerRun("A"), "one") });
    var firings = 0;
    h.Sequences.Handler = (id, ct) => {
      h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromSeconds(90), CurrentScope(h));
      Interlocked.Increment(ref firings);
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    for (var generation = 1; generation <= 3; generation++) {
      var expected = generation;
      await WaitForAsync(() => Volatile.Read(ref firings) == expected && h.Registry.TryGet("q1", out var handle) && handle.HasPendingTimerFirings, 10000).ConfigureAwait(false);
      Volatile.Read(ref firings).Should().Be(expected);
      clock.Advance(TimeSpan.FromSeconds(90));
    }
    await WaitForAsync(() => Volatile.Read(ref firings) >= 4, 10000).ConfigureAwait(false);
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    var scopes = ScopesOf(h, "A");
    scopes.Should().HaveCountGreaterThanOrEqualTo(4); // the entry run and three booked runs
    foreach (var scope in scopes) ShouldResolveSlotFromEntry(scope, "one");
  }

  [Fact] // T014 (feature 116) — FR-010: of two Timer bookings in one run, the kept one resolves the entry value, and so does its next booking.
  public async Task TwoTimerBookingsInOneRunKeepTheEntryValue() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { WithSlot(OncePerRun("A"), "one") });
    var firings = 0;
    h.Sequences.Handler = (id, ct) => {
      var scope = CurrentScope(h);
      if (Interlocked.Increment(ref firings) == 1) {
        // Step 0 books +15 min, a later step (ocrOffset form) books +5 min again.
        h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(15), scope);
        h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(5), scope);
      }
      else {
        h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(15), scope);
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForAsync(() => Volatile.Read(ref firings) == 1 && h.Registry.TryGet("q1", out var handle) && handle.HasPendingTimerFirings, 10000).ConfigureAwait(false);
    h.Registry.TryGet("q1", out var run).Should().BeTrue();
    run!.SnapshotPendingTimerFirings().Should().ContainSingle()
      .Which.FireAt.Should().Be(clock.GetLocalNow() + TimeSpan.FromMinutes(5));

    clock.Advance(TimeSpan.FromMinutes(5));
    await WaitForAsync(() => Volatile.Read(ref firings) == 2 && h.Registry.TryGet("q1", out var handle) && handle.HasPendingTimerFirings, 10000).ConfigureAwait(false);
    clock.Advance(TimeSpan.FromMinutes(15));
    await WaitForAsync(() => Volatile.Read(ref firings) >= 3, 10000).ConfigureAwait(false);
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    var scopes = ScopesOf(h, "A");
    scopes.Should().HaveCountGreaterThanOrEqualTo(3);
    foreach (var scope in scopes) ShouldResolveSlotFromEntry(scope, "one");
  }

  [Fact] // T014 (feature 116) — the run stops before the later step: the step-0 booking alone keeps the entry value.
  public async Task StepZeroTimerBookingAloneKeepsTheEntryValue() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { WithSlot(OncePerRun("A"), "one") });
    var firings = 0;
    h.Sequences.Handler = (id, ct) => {
      if (Interlocked.Increment(ref firings) == 1) {
        h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(15), CurrentScope(h));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForAsync(() => h.Registry.TryGet("q1", out var handle) && handle.HasPendingTimerFirings, 10000).ConfigureAwait(false);
    clock.Advance(TimeSpan.FromMinutes(15));
    await WaitUntilStoppedAsync(h.Service, "q1", 10000).ConfigureAwait(false);

    var scopes = ScopesOf(h, "A");
    scopes.Should().HaveCount(2);
    ShouldResolveSlotFromEntry(scopes[1], "one");
  }

  [Fact] // T018 (feature 116) — FR-007: a booking with no scope still gets the queue scope only.
  public async Task BookingWithNoScopeStillGetsTheQueueScope() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithSlot(OncePerRun("A"), "one") });
    var booked = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref booked, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null);
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await RunAndWaitAsync(h, "q1").ConfigureAwait(false);

    var scope = ScopesOf(h, "R").Should().ContainSingle().Subject;
    scope.TryResolve("slot", out _).Should().BeFalse();
    scope.Describe().Should().OnlyContain(e => e.Name.StartsWith("queue.", StringComparison.Ordinal));
  }

  [Fact] // T018 (feature 116) — FR-008 regression guard: a template edit after the booking does not change the booked run.
  public async Task TemplateEditAfterTheBookingDoesNotChangeTheBookedRun() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithSlot(OncePerRun("A"), "one") });
    var booked = 0;
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref booked, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null, CurrentScope(h));
        var edited = new QueueTemplate { Id = "tpl-q1", Name = "T-q1" };
        edited.Entries.Add(WithSlot(OncePerRun("A"), "two"));
        await h.Templates.UpdateAsync(edited).ConfigureAwait(false);
      }
      return FakeSequenceExecution.Success(id);
    };

    await RunAndWaitAsync(h, "q1").ConfigureAwait(false);

    var scope = ScopesOf(h, "R").Should().ContainSingle().Subject;
    ShouldResolveSlotFromEntry(scope, "one");
  }

  // ── Feature 196 (#409): a booked run uses the current values of its template entry ──

  /// <summary>
  /// Changes the plan of the run in the same steps as <c>QueueLiveApplyService.ApplyAsync</c>: ask for an operator
  /// pause, wait until the loop is at the gate, swap the plan, and release the pause.
  /// </summary>
  private static async Task ApplyWhilePausedAsync(Harness h, string queueId, Func<QueueRunPlan, QueueRunPlan> change) {
    await WaitForAsync(() => h.Registry.TryGet(queueId, out var running) && running.Schedule is not null, 10000).ConfigureAwait(false);
    h.Registry.TryGet(queueId, out var handle).Should().BeTrue();
    handle!.RequestOperatorPause(h.Clock?.GetLocalNow() ?? DateTimeOffset.Now);
    await WaitForPausedAsync(h, handle).ConfigureAwait(false);
    SwapAndRelease(handle, change);
  }

  /// <summary>Waits until the loop is at the operator gate. With a fake clock, the clock moves in small steps.</summary>
  private static async Task WaitForPausedAsync(Harness h, QueueRunHandle handle) {
    var sw = Stopwatch.StartNew();
    for (var i = 0; handle.OperatorPause != OperatorPauseState.Paused && sw.ElapsedMilliseconds < 10000; i++) {
      if (h.Clock is not null && i % 10 == 9) h.Clock.Advance(TimeSpan.FromMilliseconds(300));
      await Task.Delay(10).ConfigureAwait(false);
    }
    handle.OperatorPause.Should().Be(OperatorPauseState.Paused);
  }

  private static void SwapAndRelease(QueueRunHandle handle, Func<QueueRunPlan, QueueRunPlan> change) {
    var schedule = handle.Schedule!;
    schedule.SwapPlan(change(schedule.Plan));
    handle.ReleaseOperatorPause();
  }

  /// <summary>
  /// A plan with the next version, where the entry with <paramref name="key"/> is a clone with <c>slot</c> =
  /// <paramref name="value"/>. A null value removes all values of the entry.
  /// </summary>
  private static QueueRunPlan WithSlotValue(QueueRunPlan plan, string key, string? value) =>
    ChangeEntry(plan, key, entry => {
      entry.ParameterValues.Clear();
      if (value is not null) entry.ParameterValues.Add(new ParameterBinding { Name = "slot", Value = value });
    });

  /// <summary>A plan with the next version, where the entry with <paramref name="key"/> is a clone that <paramref name="change"/> changed.</summary>
  private static QueueRunPlan ChangeEntry(QueueRunPlan plan, string key, Action<QueueTemplateEntry> change) {
    var entries = plan.AllEntries.Select((entry, i) => {
      if (plan.AllKeys[i] != key) return entry;
      var copy = entry.Clone();
      change(copy);
      return copy;
    }).ToList();
    return QueueRunPlan.FromAll(entries, plan.AllKeys, plan.Version + 1);
  }

  /// <summary>A plan with the next version, with no entry with <paramref name="key"/>.</summary>
  private static QueueRunPlan WithoutEntry(QueueRunPlan plan, string key) {
    var keep = Enumerable.Range(0, plan.AllEntries.Count).Where(i => plan.AllKeys[i] != key).ToList();
    return QueueRunPlan.FromAll(keep.Select(i => plan.AllEntries[i]).ToList(), keep.Select(i => plan.AllKeys[i]).ToList(), plan.Version + 1);
  }

  private static QueueTemplateEntry SlotEntry(QueueTemplateEntry entry, string id, string? value) {
    entry.EntryId = id;
    return value is null ? entry : WithSlot(entry, value);
  }

  private static async Task<QueueRunHandle> WaitForPendingTimerAsync(Harness h, string queueId) {
    await WaitForAsync(() => h.Registry.TryGet(queueId, out var running) && running.HasPendingTimerFirings, 10000).ConfigureAwait(false);
    h.Registry.TryGet(queueId, out var handle).Should().BeTrue();
    handle!.HasPendingTimerFirings.Should().BeTrue();
    return handle;
  }

  /// <summary>
  /// Runs the issue scenario with one Timer booking: the first run of A books <paramref name="bookedSequence"/> with
  /// Timer +15 min, the apply changes the entry, and the clock moves to the fire time. Gives the scopes of the booked runs.
  /// </summary>
  private static async Task<List<ParameterScope>> RunTimerBookingWithApplyAsync(
      Harness h, FakeTimeProvider clock, string bookedSequence, Func<QueueRunPlan, QueueRunPlan> change) {
    var booked = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref booked, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", bookedSequence, SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(15), CurrentScope(h), SelfRescheduleKeep.Earliest);
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForPendingTimerAsync(h, "q1").ConfigureAwait(false);
    await ApplyWhilePausedAsync(h, "q1", change).ConfigureAwait(false);
    var bookedRuns = bookedSequence == "A" ? 2 : 1;
    clock.Advance(TimeSpan.FromMinutes(15));
    await WaitForAsync(() => FiringCount(h, bookedSequence) >= bookedRuns, 10000).ConfigureAwait(false);
    await h.Service.StopAsync("q1").ConfigureAwait(false);
    return ScopesOf(h, bookedSequence);
  }

  [Fact] // T009 (feature 196) — US1-1, FR-002, SC-001: the issue scenario. This test failed before the fix.
  public async Task TimerBookingUsesTheNewEntryValueAfterAnApply() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "one") });

    var scopes = await RunTimerBookingWithApplyAsync(h, clock, "A", plan => WithSlotValue(plan, "e-A", "two"));

    scopes.Should().HaveCount(2);
    ShouldResolveSlotFromEntry(scopes[0], "one");
    ShouldResolveSlotFromEntry(scopes[1], "two");
  }

  public static TheoryData<string> AppliedBookingVariants() => new() {
    "OncePerRun", "EveryStep", "AtQueueStartCycling", "TimerOffset"
  };

  [Theory] // T010 (feature 196) — FR-002: each register uses the new value after an apply.
  [MemberData(nameof(AppliedBookingVariants))]
  public async Task BookingOfEachRegisterUsesTheNewEntryValueAfterAnApply(string variant) {
    ArgumentNullException.ThrowIfNull(variant);
    var clock = variant == "TimerOffset" ? new FakeTimeProvider(FakeStart) : null;
    var h = new Harness(clock);
    // A cycling queue keeps the run alive, so the loop reaches the gate after the booking.
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "one") }, cycle: clock is null);
    var booked = 0;
    Task? apply = null;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref booked, 1) == 0) {
        var scope = CurrentScope(h);
        _ = variant switch {
          "OncePerRun" => h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null, scope),
          "EveryStep" => h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.EveryStep, null, null, scope),
          "AtQueueStartCycling" => h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.AtQueueStart, null, null, scope),
          "TimerOffset" => h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10), scope),
          _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown variant.")
        };
        if (clock is null) {
          h.Registry.TryGet("q1", out var handle).Should().BeTrue();
          handle!.RequestOperatorPause(DateTimeOffset.Now);
          apply = Task.Run(async () => {
            await WaitForPausedAsync(h, handle).ConfigureAwait(false);
            SwapAndRelease(handle, plan => WithSlotValue(plan, "e-A", "two"));
          }, CancellationToken.None);
        }
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    if (clock is not null) {
      await WaitForPendingTimerAsync(h, "q1").ConfigureAwait(false);
      await ApplyWhilePausedAsync(h, "q1", plan => WithSlotValue(plan, "e-A", "two")).ConfigureAwait(false);
      clock.Advance(TimeSpan.FromMinutes(15));
      await WaitForAsync(() => FiringCount(h, "R") >= 1, 10000).ConfigureAwait(false);
    }
    else {
      await WaitForAsync(() => Volatile.Read(ref apply) is not null, 10000).ConfigureAwait(false);
      await apply!.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
      var afterApply = FiringCount(h, "R");
      await WaitForAsync(() => FiringCount(h, "R") > afterApply || (variant != "EveryStep" && afterApply >= 1), 10000).ConfigureAwait(false);
    }
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    var scopes = ScopesOf(h, "R");
    scopes.Should().NotBeEmpty();
    if (variant == "EveryStep") {
      // The every-step pass has no gate, so R can run once before the pause takes effect. The last run uses the new value.
      ShouldResolveSlotFromEntry(scopes[^1], "two");
    }
    else {
      foreach (var scope in scopes) ShouldResolveSlotFromEntry(scope, "two");
    }
  }

  [Fact] // T011 (feature 196) — FR-005, US1-3: the apply does not move a keep-earliest booking.
  public async Task KeepEarliestBookingKeepsItsFireTimeAfterAnApply() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "one") });
    var booked = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref booked, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(15), CurrentScope(h), SelfRescheduleKeep.Earliest);
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    var handle = await WaitForPendingTimerAsync(h, "q1").ConfigureAwait(false);
    var before = handle.SnapshotPendingTimerFirings().Select(f => (f.Id, f.FireAt)).ToList();
    await ApplyWhilePausedAsync(h, "q1", plan => WithSlotValue(plan, "e-A", "two")).ConfigureAwait(false);
    var after = handle.SnapshotPendingTimerFirings().Select(f => (f.Id, f.FireAt)).ToList();
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    before.Should().ContainSingle();
    after.Should().Equal(before);
  }

  [Fact] // T017 (feature 196) — US2, FR-003: each booking of a chain uses the current value.
  public async Task ChainBookingUsesTheNewEntryValueAfterAnApply() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "one") });
    var firings = 0;
    h.Sequences.Handler = (id, ct) => {
      h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromSeconds(90), CurrentScope(h));
      Interlocked.Increment(ref firings);
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForAsync(() => Volatile.Read(ref firings) == 1 && h.Registry.TryGet("q1", out var r) && r.HasPendingTimerFirings, 10000).ConfigureAwait(false);
    clock.Advance(TimeSpan.FromSeconds(90)); // R1 fires and books R2.
    await WaitForAsync(() => Volatile.Read(ref firings) == 2 && h.Registry.TryGet("q1", out var r) && r.HasPendingTimerFirings, 10000).ConfigureAwait(false);
    await ApplyWhilePausedAsync(h, "q1", plan => WithSlotValue(plan, "e-A", "two")).ConfigureAwait(false);
    clock.Advance(TimeSpan.FromSeconds(90)); // R2 fires.
    await WaitForAsync(() => Volatile.Read(ref firings) >= 3, 10000).ConfigureAwait(false);
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    var scopes = ScopesOf(h, "A");
    scopes.Should().HaveCountGreaterThanOrEqualTo(3);
    ShouldResolveSlotFromEntry(scopes[1], "one"); // R1, before the apply
    ShouldResolveSlotFromEntry(scopes[2], "two"); // R2, after the apply
  }

  [Fact] // T018 (feature 196) — US2: the step-0 retry of a failed run uses the new value.
  public async Task StepZeroRetryOfAFailedBookedRunUsesTheNewEntryValue() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "one") });
    var results = new List<bool>();
    h.Sequences.Handler = (id, ct) => {
      var scope = CurrentScope(h);
      h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(15), scope, SelfRescheduleKeep.Earliest);
      var ok = scope.TryResolve("slot", out var slot) && slot.Text != "one";
      lock (results) results.Add(ok);
      return Task.FromResult(ok ? FakeSequenceExecution.Success(id) : FakeSequenceExecution.Failure(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForPendingTimerAsync(h, "q1").ConfigureAwait(false);
    await ApplyWhilePausedAsync(h, "q1", plan => WithSlotValue(plan, "e-A", "two")).ConfigureAwait(false);
    clock.Advance(TimeSpan.FromMinutes(15));
    await WaitForAsync(() => FiringCount(h, "A") >= 2, 10000).ConfigureAwait(false);
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    var scopes = ScopesOf(h, "A");
    scopes.Should().HaveCountGreaterThanOrEqualTo(2);
    ShouldResolveSlotFromEntry(scopes[1], "two");
    lock (results) results.Take(2).Should().Equal(false, true);
  }

  [Fact] // T020 (feature 196) — US3, SC-001: a type change of the parameter does not make the booking fail.
  public async Task BookingAfterAParameterTypeChangeResolvesTheNewNumberValue() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    var entry = OncePerRun("A");
    entry.EntryId = "e-A";
    entry.ParameterValues.Add(new ParameterBinding { Name = "fighterTier", Value = "pns-troop-tier-max" });
    AddQueueWithEntries(h, "q1", new[] { entry });

    var scopes = await RunTimerBookingWithApplyAsync(h, clock, "A", plan => ChangeEntry(plan, "e-A", e => {
      e.ParameterValues.Clear();
      e.ParameterValues.Add(new ParameterBinding { Name = "fighterTier", Value = "0" });
    }));

    scopes.Should().HaveCount(2);
    scopes[1].TryResolve("fighterTier", out var value).Should().BeTrue();
    value.Text.Should().Be("0");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
    int.TryParse(value.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _).Should().BeTrue();
  }

  [Fact] // T022 (feature 196) — US4, FR-006, SC-002: a change of one entry does not touch the bookings of other entries.
  public async Task ApplyOfOneEntryDoesNotChangeTheBookingsOfOtherEntries() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "a1"), SlotEntry(OncePerRun("B"), "e-B", "b1") });
    var bookedA = 0;
    var bookedB = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref bookedA, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10), CurrentScope(h));
      }
      if (id == "B" && Interlocked.Exchange(ref bookedB, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "B", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(20), CurrentScope(h));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForAsync(() => h.Registry.TryGet("q1", out var r) && r.SnapshotPendingTimerFirings().Count == 2, 10000).ConfigureAwait(false);
    h.Registry.TryGet("q1", out var handle).Should().BeTrue();
    var before = handle!.SnapshotPendingTimerFirings().Select(f => (f.Id, f.FireAt)).OrderBy(x => x.Id).ToList();
    await ApplyWhilePausedAsync(h, "q1", plan => WithSlotValue(plan, "e-A", "a2")).ConfigureAwait(false);
    var after = handle.SnapshotPendingTimerFirings().Select(f => (f.Id, f.FireAt)).OrderBy(x => x.Id).ToList();
    clock.Advance(TimeSpan.FromMinutes(20));
    await WaitForAsync(() => FiringCount(h, "A") >= 2 && FiringCount(h, "B") >= 2, 10000).ConfigureAwait(false);
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    before.Should().HaveCount(2);
    after.Should().Equal(before);
    ShouldResolveSlotFromEntry(ScopesOf(h, "A")[1], "a2");
    ShouldResolveSlotFromEntry(ScopesOf(h, "B")[1], "b1");
  }

  [Fact] // T023 (feature 196) — clarification 5: two entries of one sequence each use their own current value.
  public async Task TwoEntriesOfOneSequenceEachUseTheirOwnCurrentValue() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A1", "x1"), SlotEntry(OncePerRun("A"), "e-A2", "y1") });
    var runsOfA = 0;
    h.Sequences.Handler = (id, ct) => {
      // Two Timer bookings of one sequence replace each other, so each entry books a different sequence.
      // The entries run in template order: e-A1 books R1, and e-A2 books R2.
      var run = id == "A" ? Interlocked.Increment(ref runsOfA) : 0;
      if (run is 1 or 2) {
        h.Coordinator.ScheduleSelf("q1", "R" + run, SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10), CurrentScope(h));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    (await h.Service.StartAsync("q1").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForAsync(() => h.Registry.TryGet("q1", out var r) && r.SnapshotPendingTimerFirings().Count == 2, 10000).ConfigureAwait(false);
    await ApplyWhilePausedAsync(h, "q1", plan => WithSlotValue(plan, "e-A1", "x2")).ConfigureAwait(false);
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitForAsync(() => FiringCount(h, "R1") >= 1 && FiringCount(h, "R2") >= 1, 10000).ConfigureAwait(false);
    await h.Service.StopAsync("q1").ConfigureAwait(false);

    ShouldResolveSlotFromEntry(ScopesOf(h, "R1").Should().ContainSingle().Subject, "x2");
    ShouldResolveSlotFromEntry(ScopesOf(h, "R2").Should().ContainSingle().Subject, "y1");
  }

  // ── Feature 196: edge cases (T024) ──

  [Fact] // FR-004: the apply removed the entry, so the booking uses its kept values.
  public async Task BookingOfARemovedEntryUsesItsKeptValues() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "one"), SlotEntry(OncePerRun("B"), "e-B", null) });

    var scopes = await RunTimerBookingWithApplyAsync(h, clock, "R", plan => WithoutEntry(plan, "e-A"));

    ShouldResolveSlotFromEntry(scopes.Should().ContainSingle().Subject, "one");
  }

  [Fact] // Edge case: a disabled entry still gives its current values to its booking.
  public async Task BookingOfADisabledEntryUsesItsCurrentValues() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "one") });

    var scopes = await RunTimerBookingWithApplyAsync(h, clock, "R", plan => ChangeEntry(plan, "e-A", e => {
      e.Enabled = false;
      e.ParameterValues.Clear();
      e.ParameterValues.Add(new ParameterBinding { Name = "slot", Value = "two" });
    }));

    ShouldResolveSlotFromEntry(scopes.Should().ContainSingle().Subject, "two");
  }

  [Fact] // Edge case: the entry had no values at the booking, and the apply added a value.
  public async Task BookingOfAnEntryWithNoValuesGetsTheValuesThatTheApplyAdded() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", null) });

    var scopes = await RunTimerBookingWithApplyAsync(h, clock, "R", plan => WithSlotValue(plan, "e-A", "two"));

    ShouldResolveSlotFromEntry(scopes.Should().ContainSingle().Subject, "two");
  }

  [Fact] // Edge case: the apply removed all values of the entry.
  public async Task BookingAfterTheApplyRemovedAllValuesGetsNoEntryValue() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { SlotEntry(OncePerRun("A"), "e-A", "one") });

    var scopes = await RunTimerBookingWithApplyAsync(h, clock, "R", plan => WithSlotValue(plan, "e-A", null));

    scopes.Should().ContainSingle().Which.TryResolve("slot", out _).Should().BeFalse();
  }

  [Fact] // R-004: a template upgrade renames the key of the entry. Then an apply changes the value under the new key.
  public async Task BookingAfterAKeyRenameUsesTheCurrentValues() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { WithSlot(OncePerRun("A"), "one") }); // no entry ID: the key is idx:0

    var scopes = await RunTimerBookingWithApplyAsync(h, clock, "R", plan => {
      var map = new Dictionary<string, string> { ["idx:0"] = "e-1" };
      var renamed = plan.WithRenamedKeys(map);
      h.Registry.TryGet("q1", out var handle).Should().BeTrue();
      handle!.Schedule!.SwapPlan(renamed, renameMap: map);
      return WithSlotValue(renamed, "e-1", "two");
    });

    var scope = scopes.Should().ContainSingle().Subject;
    ShouldResolveSlotFromEntry(scope, "two");
    scope.EntryKey.Should().Be("e-1");
  }
}

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
}

using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 065: per-option timing resolution and ephemeral register injection.</summary>
public sealed class SelfRescheduleCoordinatorTests {
  private static readonly DateTimeOffset FakeStart = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

  private static (QueueRunRegistry Registry, QueueRunHandle Handle, SelfRescheduleCoordinator Coordinator) Setup(
      bool cycling = false, FakeTimeProvider? clock = null) {
    var registry = new QueueRunRegistry();
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource(), CycleExecution = cycling };
    registry.TryAdd("q1", handle);
    var coordinator = new SelfRescheduleCoordinator(registry, clock);
    return (registry, handle, coordinator);
  }

  // ── US1 ──────────────────────────────────────────────────────────────────

  [Fact] // T021
  public void OncePerRunInjectsIntoPendingOncePerRun() {
    var (_, handle, coordinator) = Setup();

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.OncePerRun, null, null);

    result.Outcome.Should().Be(SelfRescheduleOutcome.Scheduled);
    handle.PendingOncePerRun.Should().ContainSingle();
    handle.PendingOncePerRun.TryPeek(out var entry).Should().BeTrue();
    entry!.SequenceId.Should().Be("seq-A");
    entry.Option.Should().Be(SelfRescheduleOption.OncePerRun);
    result.EntryId.Should().Be(entry.Id);
  }

  [Fact] // T021a
  public void ScheduleAgainstNoActiveRunReturnsNotRunning() {
    var registry = new QueueRunRegistry();
    var coordinator = new SelfRescheduleCoordinator(registry, null);

    var result = coordinator.ScheduleSelf("missing", "seq-A", SelfRescheduleOption.OncePerRun, null, null);

    result.Outcome.Should().Be(SelfRescheduleOutcome.NotRunning);
  }

  [Fact] // T022a (coordinator side): two accepted OncePerRun reschedules accumulate independently.
  public void TwoOncePerRunReschedulesAccumulate() {
    var (_, handle, coordinator) = Setup();

    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.OncePerRun, null, null);
    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.OncePerRun, null, null);

    handle.PendingOncePerRun.Should().HaveCount(2);
  }

  // ── US2: Timer ─────────────────────────────────────────────────────────────

  [Fact] // T030
  public void TimerRelativeOffsetResolvesNowPlusOffset() {
    var clock = new FakeTimeProvider(FakeStart);
    var (_, handle, coordinator) = Setup(clock: clock);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10));

    result.Outcome.Should().Be(SelfRescheduleOutcome.Scheduled);
    result.FireAt.Should().Be(clock.GetLocalNow() + TimeSpan.FromMinutes(10));
    handle.HasPendingTimerFirings.Should().BeTrue();
    handle.DrainDueTimerFirings(clock.GetLocalNow()).Should().BeEmpty(); // not due yet
    handle.DrainDueTimerFirings(clock.GetLocalNow() + TimeSpan.FromMinutes(10)).Should().ContainSingle();
  }

  [Fact] // T030 — offset zero fires at the next boundary (immediately due).
  public void TimerZeroOffsetIsImmediatelyDue() {
    var clock = new FakeTimeProvider(FakeStart);
    var (_, handle, coordinator) = Setup(clock: clock);

    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.Zero);

    handle.DrainDueTimerFirings(clock.GetLocalNow()).Should().ContainSingle();
  }

  [Fact] // T031 — time-of-day already past collapses to now (fires next boundary).
  public void TimerPastTimeOfDayCollapsesToNow() {
    var clock = new FakeTimeProvider(FakeStart); // local 12:00 (or offset)
    var (_, handle, coordinator) = Setup(clock: clock);
    var pastTime = TimeOnly.FromDateTime(clock.GetLocalNow().DateTime).AddHours(-1);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, pastTime, null);

    result.FireAt.Should().BeOnOrBefore(clock.GetLocalNow());
    handle.DrainDueTimerFirings(clock.GetLocalNow()).Should().ContainSingle();
  }

  [Fact] // T031 — future time-of-day fires at that instant, not before.
  public void TimerFutureTimeOfDayFiresAtThatInstant() {
    var clock = new FakeTimeProvider(FakeStart);
    var (_, handle, coordinator) = Setup(clock: clock);
    var futureTime = TimeOnly.FromDateTime(clock.GetLocalNow().DateTime).AddHours(2);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, futureTime, null);

    handle.DrainDueTimerFirings(clock.GetLocalNow()).Should().BeEmpty();
    handle.DrainDueTimerFirings(result.FireAt!.Value).Should().ContainSingle();
  }

  // ── US2: EveryStep ──────────────────────────────────────────────────────────

  [Fact] // T032 — EveryStep is idempotent per sequence (no unbounded self-chain).
  public void EveryStepIsIdempotentPerSequence() {
    var (_, handle, coordinator) = Setup();

    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.EveryStep, null, null);
    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.EveryStep, null, null);

    handle.EveryStepInjections.Should().ContainSingle();
    handle.EveryStepInjections.ContainsKey("seq-A").Should().BeTrue();
  }

  // ── US2: AtQueueStart ───────────────────────────────────────────────────────

  [Fact] // T033 — cycling run → next cycle start register.
  public void AtQueueStartOnCyclingRunGoesToNextCycleStart() {
    var (_, handle, coordinator) = Setup(cycling: true);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.AtQueueStart, null, null);

    result.Outcome.Should().Be(SelfRescheduleOutcome.Scheduled);
    handle.PendingNextCycleStart.Should().ContainSingle();
    handle.PendingOncePerRun.Should().BeEmpty();
  }

  [Fact] // T033 — non-cycling run → falls back to the once-per-run register (next iteration boundary).
  public void AtQueueStartOnNonCyclingRunFallsBackToOncePerRun() {
    var (_, handle, coordinator) = Setup(cycling: false);

    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.AtQueueStart, null, null);

    handle.PendingNextCycleStart.Should().BeEmpty();
    handle.PendingOncePerRun.Should().ContainSingle();
  }
}

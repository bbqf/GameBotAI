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

  // ── Feature 109 (#227): a time of day that has passed books that time on the next day ──────────

  private static readonly TimeZoneInfo UtcPlusTwo =
    TimeZoneInfo.CreateCustomTimeZone("Test+02", TimeSpan.FromHours(2), "Test+02", "Test+02");

  /// <summary>A test zone with the CET/CEST rules: +01:00, and +02:00 in summer.</summary>
  private static TimeZoneInfo CentralEuropean() {
    var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
    var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
    var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
      DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
    return TimeZoneInfo.CreateCustomTimeZone("TestCET", TimeSpan.FromHours(1), "TestCET", "TestCET", "TestCEST", new[] { rule });
  }

  [Fact] // T003 (feature 109) — the probe of issue #227.
  public void TimerPastTimeOfDayBooksNextDay() {
    var now = new DateTimeOffset(2026, 9, 24, 14, 55, 41, TimeSpan.FromHours(2));
    var clock = new FakeTimeProvider(now.ToUniversalTime(), UtcPlusTwo);
    var (_, handle, coordinator) = Setup(clock: clock);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, new TimeOnly(14, 55), null);

    var expected = new DateTimeOffset(2026, 9, 25, 14, 55, 0, TimeSpan.FromHours(2));
    result.FireAt.Should().Be(expected);
    result.FireAt!.Value.Offset.Should().Be(TimeSpan.FromHours(2));
    handle.DrainDueTimerFirings(now).Should().BeEmpty();
    handle.DrainDueTimerFirings(new DateTimeOffset(2026, 9, 24, 23, 59, 59, TimeSpan.FromHours(2))).Should().BeEmpty();
    handle.DrainDueTimerFirings(expected).Should().ContainSingle();
  }

  [Fact] // T004 (feature 109) — a time of day equal to now is not ahead any more.
  public void TimerTimeOfDayEqualToNowBooksNextDay() {
    var clock = new FakeTimeProvider(FakeStart); // local 12:00:00 (UTC)
    var (_, _, coordinator) = Setup(clock: clock);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, new TimeOnly(12, 0), null);

    result.FireAt.Should().Be(new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero));
  }

  [Theory] // T005 (feature 109) — the first and the last second of the day.
  [InlineData(0, 0, 0)]
  [InlineData(23, 59, 59)]
  public void TimerTimeOfDayAtDayEdgesBooksNextDay(int hour, int minute, int second) {
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, hour, minute, second, TimeSpan.Zero));
    var (_, _, coordinator) = Setup(clock: clock);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, new TimeOnly(hour, minute, second), null);

    result.FireAt.Should().Be(new DateTimeOffset(2026, 1, 2, hour, minute, second, TimeSpan.Zero));
  }

  [Fact] // T006 (feature 109) — the clock goes back between today and the next day.
  public void TimerNextDayUsesOffsetOfThatDay() {
    var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));
    var clock = new FakeTimeProvider(now.ToUniversalTime(), CentralEuropean());
    var (_, _, coordinator) = Setup(clock: clock);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, new TimeOnly(11, 0), null);

    result.FireAt.Should().Be(new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.FromHours(1)));
    result.FireAt!.Value.Offset.Should().Be(TimeSpan.FromHours(1));
  }

  [Fact] // T006 (feature 109) — a next-day clock time in the gap when the clock goes forward.
  public void TimerNextDayInClockGapUsesStandardOffset() {
    var now = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.FromHours(1));
    var clock = new FakeTimeProvider(now.ToUniversalTime(), CentralEuropean());
    var (_, _, coordinator) = Setup(clock: clock);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, new TimeOnly(2, 30), null);

    result.FireAt.Should().Be(new DateTimeOffset(2026, 3, 29, 2, 30, 0, TimeSpan.FromHours(1)));
    result.FireAt!.Value.Offset.Should().Be(TimeSpan.FromHours(1));
  }

  [Fact] // T010 (feature 109) — a time of day that is still ahead keeps today (the probe at 14:52:11).
  public void TimerFutureTimeOfDayKeepsToday() {
    var now = new DateTimeOffset(2026, 9, 24, 14, 52, 11, TimeSpan.FromHours(2));
    var clock = new FakeTimeProvider(now.ToUniversalTime(), UtcPlusTwo);
    var (_, _, coordinator) = Setup(clock: clock);

    var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, new TimeOnly(14, 55), null);

    result.FireAt.Should().Be(new DateTimeOffset(2026, 9, 24, 14, 55, 0, TimeSpan.FromHours(2)));
    result.FireAt!.Value.Offset.Should().Be(TimeSpan.FromHours(2));
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

  [Fact] // T003 (feature 075) — two Timer reschedules of the same sequence: the latest replaces the earlier.
  public void TwoTimerReschedulesReplacePreviousBySequence() {
    var clock = new FakeTimeProvider(FakeStart);
    var (_, handle, coordinator) = Setup(clock: clock);

    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10));
    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(30));

    // The earlier (10-min) firing was replaced, so nothing is due at +10min...
    handle.DrainDueTimerFirings(clock.GetLocalNow() + TimeSpan.FromMinutes(10)).Should().BeEmpty();
    // ...and exactly the later (30-min) firing drains at +30min.
    var due = handle.DrainDueTimerFirings(clock.GetLocalNow() + TimeSpan.FromMinutes(30));
    due.Should().ContainSingle();
    due[0].SequenceId.Should().Be("seq-A");
  }

  [Fact] // T008 (feature 075) — Timer dedup does NOT touch the OncePerRun register (FR-004).
  public void TimerRescheduleDoesNotAffectOncePerRunRegister() {
    var clock = new FakeTimeProvider(FakeStart);
    var (_, handle, coordinator) = Setup(clock: clock);

    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.OncePerRun, null, null);
    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10));
    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(30));

    // The Timer dedup collapsed the two Timer firings to one, but the OncePerRun entry is untouched.
    handle.PendingOncePerRun.Should().ContainSingle();
    handle.SnapshotPendingTimerFirings().Should().ContainSingle();
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

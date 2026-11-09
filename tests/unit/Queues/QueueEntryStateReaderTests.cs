using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 184: one test for each row of the table "State of an entry by schedule type" in
/// <c>specs/184-showdown-run-next/data-model.md</c>.
/// </summary>
public sealed class QueueEntryStateReaderTests {
  private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

  private static QueueTemplateEntry Entry(string id, ScheduleType type, TimeOnly? tod = null, TimeSpan? rel = null, bool enabled = true) =>
    new() { EntryId = id, SequenceId = "seq-" + id, ScheduleType = type, TimerTimeOfDay = tod, TimerRelativeOffset = rel, Enabled = enabled };

  private static (QueueRunHandle Handle, QueueRunSchedule Schedule) Run(bool cycling, params QueueTemplateEntry[] entries) {
    var plan = QueueRunPlan.Build(entries, 0);
    var schedule = new QueueRunSchedule(plan, Now, cycling);
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource(), Schedule = schedule, CycleExecution = cycling };
    return (handle, schedule);
  }

  private static string State(QueueRunHandle handle, QueueRunSchedule schedule, string entryId) =>
    QueueEntryStateReader.ReadAll(handle, schedule, Now).GetValueOrDefault(entryId, "none");

  [Fact]
  public void OncePerRunIsScheduledUntilItsStepIsDone() {
    var (handle, schedule) = Run(false, Entry("a", ScheduleType.OncePerRun));

    State(handle, schedule, "a").Should().Be("scheduled");

    schedule.MarkOncePerRunCompleted(0);
    schedule.MarkOncePerRunPassDone();
    State(handle, schedule, "a").Should().Be("idle");
  }

  [Fact]
  public void OncePerRunOfANonCyclingRunStaysIdleAfterThePassIsDone() {
    var (handle, schedule) = Run(false, Entry("a", ScheduleType.OncePerRun));
    schedule.MarkOncePerRunPassDone();

    State(handle, schedule, "a").Should().Be("idle");
  }

  [Fact]
  public void OncePerRunOfACyclingRunIsIdleOnlyWhileItsStepIsDoneInTheCycle() {
    var (handle, schedule) = Run(true, Entry("a", ScheduleType.OncePerRun));
    schedule.MarkOncePerRunCompleted(0);
    State(handle, schedule, "a").Should().Be("idle");

    schedule.BeginCycle();
    State(handle, schedule, "a").Should().Be("scheduled");
  }

  [Fact]
  public void TimeOfDayTimerIsIdleAfterItFiredToday() {
    var (handle, schedule) = Run(false, Entry("t", ScheduleType.Timer, tod: new TimeOnly(9, 0)));
    State(handle, schedule, "t").Should().Be("scheduled");

    schedule.MarkTimeOfDayFired(0, DateOnly.FromDateTime(Now.DateTime));
    State(handle, schedule, "t").Should().Be("idle");
  }

  [Fact]
  public void TimeOfDayTimerWithAnArmedRetryIsScheduled() {
    var (handle, schedule) = Run(false, Entry("t", ScheduleType.Timer, tod: new TimeOnly(9, 0)));
    schedule.MarkTimeOfDayFired(0, DateOnly.FromDateTime(Now.DateTime));
    schedule.ArmDailyRetry(0, Now.AddMinutes(10), attempt: 1);

    State(handle, schedule, "t").Should().Be("scheduled");
  }

  [Fact]
  public void RelativeTimerIsIdleAfterItFired() {
    var (handle, schedule) = Run(false, Entry("r", ScheduleType.Timer, rel: TimeSpan.FromHours(1)));
    State(handle, schedule, "r").Should().Be("scheduled");

    schedule.MarkRelativeFired(0);
    State(handle, schedule, "r").Should().Be("idle");
  }

  [Theory]
  [InlineData(ScheduleType.AtQueueStart)]
  [InlineData(ScheduleType.BeforeEachRun)]
  [InlineData(ScheduleType.EveryStep)]
  public void AnEntryOutsideTheOncePerRunPassIsIdleAfterItRanOnce(ScheduleType type) {
    var (handle, schedule) = Run(false, Entry("x", type));
    State(handle, schedule, "x").Should().Be("scheduled");

    schedule.MarkEntryRan(schedule.Entries[0]);
    State(handle, schedule, "x").Should().Be("idle");
  }

  [Fact]
  public void APendingBookingOfTheSameSequenceMakesAnIdleEntryScheduled() {
    var (handle, schedule) = Run(false, Entry("a", ScheduleType.OncePerRun));
    schedule.MarkOncePerRunCompleted(0);
    schedule.MarkOncePerRunPassDone();
    State(handle, schedule, "a").Should().Be("idle");

    handle.AddTimerFiring(new SelfRescheduleEntry("b1", "seq-a", SelfRescheduleOption.Timer, Now.AddMinutes(5)));

    State(handle, schedule, "a").Should().Be("scheduled");
  }

  [Fact]
  public void ALiveScheduleOfTheSameSequenceMakesAnIdleEntryScheduled() {
    var (handle, schedule) = Run(false, Entry("a", ScheduleType.OncePerRun));
    schedule.MarkOncePerRunPassDone();
    handle.PendingLiveSchedules["seq-a"] = Now.AddMinutes(5);

    State(handle, schedule, "a").Should().Be("scheduled");
  }

  [Fact]
  public void TheSequenceThatRunsNowGivesTheStateRunning() {
    var (handle, schedule) = Run(false, Entry("a", ScheduleType.OncePerRun), Entry("b", ScheduleType.OncePerRun));
    handle.SetCurrentSequence("seq-a", Now);

    State(handle, schedule, "a").Should().Be("running");
    State(handle, schedule, "b").Should().Be("scheduled");
  }

  [Fact]
  public void AnEntryWithARequestGivesTheStateQueued() {
    var (handle, schedule) = Run(false, Entry("a", ScheduleType.OncePerRun));
    schedule.MarkOncePerRunPassDone();
    handle.TryAddRunNext(new PendingRunNext("a", "seq-a", ParameterScope.Empty, Now));

    State(handle, schedule, "a").Should().Be("queued");
  }

  [Fact]
  public void ADisabledEntryAndAnEntryWithNoIdHaveNoState() {
    var disabled = Entry("d", ScheduleType.OncePerRun, enabled: false);
    var noId = Entry("", ScheduleType.OncePerRun);
    var plan = QueueRunPlan.Build(new[] { disabled, noId, Entry("a", ScheduleType.OncePerRun) }, 0);
    var schedule = new QueueRunSchedule(plan, Now, false);
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource(), Schedule = schedule };

    var states = QueueEntryStateReader.ReadAll(handle, schedule, Now);

    states.Keys.Should().Equal("a");
  }

  [Fact]
  public void MarkEntryRanIgnoresAnEntryThatIsNotInThePlan() {
    var (handle, schedule) = Run(false, Entry("a", ScheduleType.EveryStep));

    schedule.MarkEntryRan(Entry("other", ScheduleType.EveryStep));

    State(handle, schedule, "a").Should().Be("scheduled");
  }
}

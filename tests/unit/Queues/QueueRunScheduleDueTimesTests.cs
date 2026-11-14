using System;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA1861

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 189: <see cref="QueueRunSchedule.SnapshotTimedEntryDueTimes"/> gives the smart scheduling candidates of the template.</summary>
public sealed class QueueRunScheduleDueTimesTests {
  private static readonly DateTimeOffset Noon = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(1));
  private static readonly DateTimeOffset RunStart = Noon.AddHours(-1);
  private static readonly DateOnly Today = DateOnly.FromDateTime(Noon.DateTime);

  private static QueueTemplateEntry TimeOfDay(string id, string sequence, TimeOnly at) =>
    new() { EntryId = id, SequenceId = sequence, ScheduleType = ScheduleType.Timer, TimerTimeOfDay = at };

  private static QueueTemplateEntry Relative(string id, string sequence, TimeSpan offset) =>
    new() { EntryId = id, SequenceId = sequence, ScheduleType = ScheduleType.Timer, TimerRelativeOffset = offset };

  private static QueueRunSchedule NewSchedule(params QueueTemplateEntry[] entries) => new(entries, RunStart, cycling: true);

  [Fact]
  public void ATimeOfDayEntryAheadTodayGivesTodayAtThatTime() {
    var schedule = NewSchedule(TimeOfDay("t", "seq-t", new TimeOnly(12, 10)));

    var candidates = schedule.SnapshotTimedEntryDueTimes(Noon);

    candidates.Should().ContainSingle().Which.Should().Be(
      new SmartCandidate(Noon.AddMinutes(10), "seq-t", SmartTargetKind.TemplateTimer));
  }

  [Fact]
  public void ATimeOfDayEntryThatFiredTodayGivesTomorrow() {
    var schedule = NewSchedule(TimeOfDay("t", "seq-t", new TimeOnly(12, 10)));
    schedule.MarkTimeOfDayFired(0, Today);

    var candidates = schedule.SnapshotTimedEntryDueTimes(Noon);

    candidates.Should().ContainSingle().Which.DueAt.Should().Be(Noon.AddDays(1).AddMinutes(10));
  }

  [Fact]
  public void ARelativeEntryThatDidNotFireGivesTheRunStartPlusTheOffset() {
    var schedule = NewSchedule(Relative("r", "seq-r", TimeSpan.FromMinutes(70)));

    var candidates = schedule.SnapshotTimedEntryDueTimes(Noon);

    candidates.Should().ContainSingle().Which.Should().Be(
      new SmartCandidate(RunStart.AddMinutes(70), "seq-r", SmartTargetKind.TemplateTimer));
  }

  [Fact]
  public void ARelativeEntryThatFiredGivesNoCandidate() {
    var schedule = NewSchedule(Relative("r", "seq-r", TimeSpan.FromMinutes(70)));
    schedule.MarkRelativeFired(0);

    schedule.SnapshotTimedEntryDueTimes(Noon).Should().BeEmpty();
  }

  [Fact]
  public void AnArmedDailyRetryGivesItsDueTime() {
    var schedule = NewSchedule(TimeOfDay("t", "seq-t", new TimeOnly(6, 0)));
    schedule.MarkTimeOfDayFired(0, Today);
    schedule.ArmDailyRetry(0, Noon.AddMinutes(5), 1);

    var candidates = schedule.SnapshotTimedEntryDueTimes(Noon);

    candidates.Should().Contain(new SmartCandidate(Noon.AddMinutes(5), "seq-t", SmartTargetKind.DailyRetry));
    candidates.Should().Contain(c => c.Kind == SmartTargetKind.TemplateTimer && c.DueAt == Noon.AddDays(1).AddHours(-6));
  }

  [Fact]
  public void EntriesThatAreNotTimerEntriesGiveNoCandidate() {
    var schedule = NewSchedule(
      new QueueTemplateEntry { EntryId = "o", SequenceId = "seq-o", ScheduleType = ScheduleType.OncePerRun },
      new QueueTemplateEntry { EntryId = "s", SequenceId = "seq-s", ScheduleType = ScheduleType.AtQueueStart });

    schedule.SnapshotTimedEntryDueTimes(Noon).Should().BeEmpty();
  }

  [Fact]
  public void TheCandidatesComeInPlanOrder() {
    var schedule = NewSchedule(
      Relative("r", "seq-r", TimeSpan.FromMinutes(90)),
      TimeOfDay("t", "seq-t", new TimeOnly(12, 30)));

    schedule.SnapshotTimedEntryDueTimes(Noon).Select(c => c.SequenceId).Should().Equal("seq-r", "seq-t");
  }
}

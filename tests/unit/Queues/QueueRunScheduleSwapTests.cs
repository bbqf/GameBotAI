using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

// CA5394: a seeded Random makes a repeatable test. No security use.
#pragma warning disable CA2000, CA1861, CA5394

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 171: <see cref="QueueRunSchedule.SwapPlan"/>. A swap keeps the consumed state of an entry that stays, so
/// no timer moves (FR-008, FR-009). It drops the state of a removed entry and of an entry with a selected schedule
/// change. A rename map moves the state to a new key.
/// </summary>
public sealed class QueueRunScheduleSwapTests {
  private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
  private static readonly DateOnly Today = DateOnly.FromDateTime(Noon.DateTime);

  private static QueueTemplateEntry Once(string id, string sequence) =>
    new() { EntryId = id, SequenceId = sequence, ScheduleType = ScheduleType.OncePerRun };

  private static QueueTemplateEntry TimeOfDay(string id, string sequence, TimeOnly at) =>
    new() { EntryId = id, SequenceId = sequence, ScheduleType = ScheduleType.Timer, TimerTimeOfDay = at };

  private static QueueTemplateEntry Relative(string id, string sequence, TimeSpan offset) =>
    new() { EntryId = id, SequenceId = sequence, ScheduleType = ScheduleType.Timer, TimerRelativeOffset = offset };

  private static QueueRunHandle NewHandle() => new() { QueueId = "q1", Cts = new System.Threading.CancellationTokenSource() };

  private static QueueRunSchedule NewSchedule(QueueRunPlan plan) => new(plan, Noon.AddHours(-1), cycling: true);

  [Fact]
  public void ConsumedStateOfAnEntryThatStaysSurvivesASwapThatAddsAnEntryBeforeIt() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { Once("a", "seq-a"), TimeOfDay("t", "seq-t", new TimeOnly(6, 0)) }, 0));
    schedule.MarkTimeOfDayFired(1, Today);
    schedule.MarkOncePerRunCompleted(0);
    var newPlan = QueueRunPlan.Build(new[] { Once("new", "seq-n"), Once("a", "seq-a"), TimeOfDay("t", "seq-t", new TimeOnly(6, 0)) }, 1);

    schedule.SwapPlan(newPlan);

    // The index of the timer moved from 1 to 2, but its state follows its key.
    schedule.TimeOfDayFiredOn(2, Today).Should().BeTrue();
    schedule.OncePerRunCompletedThisCycle(1).Should().BeTrue();
    schedule.OncePerRunCompletedThisCycle(0).Should().BeFalse("the new entry has not run");
    schedule.TimeOfDayFiredOn(1, Today).Should().BeFalse();
  }

  [Fact]
  public void ANextDueTimeDoesNotMoveWhenAnEarlierEntryIsRemoved() {
    var handle = NewHandle();
    var plan = QueueRunPlan.Build(new[] { Once("a", "seq-a"), TimeOfDay("t", "seq-t", new TimeOnly(18, 0)), Relative("r", "seq-r", TimeSpan.FromHours(3)) }, 0);
    var schedule = NewSchedule(plan);
    handle.Schedule = schedule;
    var before = schedule.ComputeNextDue(handle, Noon);

    schedule.SwapPlan(QueueRunPlan.Build(new[] { TimeOfDay("t", "seq-t", new TimeOnly(18, 0)), Relative("r", "seq-r", TimeSpan.FromHours(3)) }, 1), droppedKeys: new[] { "a" });

    schedule.ComputeNextDue(handle, Noon).Should().Be(before);
    handle.Cts.Dispose();
  }

  [Fact]
  public void ARemovedEntryLosesItsConsumedStateAndItsRetry() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { TimeOfDay("t", "seq-t", new TimeOnly(6, 0)), Once("a", "seq-a") }, 0));
    schedule.MarkTimeOfDayFired(0, Today);
    schedule.ArmDailyRetry(0, Noon.AddMinutes(5), attempt: 1);
    schedule.MarkOncePerRunCompleted(1);

    schedule.SwapPlan(QueueRunPlan.Build(new[] { Once("a", "seq-a") }, 1), droppedKeys: new[] { "t" });
    // A new entry with the same ID comes back later: it starts clean.
    schedule.SwapPlan(QueueRunPlan.Build(new[] { TimeOfDay("t", "seq-t", new TimeOnly(6, 0)), Once("a", "seq-a") }, 2));

    schedule.TimeOfDayFiredOn(0, Today).Should().BeFalse();
    schedule.HasPendingDailyRetries.Should().BeFalse();
    schedule.OncePerRunCompletedThisCycle(1).Should().BeTrue("the entry that stayed keeps its state");
  }

  [Fact]
  public void ADisabledEntryKeepsItsConsumedStateAndItsDueTime() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { TimeOfDay("t", "seq-t", new TimeOnly(6, 0)), Once("a", "seq-a") }, 0));
    schedule.MarkTimeOfDayFired(0, Today);
    var disabled = TimeOfDay("t", "seq-t", new TimeOnly(6, 0));
    disabled.Enabled = false;

    schedule.SwapPlan(QueueRunPlan.FromAll(new[] { disabled, Once("a", "seq-a") }, new[] { "t", "a" }, 1));
    var enabled = TimeOfDay("t", "seq-t", new TimeOnly(6, 0));
    schedule.SwapPlan(QueueRunPlan.FromAll(new[] { enabled, Once("a", "seq-a") }, new[] { "t", "a" }, 2));

    schedule.TimeOfDayFiredOn(0, Today).Should().BeTrue("a disable and an enable do not reset the timer");
  }

  [Fact]
  public void AClearedEntryLosesItsConsumedState() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { Relative("r", "seq-r", TimeSpan.FromMinutes(10)), Once("a", "seq-a") }, 0));
    schedule.MarkRelativeFired(0);
    schedule.MarkOncePerRunCompleted(1);

    schedule.SwapPlan(QueueRunPlan.Build(new[] { Relative("r", "seq-r", TimeSpan.FromMinutes(20)), Once("a", "seq-a") }, 1), clearedKeys: new[] { "r" });

    schedule.RelativeFired(0).Should().BeFalse("a selected schedule change starts the timer again");
    schedule.OncePerRunCompletedThisCycle(1).Should().BeTrue();
  }

  [Fact]
  public void ADailyRetryOfAnEntryThatStaysStillFiresAtItsTimeAtItsNewIndex() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { TimeOfDay("t", "seq-t", new TimeOnly(6, 0)) }, 0));
    schedule.ArmDailyRetry(0, Noon.AddMinutes(5), attempt: 2);

    schedule.SwapPlan(QueueRunPlan.Build(new[] { Once("new", "seq-n"), TimeOfDay("t", "seq-t", new TimeOnly(6, 0)) }, 1));

    schedule.DueDailyRetries(Noon.AddMinutes(6)).Should().Equal(new[] { (1, 2) });
    schedule.DueDailyRetries(Noon.AddMinutes(1)).Should().BeEmpty();
  }

  [Fact]
  public void ARetryOfADisabledEntryDoesNotKeepTheRunAliveAndDoesNotFire() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { TimeOfDay("t", "seq-t", new TimeOnly(6, 0)) }, 0));
    schedule.ArmDailyRetry(0, Noon.AddMinutes(5), attempt: 1);
    var disabled = TimeOfDay("t", "seq-t", new TimeOnly(6, 0));
    disabled.Enabled = false;

    schedule.SwapPlan(QueueRunPlan.FromAll(new[] { disabled }, new[] { "t" }, 1));

    schedule.HasPendingDailyRetries.Should().BeFalse();
    schedule.DueDailyRetries(Noon.AddHours(1)).Should().BeEmpty();
  }

  [Fact]
  public void ARenameMapMovesTheConsumedStateToTheNewKey() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { Once("", "seq-a"), TimeOfDay("", "seq-t", new TimeOnly(6, 0)) }, 0));
    schedule.MarkTimeOfDayFired(1, Today);
    schedule.MarkOncePerRunCompleted(0);
    schedule.ArmDailyRetry(1, Noon.AddMinutes(5), attempt: 1);
    var map = new Dictionary<string, string> { ["idx:0"] = "id-a", ["idx:1"] = "id-t" };
    var upgraded = schedule.Plan.WithRenamedKeys(map);

    schedule.SwapPlan(upgraded, renameMap: map);

    schedule.Plan.Keys.Should().Equal("id-a", "id-t");
    schedule.TimeOfDayFiredOn(1, Today).Should().BeTrue();
    schedule.OncePerRunCompletedThisCycle(0).Should().BeTrue();
    schedule.DueDailyRetries(Noon.AddMinutes(6)).Should().Equal(new[] { (1, 1) });
  }

  [Fact]
  public void APlanWithRenamedKeysCopiesTheEntryAndSetsTheId() {
    var original = Once("", "seq-a");
    var plan = QueueRunPlan.Build(new[] { original }, 0);

    var renamed = plan.WithRenamedKeys(new Dictionary<string, string> { ["idx:0"] = "id-a" });

    renamed.Entries.Single().EntryId.Should().Be("id-a");
    original.EntryId.Should().BeEmpty("the old entry is not changed");
    renamed.Version.Should().Be(plan.Version);
    renamed.HasIndexKeys.Should().BeFalse();
    plan.HasIndexKeys.Should().BeTrue();
  }

  [Fact]
  public void AnIndexReadAfterASwapToASmallerPlanDoesNotThrow() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { Once("a", "seq-a"), Once("b", "seq-b"), Once("c", "seq-c") }, 0));
    schedule.SwapPlan(QueueRunPlan.Build(new[] { Once("a", "seq-a") }, 1), droppedKeys: new[] { "b", "c" });

    Action read = () => schedule.TimeOfDayFiredOn(2, Today);

    read.Should().NotThrow();
    schedule.TimeOfDayFiredOn(2, Today).Should().BeFalse();
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  [InlineData(4)]
  [InlineData(5)]
  [InlineData(6)]
  [InlineData(7)]
  [InlineData(8)]
  public void ASwapWithNoScheduleKeyNeverChangesTheDueStateOfAnEntryThatStays(int seed) {
    // A seeded random run: some entries, some consumed state, then a swap that adds, removes, and moves entries.
    var random = new Random(seed);
    var entries = Enumerable.Range(0, 8).Select(i => {
      var id = "e" + i;
      return random.Next(3) switch {
        0 => Once(id, "seq-" + i),
        1 => TimeOfDay(id, "seq-" + i, new TimeOnly(random.Next(0, 24), random.Next(0, 60))),
        _ => Relative(id, "seq-" + i, TimeSpan.FromMinutes(random.Next(1, 600)))
      };
    }).ToList();
    var schedule = NewSchedule(QueueRunPlan.Build(entries, 0));
    for (var i = 0; i < entries.Count; i++) {
      if (random.Next(2) == 0) continue;
      switch (entries[i].ScheduleType) {
        case ScheduleType.Timer when entries[i].TimerTimeOfDay is not null:
          schedule.MarkTimeOfDayFired(i, Today);
          break;
        case ScheduleType.Timer:
          schedule.MarkRelativeFired(i);
          break;
        default:
          schedule.MarkOncePerRunCompleted(i);
          break;
      }
    }

    string State(QueueRunSchedule s, string key) {
      var index = s.Plan.IndexOfKey(key);
      var entry = s.Plan.Entries[index];
      return entry.ScheduleType switch {
        ScheduleType.Timer when entry.TimerTimeOfDay is { } tod => "tod:" + s.NextTimeOfDayDue(index, tod, Noon).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ScheduleType.Timer => "rel:" + s.RelativeFired(index),
        _ => "once:" + s.OncePerRunCompletedThisCycle(index)
      };
    }

    var before = entries.ToDictionary(e => e.EntryId, e => State(schedule, e.EntryId));
    var removed = entries.Where(_ => random.Next(4) == 0).Select(e => e.EntryId).ToHashSet();
    var kept = entries.Where(e => !removed.Contains(e.EntryId)).OrderBy(_ => random.Next()).ToList();
    var added = Enumerable.Range(0, random.Next(0, 3)).Select(i => Once("new" + i, "seq-new" + i)).ToList();
    var all = kept.Concat(added).OrderBy(_ => random.Next()).ToList();

    schedule.SwapPlan(QueueRunPlan.Build(all, 1), droppedKeys: removed);

    foreach (var entry in kept) {
      State(schedule, entry.EntryId).Should().Be(before[entry.EntryId], "entry " + entry.EntryId + " stayed, so its due state does not change");
    }
  }

  [Fact]
  public void TheEntriesPropertyFollowsTheCurrentPlan() {
    var schedule = NewSchedule(QueueRunPlan.Build(new[] { Once("a", "seq-a") }, 0));

    schedule.SwapPlan(QueueRunPlan.Build(new[] { Once("a", "seq-a"), Once("b", "seq-b") }, 1));

    schedule.Entries.Select(e => e.SequenceId).Should().Equal("seq-a", "seq-b");
    schedule.Plan.Version.Should().Be(1);
  }
}

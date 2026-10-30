using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA1861

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 171 (User Story 4, FR-014): a new order of the entries is one difference, <c>order:all</c>. It is selected by
/// default. The apply puts the entries of the plan in the order of the template. No consumed state and no next due time
/// changes, because the state follows the entry ID and not the position.
/// </summary>
public sealed class QueueTemplateDiffOrderTests {
  private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>();

  private static QueueTemplateEntry Entry(string id, ScheduleType type = ScheduleType.OncePerRun, TimeOnly? tod = null) =>
    new() { EntryId = id, SequenceId = "seq-" + id, ScheduleType = type, TimerTimeOfDay = tod };

  private static QueueTemplate Template(params QueueTemplateEntry[] entries) {
    var template = new QueueTemplate { Id = "t1", Name = "T" };
    foreach (var e in entries) template.Entries.Add(e);
    return template;
  }

  private static ISet<string> Keys(params string[] keys) => new HashSet<string>(keys, StringComparer.Ordinal);

  [Fact]
  public void ANewOrderGivesOneOrderDifference() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b"), Entry("c") }, 0);

    var result = QueueTemplateDiff.Compute(plan, Template(Entry("c"), Entry("a"), Entry("b")), Names);

    var difference = result.Differences.Should().ContainSingle().Subject;
    difference.Type.Should().Be("order");
    difference.Key.Should().Be("order:all");
    difference.SelectedByDefault.Should().BeTrue();
    difference.AffectsSchedule.Should().BeFalse();
  }

  [Fact]
  public void TheSameOrderGivesNoOrderDifference() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b") }, 0);

    QueueTemplateDiff.Compute(plan, Template(Entry("a"), Entry("b")), Names).Differences.Should().BeEmpty();
  }

  [Fact]
  public void AnAddedOrRemovedEntryAloneIsNotAnOrderChange() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b"), Entry("c") }, 0);

    var result = QueueTemplateDiff.Compute(plan, Template(Entry("a"), Entry("new"), Entry("c")), Names);

    result.Differences.Select(d => d.Type).Should().BeEquivalentTo("added", "removed");
  }

  [Fact]
  public void TheOrderOfTheEntriesThatBothSidesHaveDecides() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b"), Entry("c") }, 0);

    var result = QueueTemplateDiff.Compute(plan, Template(Entry("c"), Entry("new"), Entry("a")), Names);

    result.Differences.Select(d => d.Type).Should().Contain("order");
  }

  [Fact]
  public void ApplyingTheOrderPutsTheEntriesInTheOrderOfTheTemplate() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b"), Entry("c") }, 0);
    var target = Template(Entry("c"), Entry("a"), Entry("b"));

    var built = QueueLiveApplyBuilder.Build(plan, target, Keys("order:all"));

    built.AllKeys.Should().Equal("c", "a", "b");
    built.AllEntries.Select(e => e.SequenceId).Should().Equal("seq-c", "seq-a", "seq-b");
    built.DroppedKeys.Should().BeEmpty();
    built.ClearedKeys.Should().BeEmpty();
  }

  [Fact]
  public void WithoutTheOrderKeyTheOrderStaysTheSame() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b") }, 0);

    var built = QueueLiveApplyBuilder.Build(plan, Template(Entry("b"), Entry("a")), Keys());

    built.AllKeys.Should().Equal("a", "b");
  }

  [Fact]
  public void AnEntryThatTheTemplateDoesNotHaveKeepsItsSlot() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("keep"), Entry("b") }, 0);

    var built = QueueLiveApplyBuilder.Build(plan, Template(Entry("b"), Entry("a")), Keys("order:all"));

    built.AllKeys.Should().Equal("b", "keep", "a");
  }

  [Fact]
  public void TheOrderAndAnAddedEntryTogetherGiveTheTemplateOrder() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b") }, 0);
    var target = Template(Entry("b"), Entry("new"), Entry("a"));

    var built = QueueLiveApplyBuilder.Build(plan, target, Keys("order:all", "added:new"));

    built.AllKeys.Should().Equal("b", "new", "a");
  }

  [Fact]
  public void TheOrderAndARemovalTogetherWork() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b"), Entry("c") }, 0);
    var target = Template(Entry("c"), Entry("a"));

    var built = QueueLiveApplyBuilder.Build(plan, target, Keys("order:all", "removed:b"));

    built.AllKeys.Should().Equal("c", "a");
  }

  [Fact]
  public void AnOrderApplyKeepsEveryConsumedStateAndEveryNextDueTime() {
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new System.Threading.CancellationTokenSource() };
    var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    var entries = new[] { Entry("a"), Entry("t", ScheduleType.Timer, new TimeOnly(18, 0)), Entry("c") };
    var schedule = new QueueRunSchedule(QueueRunPlan.Build(entries, 0), now.AddHours(-1), cycling: true);
    handle.Schedule = schedule;
    schedule.MarkOncePerRunCompleted(0);
    schedule.MarkTimeOfDayFired(1, DateOnly.FromDateTime(now.DateTime));
    var before = schedule.ComputeNextDue(handle, now);
    var target = Template(Entry("c"), Entry("t", ScheduleType.Timer, new TimeOnly(18, 0)), Entry("a"));
    var built = QueueLiveApplyBuilder.Build(schedule.Plan, target, Keys("order:all"));

    schedule.SwapPlan(QueueRunPlan.FromAll(built.AllEntries, built.AllKeys, 1), built.DroppedKeys, built.ClearedKeys);

    schedule.ComputeNextDue(handle, now).Should().Be(before);
    schedule.OncePerRunCompletedThisCycle(schedule.Plan.IndexOfKey("a")).Should().BeTrue();
    schedule.OncePerRunCompletedThisCycle(schedule.Plan.IndexOfKey("c")).Should().BeFalse();
    schedule.TimeOfDayFiredOn(schedule.Plan.IndexOfKey("t"), DateOnly.FromDateTime(now.DateTime)).Should().BeTrue();
    handle.Cts.Dispose();
  }
}

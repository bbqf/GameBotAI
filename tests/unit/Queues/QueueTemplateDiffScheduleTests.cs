using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA1861

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 171 (FR-008, FR-010): a schedule change is a difference of its own. It is not selected by default. Without
/// its key, the entry keeps its schedule. With its key, the entry gets the new schedule and its consumed state clears.
/// </summary>
public sealed class QueueTemplateDiffScheduleTests {
  private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string> { ["seq-a"] = "Alpha" };

  private static QueueTemplateEntry Entry(string id, ScheduleType type, TimeOnly? tod = null, TimeSpan? offset = null) =>
    new() { EntryId = id, SequenceId = "seq-a", ScheduleType = type, TimerTimeOfDay = tod, TimerRelativeOffset = offset };

  private static QueueTemplate Template(params QueueTemplateEntry[] entries) {
    var template = new QueueTemplate { Id = "t1", Name = "T" };
    foreach (var e in entries) template.Entries.Add(e);
    return template;
  }

  private static TemplateDiffResult Diff(QueueTemplateEntry live, QueueTemplateEntry target) =>
    QueueTemplateDiff.Compute(QueueRunPlan.Build(new[] { live }, 0), Template(target), Names);

  [Fact]
  public void ATypeChangeIsAScheduleDifference() {
    var result = Diff(Entry("1", ScheduleType.OncePerRun), Entry("1", ScheduleType.EveryStep));

    var difference = result.Differences.Should().ContainSingle().Subject;
    difference.Type.Should().Be("schedule");
    difference.Key.Should().Be("schedule:1");
  }

  [Fact]
  public void ATimeOfDayChangeIsAScheduleDifference() {
    var result = Diff(Entry("1", ScheduleType.Timer, tod: new TimeOnly(6, 0)), Entry("1", ScheduleType.Timer, tod: new TimeOnly(7, 30)));

    var difference = result.Differences.Should().ContainSingle().Subject;
    difference.Type.Should().Be("schedule");
    difference.Summary.Should().Contain("06:00").And.Contain("07:30");
  }

  [Fact]
  public void AnOffsetChangeIsAScheduleDifference() {
    var result = Diff(
      Entry("1", ScheduleType.Timer, offset: TimeSpan.FromMinutes(10)),
      Entry("1", ScheduleType.Timer, offset: TimeSpan.FromMinutes(20)));

    result.Differences.Should().ContainSingle().Which.Type.Should().Be("schedule");
  }

  [Fact]
  public void ASwitchFromTimeOfDayToOffsetIsAScheduleDifference() {
    var result = Diff(
      Entry("1", ScheduleType.Timer, tod: new TimeOnly(6, 0)),
      Entry("1", ScheduleType.Timer, offset: TimeSpan.FromHours(1)));

    result.Differences.Should().ContainSingle().Which.Type.Should().Be("schedule");
  }

  [Fact]
  public void AScheduleDifferenceIsNotSelectedByDefaultAndAffectsTheSchedule() {
    var result = Diff(Entry("1", ScheduleType.OncePerRun), Entry("1", ScheduleType.EveryStep));

    var difference = result.Differences.Single();
    difference.SelectedByDefault.Should().BeFalse("a schedule changes only on a direct instruction");
    difference.AffectsSchedule.Should().BeTrue();
  }

  [Fact]
  public void AllOtherTypesAreSelectedByDefault() {
    var live = Entry("1", ScheduleType.OncePerRun);
    var target = Entry("1", ScheduleType.EveryStep);
    target.Enabled = false;
    target.ParameterValues.Add(new ParameterBinding { Name = "n", Value = "1" });

    var result = Diff(live, target);

    result.Differences.Select(d => d.Type).Should().BeEquivalentTo("schedule", "disabled", "parameters");
    result.Differences.Where(d => d.Type != "schedule").Should().OnlyContain(d => d.SelectedByDefault);
    result.Differences.Single(d => d.Type == "schedule").SelectedByDefault.Should().BeFalse();
  }

  [Fact]
  public void TheSameScheduleGivesNoScheduleDifference() {
    var result = Diff(Entry("1", ScheduleType.Timer, tod: new TimeOnly(6, 0)), Entry("1", ScheduleType.Timer, tod: new TimeOnly(6, 0)));

    result.Differences.Should().BeEmpty();
  }

  [Fact]
  public void WithTheScheduleKeyTheEntryGetsTheNewScheduleAndItsStateIsCleared() {
    var live = Entry("1", ScheduleType.Timer, tod: new TimeOnly(6, 0));
    var target = Entry("1", ScheduleType.Timer, tod: new TimeOnly(7, 30));

    var built = QueueLiveApplyBuilder.Build(QueueRunPlan.Build(new[] { live }, 0), Template(target), new HashSet<string> { "schedule:1" });

    var changed = built.AllEntries.Single();
    changed.TimerTimeOfDay.Should().Be(new TimeOnly(7, 30));
    built.ClearedKeys.Should().Equal("1");
    live.TimerTimeOfDay.Should().Be(new TimeOnly(6, 0), "the live entry is not changed");
  }

  [Fact]
  public void WithoutTheScheduleKeyTheEntryKeepsItsScheduleAndItsState() {
    var live = Entry("1", ScheduleType.Timer, tod: new TimeOnly(6, 0));
    var target = Entry("1", ScheduleType.Timer, tod: new TimeOnly(7, 30));
    target.ParameterValues.Add(new ParameterBinding { Name = "n", Value = "1" });

    var built = QueueLiveApplyBuilder.Build(QueueRunPlan.Build(new[] { live }, 0), Template(target), new HashSet<string> { "parameters:1" });

    built.AllEntries.Single().TimerTimeOfDay.Should().Be(new TimeOnly(6, 0));
    built.ClearedKeys.Should().BeEmpty();
  }

  [Fact]
  public void ASwitchToTheTypeOncePerRunClearsTheTimerFields() {
    var live = Entry("1", ScheduleType.Timer, tod: new TimeOnly(6, 0));
    var target = Entry("1", ScheduleType.OncePerRun);

    var built = QueueLiveApplyBuilder.Build(QueueRunPlan.Build(new[] { live }, 0), Template(target), new HashSet<string> { "schedule:1" });

    var changed = built.AllEntries.Single();
    changed.ScheduleType.Should().Be(ScheduleType.OncePerRun);
    changed.TimerTimeOfDay.Should().BeNull();
    changed.TimerRelativeOffset.Should().BeNull();
  }

  [Fact]
  public void ANewEntryHasNoScheduleDifferenceAndKeepsTheScheduleOfTheTemplate() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", ScheduleType.OncePerRun) }, 0);
    var target = Template(Entry("1", ScheduleType.OncePerRun), Entry("2", ScheduleType.Timer, tod: new TimeOnly(5, 0)));

    var result = QueueTemplateDiff.Compute(plan, target, Names);
    var built = QueueLiveApplyBuilder.Build(plan, target, new HashSet<string> { "added:2" });

    result.Differences.Should().ContainSingle().Which.Type.Should().Be("added");
    built.AllEntries[1].TimerTimeOfDay.Should().Be(new TimeOnly(5, 0));
    built.ClearedKeys.Should().BeEmpty();
  }
}

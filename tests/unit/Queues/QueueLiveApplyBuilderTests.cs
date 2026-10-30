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
/// Feature 171: the entries of the new plan of a live apply. Only the selected differences go in. An entry with no
/// selected difference stays the same object, so its schedule and its consumed state do not change (FR-008).
/// </summary>
public sealed class QueueLiveApplyBuilderTests {
  private static QueueTemplateEntry Entry(string id, string sequence, bool enabled = true, ScheduleType type = ScheduleType.OncePerRun) =>
    new() { EntryId = id, SequenceId = sequence, Enabled = enabled, ScheduleType = type };

  private static QueueTemplate Template(params QueueTemplateEntry[] entries) {
    var template = new QueueTemplate { Id = "t1", Name = "T" };
    foreach (var e in entries) template.Entries.Add(e);
    return template;
  }

  private static ISet<string> Keys(params string[] keys) => new HashSet<string>(keys, StringComparer.Ordinal);

  private static LiveApplyEntries Build(QueueRunPlan plan, QueueTemplate template, params string[] selected) =>
    QueueLiveApplyBuilder.Build(plan, template, Keys(selected));

  [Fact]
  public void NothingSelectedGivesTheSameEntries() {
    var a = Entry("1", "seq-a");
    var b = Entry("2", "seq-b");
    var plan = QueueRunPlan.Build(new[] { a, b }, 0);

    var result = Build(plan, Template(Entry("1", "seq-a", enabled: false), Entry("3", "seq-c")));

    result.AllEntries.Should().HaveCount(2);
    result.AllEntries[0].Should().BeSameAs(a);
    result.AllEntries[1].Should().BeSameAs(b);
    result.DroppedKeys.Should().BeEmpty();
    result.TouchedEntries.Should().BeEmpty();
  }

  [Fact]
  public void OnlyTheSelectedDifferencesGoIn() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a"), Entry("2", "seq-b") }, 0);
    var target = Template(Entry("1", "seq-a", enabled: false), Entry("2", "seq-b", enabled: false));

    var result = Build(plan, target, "disabled:1");

    result.AllEntries[0].Enabled.Should().BeFalse();
    result.AllEntries[1].Enabled.Should().BeTrue("disabled:2 was not selected");
  }

  [Fact]
  public void AnEntryWithNoSelectedDifferenceIsNotCloned() {
    var untouched = Entry("2", "seq-b");
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a"), untouched }, 0);

    var result = Build(plan, Template(Entry("1", "seq-a", enabled: false), Entry("2", "seq-b")), "disabled:1");

    result.AllEntries[1].Should().BeSameAs(untouched);
    result.TouchedEntries.Should().ContainSingle().Which.EntryId.Should().Be("1");
  }

  [Fact]
  public void TheLivePlanIsNotChanged() {
    var live = Entry("1", "seq-a");
    var plan = QueueRunPlan.Build(new[] { live }, 0);

    Build(plan, Template(Entry("1", "seq-a", enabled: false)), "disabled:1");

    live.Enabled.Should().BeTrue();
    plan.Entries.Should().ContainSingle().Which.Should().BeSameAs(live);
  }

  [Fact]
  public void ARemovedEntryIsDroppedOnlyWhenSelected() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a"), Entry("2", "seq-b") }, 0);
    var target = Template(Entry("1", "seq-a"));

    var kept = Build(plan, target);
    var removed = Build(plan, target, "removed:2");

    kept.AllKeys.Should().Equal("1", "2");
    kept.DroppedKeys.Should().BeEmpty();
    removed.AllKeys.Should().Equal("1");
    removed.DroppedKeys.Should().Equal("2");
  }

  [Fact]
  public void EnableAndDisableSetTheFlag() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a", enabled: false), Entry("2", "seq-b") }, 0);
    var target = Template(Entry("1", "seq-a"), Entry("2", "seq-b", enabled: false));

    var result = Build(plan, target, "enabled:1", "disabled:2");

    result.AllEntries[0].Enabled.Should().BeTrue();
    result.AllEntries[1].Enabled.Should().BeFalse();
  }

  [Fact]
  public void ParametersCopyTheValuesAndTheSequenceOfTheTemplateEntry() {
    var live = Entry("1", "seq-a");
    live.ParameterValues.Add(new ParameterBinding { Name = "count", Value = "3" });
    var target = Entry("1", "seq-b");
    target.ParameterValues.Add(new ParameterBinding { Name = "count", Value = "5" });
    target.ParameterValues.Add(new ParameterBinding { Name = "extra", Value = "x" });

    var result = Build(QueueRunPlan.Build(new[] { live }, 0), Template(target), "parameters:1");

    var changed = result.AllEntries.Single();
    changed.SequenceId.Should().Be("seq-b");
    changed.ParameterValues.Select(p => (p.Name, p.Value)).Should().Equal(("count", "5"), ("extra", "x"));
    live.ParameterValues.Single().Value.Should().Be("3", "the live entry is not changed");
  }

  [Fact]
  public void ParametersDoNotChangeTheScheduleOfTheEntry() {
    var live = Entry("1", "seq-a", type: ScheduleType.Timer);
    live.TimerTimeOfDay = new TimeOnly(6, 0);
    var target = Entry("1", "seq-a", type: ScheduleType.OncePerRun);
    target.ParameterValues.Add(new ParameterBinding { Name = "n", Value = "1" });

    var result = Build(QueueRunPlan.Build(new[] { live }, 0), Template(target), "parameters:1");

    var changed = result.AllEntries.Single();
    changed.ScheduleType.Should().Be(ScheduleType.Timer);
    changed.TimerTimeOfDay.Should().Be(new TimeOnly(6, 0));
    result.ClearedKeys.Should().BeEmpty();
  }

  [Fact]
  public void ANewEntryGoesAfterTheNearestEarlierTemplateEntry() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a"), Entry("3", "seq-c") }, 0);
    var target = Template(Entry("1", "seq-a"), Entry("2", "seq-b"), Entry("3", "seq-c"));

    var result = Build(plan, target, "added:2");

    result.AllKeys.Should().Equal("1", "2", "3");
    result.TouchedEntries.Should().ContainSingle().Which.EntryId.Should().Be("2");
  }

  [Fact]
  public void ANewEntryWithNoEarlierEntryGoesFirst() {
    var plan = QueueRunPlan.Build(new[] { Entry("2", "seq-b") }, 0);
    var target = Template(Entry("1", "seq-a"), Entry("2", "seq-b"));

    Build(plan, target, "added:1").AllKeys.Should().Equal("1", "2");
  }

  [Fact]
  public void SeveralNewEntriesKeepTheTemplateOrder() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a") }, 0);
    var target = Template(Entry("1", "seq-a"), Entry("2", "seq-b"), Entry("3", "seq-c"));

    Build(plan, target, "added:2", "added:3").AllKeys.Should().Equal("1", "2", "3");
  }

  [Fact]
  public void ANewEntryThatIsNotSelectedStaysOut() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a") }, 0);

    Build(plan, Template(Entry("1", "seq-a"), Entry("2", "seq-b"))).AllKeys.Should().Equal("1");
  }

  [Fact]
  public void ANewEntryIsACopyOfTheTemplateEntry() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a") }, 0);
    var templateEntry = Entry("2", "seq-b", type: ScheduleType.EveryStep);

    var result = Build(plan, Template(Entry("1", "seq-a"), templateEntry), "added:2");

    var added = result.AllEntries[1];
    added.Should().NotBeSameAs(templateEntry);
    added.ScheduleType.Should().Be(ScheduleType.EveryStep);
    added.EntryId.Should().Be("2");
  }

  [Fact]
  public void TheNewPlanKeepsDisabledEntriesOutOfTheRunButInTheList() {
    var plan = QueueRunPlan.Build(new[] { Entry("1", "seq-a"), Entry("2", "seq-b") }, 0);

    var result = Build(plan, Template(Entry("1", "seq-a", enabled: false), Entry("2", "seq-b")), "disabled:1");
    var newPlan = QueueRunPlan.FromAll(result.AllEntries, result.AllKeys, 1);

    newPlan.AllEntries.Should().HaveCount(2);
    newPlan.Entries.Select(e => e.SequenceId).Should().Equal("seq-b");
    newPlan.Keys.Should().Equal("2");
    newPlan.Version.Should().Be(1);
  }

  [Fact]
  public void EachDifferenceOfAnEntryCanBeSelectedAlone() {
    var live = Entry("1", "seq-a");
    var target = Entry("1", "seq-a", enabled: false);
    target.ParameterValues.Add(new ParameterBinding { Name = "n", Value = "9" });
    var plan = QueueRunPlan.Build(new[] { live }, 0);

    var onlyParameters = Build(plan, Template(target), "parameters:1").AllEntries.Single();
    var onlyDisable = Build(plan, Template(target), "disabled:1").AllEntries.Single();

    onlyParameters.Enabled.Should().BeTrue();
    onlyParameters.ParameterValues.Should().ContainSingle();
    onlyDisable.Enabled.Should().BeFalse();
    onlyDisable.ParameterValues.Should().BeEmpty();
  }
}

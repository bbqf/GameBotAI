using System.Linq;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 171: <see cref="QueueRunPlan"/> holds the enabled entries of a run and the partitions that the run
/// loop kept in locals before. Each entry has a key: its entry ID, or <c>idx:&lt;position&gt;</c> (the position in
/// the whole template, disabled entries counted) for an entry of an old template.
/// </summary>
public sealed class QueueRunPlanTests {
  private static QueueTemplateEntry Entry(string sequence, ScheduleType type = ScheduleType.OncePerRun, string id = "", bool enabled = true) =>
    new() { SequenceId = sequence, ScheduleType = type, EntryId = id, Enabled = enabled };

  [Fact]
  public void PartitionsEntriesByScheduleType() {
    var plan = QueueRunPlan.Build(new[] {
      Entry("start", ScheduleType.AtQueueStart, "a"),
      Entry("once", ScheduleType.OncePerRun, "b"),
      Entry("step", ScheduleType.EveryStep, "c"),
      Entry("before", ScheduleType.BeforeEachRun, "d"),
      Entry("timer", ScheduleType.Timer, "e"),
    }, version: 0);

    plan.AtQueueStart.Select(e => e.SequenceId).Should().Equal("start");
    plan.OncePerRun.Select(x => x.Entry.SequenceId).Should().Equal("once");
    plan.EveryStep.Select(e => e.SequenceId).Should().Equal("step");
    plan.BeforeEachRun.Select(e => e.SequenceId).Should().Equal("before");
    plan.Timers.Select(x => x.Entry.SequenceId).Should().Equal("timer");
  }

  [Fact]
  public void LeavesOutDisabledEntries() {
    var plan = QueueRunPlan.Build(new[] {
      Entry("a", id: "a1"),
      Entry("b", id: "b1", enabled: false),
      Entry("c", id: "c1"),
    }, version: 0);

    plan.Entries.Select(e => e.SequenceId).Should().Equal("a", "c");
    plan.Keys.Should().Equal("a1", "c1");
  }

  [Fact]
  public void KeepsTemplateOrderInEachPartition() {
    var plan = QueueRunPlan.Build(new[] {
      Entry("x", id: "1"), Entry("y", id: "2"), Entry("z", id: "3"),
    }, version: 0);

    plan.OncePerRun.Select(p => p.Entry.SequenceId).Should().Equal("x", "y", "z");
    plan.OncePerRun.Select(p => p.Position).Should().Equal(0, 1, 2);
  }

  [Fact]
  public void AnEntryWithNoIdGetsAnIndexKeyFromItsPositionInTheWholeTemplate() {
    var plan = QueueRunPlan.Build(new[] {
      Entry("a"),
      Entry("b", enabled: false),
      Entry("c"),
    }, version: 0);

    plan.Keys.Should().Equal("idx:0", "idx:2");
  }

  [Fact]
  public void TwoEntriesOfOneSequenceGetTwoKeys() {
    var plan = QueueRunPlan.Build(new[] { Entry("same", id: "1"), Entry("same", id: "2") }, version: 0);

    plan.Keys.Should().Equal("1", "2");
    plan.OncePerRun.Select(p => p.Key).Should().Equal("1", "2");
  }

  [Fact]
  public void FindsAnEntryByKey() {
    var plan = QueueRunPlan.Build(new[] { Entry("a", id: "1"), Entry("b", id: "2") }, version: 0);

    plan.TryGetByKey("2", out var entry).Should().BeTrue();
    entry.SequenceId.Should().Be("b");
    plan.TryGetByKey("missing", out _).Should().BeFalse();
  }

  [Fact]
  public void BuildFromAListKeepsEveryEntryAndKeysByListPosition() {
    var plan = QueueRunPlan.Build(new[] { Entry("a"), Entry("b", enabled: false) });

    plan.Entries.Should().HaveCount(2);
    plan.Keys.Should().Equal("idx:0", "idx:1");
    plan.Version.Should().Be(0);
  }

  [Fact]
  public void IndexOfKeyAndIndexOfFindTheEntry() {
    var a = Entry("a", id: "1");
    var b = Entry("b", id: "2");
    var plan = QueueRunPlan.Build(new[] { a, b }, version: 0);

    plan.IndexOfKey("2").Should().Be(1);
    plan.IndexOfKey("none").Should().Be(-1);
    plan.IndexOf(a).Should().Be(0);
    plan.ContainsKey("1").Should().BeTrue();
  }

  [Fact]
  public void CarriesTheVersion() {
    QueueRunPlan.Build(System.Array.Empty<QueueTemplateEntry>(), version: 7).Version.Should().Be(7);
  }

  [Fact]
  public void AnEmptyTemplateGivesAnEmptyPlan() {
    var plan = QueueRunPlan.Build(System.Array.Empty<QueueTemplateEntry>(), version: 0);

    plan.Entries.Should().BeEmpty();
    plan.HasLoopWork.Should().BeFalse();
  }

  [Fact]
  public void HasLoopWorkWhenAnOncePerRunEveryStepOrTimerEntryExists() {
    QueueRunPlan.Build(new[] { Entry("a", ScheduleType.OncePerRun, "1") }, 0).HasLoopWork.Should().BeTrue();
    QueueRunPlan.Build(new[] { Entry("a", ScheduleType.EveryStep, "1") }, 0).HasLoopWork.Should().BeTrue();
    QueueRunPlan.Build(new[] { Entry("a", ScheduleType.Timer, "1") }, 0).HasLoopWork.Should().BeTrue();
    QueueRunPlan.Build(new[] { Entry("a", ScheduleType.AtQueueStart, "1") }, 0).HasLoopWork.Should().BeFalse();
  }
}

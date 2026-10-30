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
/// Feature 171: the differences between the entries of a running queue (the run plan) and the entries of its
/// linked template. Each difference has a key <c>&lt;type&gt;:&lt;entryId&gt;</c>. Entries match by entry ID, so two entries of
/// one sequence stay separate.
/// </summary>
public sealed class QueueTemplateDiffTests {
  private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string> {
    ["seq-a"] = "Alpha", ["seq-b"] = "Bravo", ["seq-c"] = "Charlie"
  };

  private static QueueTemplateEntry Entry(string id, string sequence, bool enabled = true, ScheduleType type = ScheduleType.OncePerRun) =>
    new() { EntryId = id, SequenceId = sequence, Enabled = enabled, ScheduleType = type };

  private static QueueTemplate Template(params QueueTemplateEntry[] entries) {
    var template = new QueueTemplate { Id = "t1", Name = "T" };
    foreach (var e in entries) template.Entries.Add(e);
    return template;
  }

  private static QueueRunPlan PlanOf(params QueueTemplateEntry[] entries) => QueueRunPlan.Build(entries, version: 0);

  private static TemplateDiffResult Diff(QueueRunPlan plan, QueueTemplate template) =>
    QueueTemplateDiff.Compute(plan, template, Names);

  [Fact]
  public void NoDifferenceGivesAnEmptyList() {
    var entries = new[] { Entry("1", "seq-a"), Entry("2", "seq-b") };

    var result = Diff(PlanOf(entries), Template(entries.Select(e => e.Clone()).ToArray()));

    result.Differences.Should().BeEmpty();
    result.NeedsUpgrade.Should().BeFalse();
  }

  [Fact]
  public void ANewEntryIsAdded() {
    var result = Diff(PlanOf(Entry("1", "seq-a")), Template(Entry("1", "seq-a"), Entry("2", "seq-b")));

    var difference = result.Differences.Should().ContainSingle().Subject;
    difference.Type.Should().Be("added");
    difference.Key.Should().Be("added:2");
    difference.EntryId.Should().Be("2");
    difference.SequenceName.Should().Be("Bravo");
    difference.SelectedByDefault.Should().BeTrue();
    difference.AffectsSchedule.Should().BeTrue();
    difference.Summary.Should().Contain("Bravo");
  }

  [Fact]
  public void ARemovedEntryIsRemoved() {
    var result = Diff(PlanOf(Entry("1", "seq-a"), Entry("2", "seq-b")), Template(Entry("1", "seq-a")));

    var difference = result.Differences.Should().ContainSingle().Subject;
    difference.Type.Should().Be("removed");
    difference.Key.Should().Be("removed:2");
    difference.SelectedByDefault.Should().BeTrue();
    difference.AffectsSchedule.Should().BeFalse();
  }

  [Fact]
  public void AnEntryThatWasEnabledAndIsNowDisabledIsDisabled() {
    var result = Diff(PlanOf(Entry("1", "seq-a")), Template(Entry("1", "seq-a", enabled: false)));

    result.Differences.Should().ContainSingle().Which.Type.Should().Be("disabled");
  }

  [Fact]
  public void AnEntryThatWasDisabledAtTheStartAndIsNowEnabledIsEnabledNotAdded() {
    var result = Diff(PlanOf(Entry("1", "seq-a", enabled: false)), Template(Entry("1", "seq-a", enabled: true)));

    var difference = result.Differences.Should().ContainSingle().Subject;
    difference.Type.Should().Be("enabled");
    difference.AffectsSchedule.Should().BeTrue();
  }

  [Fact]
  public void ADisabledNewEntryIsAddedAndStaysDisabled() {
    var result = Diff(PlanOf(Entry("1", "seq-a")), Template(Entry("1", "seq-a"), Entry("2", "seq-b", enabled: false)));

    result.Differences.Should().ContainSingle().Which.Type.Should().Be("added");
  }

  [Fact]
  public void ChangedParameterValuesGiveAParametersDifference() {
    var live = Entry("1", "seq-a");
    live.ParameterValues.Add(new ParameterBinding { Name = "count", Value = "3" });
    var target = Entry("1", "seq-a");
    target.ParameterValues.Add(new ParameterBinding { Name = "count", Value = "5" });

    var result = Diff(PlanOf(live), Template(target));

    var difference = result.Differences.Should().ContainSingle().Subject;
    difference.Type.Should().Be("parameters");
    difference.Key.Should().Be("parameters:1");
    difference.AffectsSchedule.Should().BeFalse();
    difference.SelectedByDefault.Should().BeTrue();
  }

  [Fact]
  public void ASequenceChangeOfOneEntryCountsAsAParametersDifference() {
    var result = Diff(PlanOf(Entry("1", "seq-a")), Template(Entry("1", "seq-b")));

    var difference = result.Differences.Should().ContainSingle().Subject;
    difference.Type.Should().Be("parameters");
    difference.Summary.Should().Contain("sequence");
  }

  [Fact]
  public void TwoEntriesOfOneSequenceStaySeparate() {
    var live = new[] { Entry("1", "seq-a"), Entry("2", "seq-a") };
    var target = Entry("2", "seq-a");
    target.ParameterValues.Add(new ParameterBinding { Name = "n", Value = "9" });

    var result = Diff(PlanOf(live), Template(Entry("1", "seq-a"), target));

    result.Differences.Should().ContainSingle().Which.Key.Should().Be("parameters:2");
  }

  [Fact]
  public void SeveralChangesOfOneEntryGiveSeveralDifferences() {
    var live = Entry("1", "seq-a");
    var target = Entry("1", "seq-a", enabled: false);
    target.ParameterValues.Add(new ParameterBinding { Name = "n", Value = "9" });

    var result = Diff(PlanOf(live), Template(target));

    result.Differences.Select(d => d.Type).Should().BeEquivalentTo("disabled", "parameters");
  }

  [Fact]
  public void ATemplateThatNeedsAnUpgradeGivesNoDifferenceAndTheFlag() {
    var result = Diff(PlanOf(Entry("1", "seq-a")), Template(Entry("", "seq-a")));

    result.NeedsUpgrade.Should().BeTrue();
    result.Differences.Should().BeEmpty();
  }

  [Fact]
  public void APlanWithIndexKeysGivesNoDifferenceAndTheFlag() {
    var result = Diff(PlanOf(Entry("", "seq-a")), Template(Entry("1", "seq-a")));

    result.NeedsUpgrade.Should().BeTrue();
    result.Differences.Should().BeEmpty();
  }

  [Fact]
  public void TheBaseTokenIsTheSameForTheSameContentAndChangesWithAnEdit() {
    var first = QueueTemplateDiff.ComputeBaseToken(Template(Entry("1", "seq-a")));
    var same = QueueTemplateDiff.ComputeBaseToken(Template(Entry("1", "seq-a")));
    var edited = Template(Entry("1", "seq-a"));
    edited.Entries[0].ParameterValues.Add(new ParameterBinding { Name = "n", Value = "1" });

    same.Should().Be(first);
    QueueTemplateDiff.ComputeBaseToken(edited).Should().NotBe(first);
    first.Should().StartWith("sha256:");
  }

  [Fact]
  public void TheBaseTokenChangesWhenTheOrderChanges() {
    var one = QueueTemplateDiff.ComputeBaseToken(Template(Entry("1", "seq-a"), Entry("2", "seq-b")));
    var two = QueueTemplateDiff.ComputeBaseToken(Template(Entry("2", "seq-b"), Entry("1", "seq-a")));

    two.Should().NotBe(one);
  }

  [Fact]
  public void TheResultCarriesTheTokenOfTheTemplate() {
    var template = Template(Entry("1", "seq-a"));

    Diff(PlanOf(Entry("1", "seq-a")), template).BaseToken.Should().Be(QueueTemplateDiff.ComputeBaseToken(template));
  }

  [Fact]
  public void AnUnknownSequenceNameFallsBackToTheSequenceId() {
    var result = Diff(PlanOf(Entry("1", "seq-a")), Template(Entry("1", "seq-a"), Entry("2", "seq-gone")));

    result.Differences.Should().ContainSingle().Which.SequenceName.Should().Be("seq-gone");
  }

  [Fact]
  public void TheDifferencesComeInTemplateOrderThenRemovals() {
    var result = Diff(
      PlanOf(Entry("1", "seq-a"), Entry("2", "seq-b")),
      Template(Entry("1", "seq-a", enabled: false), Entry("3", "seq-c")));

    result.Differences.Select(d => d.Key).Should().Equal("disabled:1", "added:3", "removed:2");
  }
}

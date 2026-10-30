using System.Linq;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using Xunit;

namespace GameBot.UnitTests.QueueTemplates;

/// <summary>
/// Feature 171: the manual upgrade gives IDs to the entries that have none. It changes no other field and
/// is safe to repeat.
/// </summary>
public sealed class QueueTemplateUpgradeTests {
  private static QueueTemplate OldTemplate() {
    var template = new QueueTemplate { Id = "t1", Name = "Old" };
    template.Entries.Add(new QueueTemplateEntry {
      SequenceId = "seq-a",
      ScheduleType = ScheduleType.Timer,
      TimerTimeOfDay = new System.TimeOnly(6, 0),
      Enabled = false
    });
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-a" });
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-b", ScheduleType = ScheduleType.EveryStep });
    return template;
  }

  [Fact]
  public void GivesAnIdToEachEntryWithNone() {
    var template = OldTemplate();

    var changed = QueueTemplateUpgrade.Apply(template);

    changed.Should().Be(3);
    template.Entries.Should().OnlyContain(e => !string.IsNullOrEmpty(e.EntryId));
    template.NeedsUpgrade.Should().BeFalse();
  }

  [Fact]
  public void IdsAreUniqueAlsoForTwoEntriesOfOneSequence() {
    var template = OldTemplate();

    QueueTemplateUpgrade.Apply(template);

    template.Entries.Select(e => e.EntryId).Distinct().Should().HaveCount(3);
  }

  [Fact]
  public void ChangesNoOtherField() {
    var template = OldTemplate();

    QueueTemplateUpgrade.Apply(template);

    template.Entries[0].SequenceId.Should().Be("seq-a");
    template.Entries[0].ScheduleType.Should().Be(ScheduleType.Timer);
    template.Entries[0].TimerTimeOfDay.Should().Be(new System.TimeOnly(6, 0));
    template.Entries[0].Enabled.Should().BeFalse();
    template.Entries[2].ScheduleType.Should().Be(ScheduleType.EveryStep);
    template.Name.Should().Be("Old");
  }

  [Fact]
  public void RepeatChangesNothing() {
    var template = OldTemplate();
    QueueTemplateUpgrade.Apply(template);
    var ids = template.Entries.Select(e => e.EntryId).ToList();

    var changed = QueueTemplateUpgrade.Apply(template);

    changed.Should().Be(0);
    template.Entries.Select(e => e.EntryId).Should().Equal(ids);
  }

  [Fact]
  public void KeepsAnExistingIdAndFillsOnlyTheMissingOnes() {
    var template = OldTemplate();
    template.Entries[1].EntryId = "keepme";

    var changed = QueueTemplateUpgrade.Apply(template);

    changed.Should().Be(2);
    template.Entries[1].EntryId.Should().Be("keepme");
  }

  [Fact]
  public void EmptyTemplateChangesNothing() {
    QueueTemplateUpgrade.Apply(new QueueTemplate { Name = "Empty" }).Should().Be(0);
  }

  [Fact]
  public void NewEntryIdIsAGuidInFormatN() {
    QueueTemplateUpgrade.NewEntryId().Should().MatchRegex("^[0-9a-f]{32}$");
  }
}

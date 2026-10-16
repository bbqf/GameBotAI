using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.QueueTemplates;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T032): the booking of disabled entries.</summary>
public sealed class TemplateEntryBookerTests {
  private static QueueTemplate TemplateWithTimer() {
    var t = new QueueTemplate { Id = "t", Name = "Daily" };
    t.Entries.Add(new QueueTemplateEntry {
      SequenceId = "s1",
      Enabled = true,
      ScheduleType = ScheduleType.Timer,
      TimerTimeOfDay = new System.TimeOnly(3, 30)
    });
    t.Entries.Add(new QueueTemplateEntry { SequenceId = "s2", Enabled = false });
    return t;
  }

  [Fact]
  public void ANewSequenceGetsOneDisabledEntryWithNoScheduleAndNoValues() {
    var template = TemplateWithTimer();

    var booked = TemplateEntryBooker.Book(template, new[] { new NewSequence("s3", "gamma") });

    booked.Should().ContainSingle().Which.Should().Be(new EntryRef("Daily", "gamma", 2));
    var entry = template.Entries[2];
    entry.SequenceId.Should().Be("s3");
    entry.Enabled.Should().BeFalse();
    entry.ScheduleType.Should().Be(ScheduleType.OncePerRun);
    entry.TimerTimeOfDay.Should().BeNull();
    entry.TimerRelativeOffset.Should().BeNull();
    entry.ParameterValues.Should().BeEmpty();
  }

  [Fact]
  public void ExistingEntriesDoNotChange() {
    var template = TemplateWithTimer();

    TemplateEntryBooker.Book(template, new[] { new NewSequence("s3", "gamma") });

    template.Entries[0].Enabled.Should().BeTrue();
    template.Entries[0].ScheduleType.Should().Be(ScheduleType.Timer);
    template.Entries[0].TimerTimeOfDay.Should().Be(new System.TimeOnly(3, 30));
    template.Entries[1].Enabled.Should().BeFalse();
  }

  [Fact]
  public void ASequenceThatTheTemplateAlreadyReferencesGetsNoSecondEntry() {
    var template = TemplateWithTimer();

    var booked = TemplateEntryBooker.Book(template, new[] { new NewSequence("S1", "alpha") });

    booked.Should().BeEmpty();
    template.Entries.Should().HaveCount(2);
  }

  [Fact]
  public void PlanDoesNotChangeTheTemplate() {
    var template = TemplateWithTimer();

    var planned = TemplateEntryBooker.Plan(template, new[] { new NewSequence("s3", "gamma"), new NewSequence("s4", "delta") });

    planned.Select(p => p.Position).Should().Equal(2, 3);
    template.Entries.Should().HaveCount(2);
  }

  [Fact]
  public void AnEntryWithARemovedSequenceIsFoundAndStays() {
    var template = TemplateWithTimer();

    var found = TemplateEntryBooker.FindRemovedSequenceEntries(template, new[] { new NewSequence("s2", "beta") });

    found.Should().ContainSingle().Which.Should().Be(new EntryRef("Daily", "beta", 1));
    template.Entries.Should().HaveCount(2);
  }
}

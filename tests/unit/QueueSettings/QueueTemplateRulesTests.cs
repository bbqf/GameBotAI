using System;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueSettings;
using GameBot.Domain.QueueTemplates;
using Xunit;

#pragma warning disable CA1707, CA1861

namespace GameBot.UnitTests.QueueSettings;

/// <summary>Feature 150 (T006): the shared queue template rules.</summary>
public sealed class QueueTemplateRulesTests {
  [Theory]
  [InlineData("Daily Farm")]
  [InlineData("a_b-c 1")]
  public void AValidNameGivesNoError(string name) => QueueTemplateRules.ValidateName(name).Should().BeNull();

  [Theory]
  [InlineData(null, "name is required")]
  [InlineData("  ", "name is required")]
  [InlineData("bad/name", "name may contain only letters, digits, spaces, hyphens, and underscores")]
  public void ABadNameGivesTheRouteText(string? name, string expected) => QueueTemplateRules.ValidateName(name).Should().Be(expected);

  [Fact]
  public void ANameOver100CharactersIsRefused() =>
    QueueTemplateRules.ValidateName(new string('a', 101)).Should().Be("name must be 100 characters or fewer");

  [Fact]
  public void ANameOf100CharactersIsValid() => QueueTemplateRules.ValidateName(new string('a', 100)).Should().BeNull();

  [Theory]
  [InlineData(ScheduleType.Timer, false, false, true)]
  [InlineData(ScheduleType.Timer, true, true, true)]
  [InlineData(ScheduleType.Timer, true, false, false)]
  [InlineData(ScheduleType.Timer, false, true, false)]
  [InlineData(ScheduleType.OncePerRun, false, false, false)]
  public void ATimerEntryNeedsExactlyOneTimerField(ScheduleType type, bool timeOfDay, bool offset, bool error) =>
    (QueueTemplateRules.ValidateTimerFields(type, timeOfDay, offset) is not null).Should().Be(error);

  [Fact]
  public void AParameterNameErrorIsReportedForEachBadName() {
    var errors = QueueTemplateRules.ValidateParameterNames(new[] {
      new ParameterBinding { Name = "good", Value = "1" },
      new ParameterBinding { Name = "queue.x", Value = "1" },
      new ParameterBinding { Name = "1bad", Value = "1" }
    });

    errors.Select(e => e.Name).Should().Equal("queue.x", "1bad");
  }

  [Fact]
  public void AValidDocGivesNoError() {
    var doc = new QueueTemplateDoc {
      Name = "Daily",
      Entries = new[] { new QueueTemplateEntryDoc { Sequence = "Help All", ScheduleType = ScheduleType.Timer, TimerTimeOfDay = new TimeOnly(3, 0) } }
    };

    QueueTemplateRules.ValidateDoc(doc).Should().BeEmpty();
  }

  [Fact]
  public void AnInvalidDocGivesOneErrorForEachProblem() {
    var doc = new QueueTemplateDoc {
      Name = "bad/name",
      Entries = new[] {
        new QueueTemplateEntryDoc { Sequence = " " },
        new QueueTemplateEntryDoc { Sequence = "S", ScheduleType = ScheduleType.Timer },
        new QueueTemplateEntryDoc { Sequence = "S", TimerTimeOfDay = new TimeOnly(1, 0) },
        new QueueTemplateEntryDoc { Sequence = "S", ParameterValues = new[] { new ParameterBinding { Name = "queue.x", Value = "1" } } },
        new QueueTemplateEntryDoc { Sequence = "S", ScheduleType = (ScheduleType)99 }
      }
    };

    var errors = QueueTemplateRules.ValidateDoc(doc);

    errors.Should().HaveCount(6);
    errors[0].Should().Contain("name may contain only");
    errors.Should().Contain(e => e.Contains("Entry 0 has no sequence name"));
    errors.Should().Contain(e => e.Contains("Entry 1 must set exactly one of timerTimeOfDay or timerRelativeOffset"));
    errors.Should().Contain(e => e.Contains("Entry 2 sets a timer field"));
    errors.Should().Contain(e => e.Contains("Entry 3: parameter value 'queue.x'"));
    errors.Should().Contain(e => e.Contains("Entry 4 has a schedule type that is not valid"));
  }
}

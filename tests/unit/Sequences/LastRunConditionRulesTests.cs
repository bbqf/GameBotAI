using System;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 105: the field rules and the strict parsers of the <c>lastRun</c> condition.
/// </summary>
public sealed class LastRunConditionRulesTests {
  [Theory]
  [InlineData("00:00", 0, 0)]
  [InlineData("11:00", 11, 0)]
  [InlineData("23:59", 23, 59)]
  public void TryParseSinceAcceptsStrictHourMinute(string text, int hour, int minute) {
    LastRunConditionRules.TryParseSince(text, out var since).Should().BeTrue();
    since.Should().Be(new TimeOnly(hour, minute));
  }

  [Theory]
  [InlineData("9:00")]
  [InlineData("24:00")]
  [InlineData("11:00:00")]
  [InlineData("")]
  [InlineData(null)]
  public void TryParseSinceRejectsOtherForms(string? text) {
    LastRunConditionRules.TryParseSince(text, out _).Should().BeFalse();
  }

  [Theory]
  [InlineData("24:00:00", 24 * 60)]
  [InlineData("1.00:00:00", 24 * 60)]
  [InlineData("00:30:00", 30)]
  [InlineData("366.00:00:00", 366 * 24 * 60)]
  public void TryParseWithinReadsHoursAsHours(string text, int expectedMinutes) {
    LastRunConditionRules.TryParseWithin(text, out var within).Should().BeTrue();
    within.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
  }

  [Theory]
  [InlineData("00:00:00")]
  [InlineData("-01:00:00")]
  [InlineData("367.00:00:00")]
  [InlineData("366.00:00:01")]
  [InlineData("1:00")]
  [InlineData("abc")]
  [InlineData("")]
  [InlineData(null)]
  public void TryParseWithinRejectsBadValues(string? text) {
    LastRunConditionRules.TryParseWithin(text, out _).Should().BeFalse();
  }

  public static TheoryData<LastRunStepCondition, string> BadConditions => new() {
    { new LastRunStepCondition { Sequence = "", Status = "success", Since = "11:00" }, "lastRun condition requires sequence ('self' or a sequence id)." },
    { new LastRunStepCondition { Sequence = "  ", Status = "success", Since = "11:00" }, "lastRun condition requires sequence ('self' or a sequence id)." },
    { new LastRunStepCondition { Sequence = "self", Status = "failed", Since = "11:00" }, "lastRun status must be one of success|failure|cancelled." },
    { new LastRunStepCondition { Sequence = "self", Status = "", Since = "11:00" }, "lastRun status must be one of success|failure|cancelled." },
    { new LastRunStepCondition { Sequence = "self", Status = "success", Since = "11:00", Within = "01:00:00" }, "lastRun condition accepts only one of since or within, not both." },
    { new LastRunStepCondition { Sequence = "self", Status = "success" }, "lastRun condition requires one of since or within." },
    { new LastRunStepCondition { Sequence = "self", Status = "success", Since = "9:00" }, "lastRun since must be a time of day in HH:mm format (00:00 to 23:59)." },
    { new LastRunStepCondition { Sequence = "self", Status = "success", Within = "abc" }, "lastRun within must be a duration more than zero and not more than 366 days, in hh:mm:ss or d.hh:mm:ss format." }
  };

  [Theory]
  [MemberData(nameof(BadConditions))]
  public void ValidateReturnsTheMessageTailOfTheContractTable(LastRunStepCondition condition, string expected) {
    LastRunConditionRules.Validate(condition).Should().ContainSingle().Which.Should().Be(expected);
  }

  [Theory]
  [InlineData("success")]
  [InlineData("SUCCESS")]
  [InlineData("Failure")]
  [InlineData("cAnCeLlEd")]
  public void ValidateAcceptsStatusInAnyCase(string status) {
    var condition = new LastRunStepCondition { Sequence = "self", Status = status, Within = "24:00:00" };
    LastRunConditionRules.Validate(condition).Should().BeEmpty();
  }
}

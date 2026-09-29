using System;
using FluentAssertions;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>Issue #226: the shared strict rule for the timerTimeOfDay text.</summary>
public sealed class TimerTimeOfDayFormatTests {
  [Theory]
  [InlineData("00:00")]
  [InlineData("09:05")]
  [InlineData("15:30")]
  [InlineData("23:59")]
  [InlineData("00:00:00")]
  [InlineData("15:30:45")]
  [InlineData("23:59:59")]
  public void AcceptsStrictForms(string value) {
    TimerTimeOfDayFormat.TryParse(value, out _).Should().BeTrue();
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("24:00")]
  [InlineData("24:00:00")]
  [InlineData("23:60")]
  [InlineData("12:00:60")]
  [InlineData("9:30")]
  [InlineData("9:30:00")]
  [InlineData("11:00 PM")]
  [InlineData(" 15:30")]
  [InlineData("15:30 ")]
  [InlineData("15:30:")]
  [InlineData("15.30")]
  [InlineData("1530")]
  [InlineData("15:30:45.123")]
  [InlineData("abc")]
  public void RejectsAllOtherForms(string? value) {
    TimerTimeOfDayFormat.TryParse(value, out _).Should().BeFalse();
  }

  [Fact]
  public void ParsesTheParts() {
    TimerTimeOfDayFormat.TryParse("15:30:45", out var result).Should().BeTrue();
    result.Should().Be(new TimeOnly(15, 30, 45));
  }

  [Fact]
  public void FormatWritesShortFormWhenSecondsAreZero() {
    TimerTimeOfDayFormat.Format(new TimeOnly(15, 30, 0)).Should().Be("15:30");
  }

  [Fact]
  public void FormatWritesLongFormWhenSecondsAreNotZero() {
    TimerTimeOfDayFormat.Format(new TimeOnly(15, 30, 45)).Should().Be("15:30:45");
  }

  [Fact]
  public void FormatDropsPartsOfASecond() {
    TimerTimeOfDayFormat.Format(new TimeOnly(15, 30, 0).Add(TimeSpan.FromTicks(1234))).Should().Be("15:30");
    TimerTimeOfDayFormat.Format(new TimeOnly(15, 30, 45).Add(TimeSpan.FromTicks(1234))).Should().Be("15:30:45");
  }

  [Theory]
  [InlineData(0, 0, 0)]
  [InlineData(15, 30, 0)]
  [InlineData(15, 30, 45)]
  [InlineData(23, 59, 59)]
  public void FormatThenParseGivesTheSameValue(int hour, int minute, int second) {
    var value = new TimeOnly(hour, minute, second);
    TimerTimeOfDayFormat.TryParse(TimerTimeOfDayFormat.Format(value), out var result).Should().BeTrue();
    result.Should().Be(value);
  }
}

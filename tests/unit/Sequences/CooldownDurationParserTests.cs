using System;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 068: tolerant extraction of a countdown duration from noisy OCR text.</summary>
public sealed class CooldownDurationParserTests {
  [Fact]
  public void ParsesHhMmSs() {
    CooldownDurationParser.TryParse("00:05:42", out var value).Should().BeTrue();
    value.Should().Be(new TimeSpan(0, 5, 42));
  }

  [Fact]
  public void ParsesMmSs() {
    CooldownDurationParser.TryParse("01:20", out var value).Should().BeTrue();
    value.Should().Be(new TimeSpan(0, 1, 20));
  }

  [Fact]
  public void ParsesHoursForm() {
    CooldownDurationParser.TryParse("02:30:15", out var value).Should().BeTrue();
    value.Should().Be(new TimeSpan(2, 30, 15));
  }

  [Theory]
  [InlineData("Next in 00:05:42 remaining")]
  [InlineData("  \n00:05:42\t")]
  [InlineData("[[00:05:42]]")]
  public void ExtractsFromSurroundingNoise(string text) {
    CooldownDurationParser.TryParse(text, out var value).Should().BeTrue();
    value.Should().Be(new TimeSpan(0, 5, 42));
  }

  [Fact]
  public void NormalizesDigitConfusions() {
    // 'O' -> '0', 'l'/'I'/'|' -> '1'
    CooldownDurationParser.TryParse("OO:O5:42", out var oValue).Should().BeTrue();
    oValue.Should().Be(new TimeSpan(0, 5, 42));

    CooldownDurationParser.TryParse("Ol:2O", out var lValue).Should().BeTrue();
    lValue.Should().Be(new TimeSpan(0, 1, 20));
  }

  [Fact]
  public void ParsesZeroAsSuccess() {
    // Zero is a valid parse; bounds-checking (not the parser) rejects it downstream.
    CooldownDurationParser.TryParse("00:00:00", out var value).Should().BeTrue();
    value.Should().Be(TimeSpan.Zero);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  [InlineData("no timer here")]
  [InlineData("Attack")]
  [InlineData("12")]
  public void ReturnsFalseOnGarbage(string? text) {
    CooldownDurationParser.TryParse(text, out var value).Should().BeFalse();
    value.Should().Be(TimeSpan.Zero);
  }

  [Fact]
  public void ReturnsFalseOnOverflow() {
    CooldownDurationParser.TryParse("999999999999999999999:00:00", out _).Should().BeFalse();
  }

  // Day part directly before the time token adds N days.
  [Theory]
  [InlineData("1d 23:29:10", 1, 23, 29, 10)]
  [InlineData("Free in 1d 23:29:10", 1, 23, 29, 10)]
  [InlineData("1d23:29:10", 1, 23, 29, 10)]
  [InlineData("1D 23:29:10", 1, 23, 29, 10)]
  [InlineData("11d 23:29:10", 11, 23, 29, 10)]
  [InlineData("1d 05:30", 1, 0, 5, 30)]
  public void AddsTheDayPart(string text, int days, int hours, int minutes, int seconds) {
    CooldownDurationParser.TryParse(text, out var value).Should().BeTrue();
    value.Should().Be(new TimeSpan(days, hours, minutes, seconds));
  }

  // A day token that is not directly before the time token does not count.
  [Theory]
  [InlineData("Reward 23:29:10")]
  [InlineData("3days 23:29:10")]
  [InlineData("23:29:10 1d")]
  [InlineData("1d left 23:29:10")]
  [InlineData("1w 23:29:10")]
  [InlineData("1 d 23:29:10")]
  public void IgnoresADayTokenThatIsNotDirectlyBeforeTheTime(string text) {
    CooldownDurationParser.TryParse(text, out var value).Should().BeTrue();
    value.Should().Be(new TimeSpan(23, 29, 10));
  }

  [Theory]
  [InlineData("999999999999d 00:00:01")]
  [InlineData("2147483647d 00:00:01")]
  public void ReturnsFalseOnDayOverflow(string text) {
    CooldownDurationParser.TryParse(text, out _).Should().BeFalse();
  }

  [Theory]
  [InlineData("2d", 2)]
  [InlineData("Free in 2d", 2)]
  [InlineData("Id", 1)]
  [InlineData("ld", 1)]
  public void ReadsADayCountAlone(string text, int days) {
    CooldownDurationParser.TryParse(text, out var value).Should().BeTrue();
    value.Should().Be(TimeSpan.FromDays(days));
  }

  [Theory]
  [InlineData("Reward")]
  [InlineData("3days")]
  public void ReturnsFalseOnTextWithoutDayOrTime(string text) {
    CooldownDurationParser.TryParse(text, out _).Should().BeFalse();
  }

  [Fact]
  public void KeepsTheOldResultWithoutADayPart() {
    CooldownDurationParser.TryParse("23:29:10", out var hms).Should().BeTrue();
    hms.Should().Be(new TimeSpan(23, 29, 10));
    CooldownDurationParser.TryParse("05:30", out var ms).Should().BeTrue();
    ms.Should().Be(new TimeSpan(0, 5, 30));
  }

  [Fact]
  public void TakesTheFirstDurationToken() {
    CooldownDurationParser.TryParse("first 00:01:00 then 00:09:00", out var value).Should().BeTrue();
    value.Should().Be(new TimeSpan(0, 1, 0));
  }
}

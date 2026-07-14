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

  [Fact]
  public void TakesTheFirstDurationToken() {
    CooldownDurationParser.TryParse("first 00:01:00 then 00:09:00", out var value).Should().BeTrue();
    value.Should().Be(new TimeSpan(0, 1, 0));
  }
}

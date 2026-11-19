using System;
using FluentAssertions;
using GameBot.Emulator.Adb;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 192 and 195: the split of a text into characters, the space check, and the quotes for the
/// device shell.
/// </summary>
public sealed class AdbTextEncoderTests {
  [Fact]
  public void ANumberGivesOnePartForEachCharacter() {
    AdbTextEncoder.SplitCharacters("250").Should().Equal("2", "5", "0");
  }

  [Fact]
  public void ASpaceIsItsOwnPart() {
    AdbTextEncoder.SplitCharacters("25 0").Should().Equal("2", "5", " ", "0");
  }

  [Fact]
  public void PercentSAndAQuoteAreSeparateParts() {
    AdbTextEncoder.SplitCharacters("%s'").Should().Equal("%", "s", "'");
  }

  [Fact]
  public void AnEmptyTextGivesNoPart() {
    AdbTextEncoder.SplitCharacters(string.Empty).Should().BeEmpty();
  }

  [Fact]
  public void ANullTextThrows() {
    var act = () => AdbTextEncoder.SplitCharacters(null!);
    act.Should().Throw<ArgumentNullException>();
  }

  [Fact]
  public void IsSpaceIsTrueOnlyForASpace() {
    AdbTextEncoder.IsSpace(" ").Should().BeTrue();
    AdbTextEncoder.IsSpace("a").Should().BeFalse();
  }

  [Fact]
  public void QuoteForDeviceShellPutsTheChunkInSingleQuotes() {
    AdbTextEncoder.QuoteForDeviceShell("abc").Should().Be("'abc'");
  }

  [Fact]
  public void QuoteForDeviceShellEscapesASingleQuote() {
    AdbTextEncoder.QuoteForDeviceShell("it's").Should().Be("'it'\\''s'");
  }

  [Fact]
  public void QuoteForDeviceShellKeepsOtherShellCharacters() {
    AdbTextEncoder.QuoteForDeviceShell("&;|<>()$\"\\*?").Should().Be("'&;|<>()$\"\\*?'");
  }
}

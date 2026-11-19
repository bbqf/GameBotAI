using FluentAssertions;
using GameBot.Emulator.Adb;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>Feature 192: the split of a text into chunks and spaces, and the quotes for the device shell.</summary>
public sealed class AdbTextEncoderTests {
  [Fact]
  public void ANumberGivesOneChunk() {
    AdbTextEncoder.Split("250").Should().Equal("250");
  }

  [Fact]
  public void ASpaceIsItsOwnPart() {
    AdbTextEncoder.Split("250 Fighters").Should().Equal("250", " ", "Fighters");
  }

  [Fact]
  public void TwoSpacesGiveTwoSpaceParts() {
    AdbTextEncoder.Split("a  b").Should().Equal("a", " ", " ", "b");
  }

  [Fact]
  public void APercentFollowedBySIsSplit() {
    AdbTextEncoder.Split("50%s").Should().Equal("50%", "s");
  }

  [Fact]
  public void APercentAtTheEndStaysInTheChunk() {
    AdbTextEncoder.Split("100%").Should().Equal("100%");
  }

  [Theory]
  [InlineData("250")]
  [InlineData("250 Fighters")]
  [InlineData(" a%s b %%ss ")]
  [InlineData("it's & ; | < > ( ) $ \" \\ * ?")]
  public void ThePartsInOrderGiveTheOriginalText(string text) {
    string.Concat(AdbTextEncoder.Split(text)).Should().Be(text);
  }

  [Fact]
  public void NoChunkHoldsASpaceOrPercentS() {
    foreach (var part in AdbTextEncoder.Split(" a%s b %%ss ")) {
      if (AdbTextEncoder.IsSpace(part)) continue;
      part.Should().NotContain(" ").And.NotContain("%s");
    }
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

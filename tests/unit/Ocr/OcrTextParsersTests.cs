using FluentAssertions;
using GameBot.Service.Services.Ocr;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Ocr;

/// <summary>Feature 128: the parser registry of the OCR read endpoint.</summary>
public sealed class OcrTextParsersTests {
  [Fact]
  public void CleanCountdownParses() {
    var ok = OcrTextParsers.TryParse("hh:mm:ss", "02:10:35", out var value, out var seconds);

    ok.Should().BeTrue();
    value.Should().Be("02:10:35");
    seconds.Should().Be(7835);
  }

  [Theory]
  [InlineData("hh:mm:ss")]
  [InlineData("HH:MM:SS")]
  [InlineData("Hh:Mm:Ss")]
  public void NameIsAcceptedInAnyLetterCase(string name) {
    OcrTextParsers.TryGetName(name, out var canonical).Should().BeTrue();
    canonical.Should().Be("hh:mm:ss");
  }

  [Fact]
  public void HoursPastTwentyFourAreKept() {
    OcrTextParsers.TryParse("hh:mm:ss", "30:00:00", out var value, out var seconds).Should().BeTrue();
    value.Should().Be("30:00:00");
    seconds.Should().Be(108000);
  }

  [Fact]
  public void NoisyTextThatHasNoDurationDoesNotParse() {
    OcrTextParsers.TryParse("hh:mm:ss", "8 ODeIOs35", out _, out _).Should().BeFalse();
  }

  [Theory]
  [InlineData("nope")]
  [InlineData("")]
  [InlineData(null)]
  public void UnknownNameIsNotFound(string? name) {
    OcrTextParsers.TryGetName(name, out _).Should().BeFalse();
  }
}

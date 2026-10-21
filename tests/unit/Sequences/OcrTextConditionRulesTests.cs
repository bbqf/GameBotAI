using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;
using static GameBot.UnitTests.Sequences.OcrTextTestKit;

namespace GameBot.UnitTests.Sequences;

/// <summary>The field rules and the match rules of the ocrText condition (issue #338, FR-017, FR-019).</summary>
public sealed class OcrTextConditionRulesTests {
  [Theory]
  [InlineData(0, 10)]
  [InlineData(10, 0)]
  [InlineData(-5, 10)]
  [InlineData(10, -1)]
  public void ARegionWithNoAreaIsRejected(int width, int height) {
    var condition = Expect("0");
    condition.Region = new PixelRegion(1, 1, width, height);

    OcrTextConditionRules.Validate(condition).Should().NotBeEmpty().And.OnlyContain(m => m.Contains("region"));
  }

  [Fact]
  public void AMissingRegionIsRejected() {
    var condition = Expect("0");
    condition.Region = null;

    OcrTextConditionRules.Validate(condition).Should().ContainSingle().Which.Should().Contain("region");
  }

  [Fact]
  public void BothOrNeitherOfExpectedAndPatternIsRejected() {
    var both = Expect("0");
    both.Pattern = "0";
    var neither = Expect("0");
    neither.Expected = null;

    OcrTextConditionRules.Validate(both).Should().ContainSingle().Which.Should().Contain("expected").And.Contain("pattern");
    OcrTextConditionRules.Validate(neither).Should().ContainSingle().Which.Should().Contain("expected").And.Contain("pattern");
  }

  [Fact]
  public void AnInvalidRegularExpressionIsRejectedAndNamesThePattern() {
    OcrTextConditionRules.Validate(Match("(unclosed")).Should().ContainSingle().Which.Should().Contain("pattern");
  }

  [Fact]
  public void AnEmptyExpectedIsAllowed() {
    OcrTextConditionRules.Validate(Expect(string.Empty)).Should().BeEmpty();
  }

  [Fact]
  public void WhiteSpaceAtTheEdgesIsRemovedBeforeTheCompare() {
    OcrTextConditionRules.Matches(Expect("0"), "  0\n", out _).Should().BeTrue();
  }

  [Fact]
  public void ExpectedIsCaseSensitive() {
    OcrTextConditionRules.Matches(Expect("Ready"), "ready", out _).Should().BeFalse();
    OcrTextConditionRules.Matches(Expect("Ready"), "Ready", out _).Should().BeTrue();
  }

  [Fact]
  public void PatternMatchesAnywhereInTheText() {
    OcrTextConditionRules.Matches(Match(@"left: 0$"), "Interaction attempts left: 0", out _).Should().BeTrue();
    OcrTextConditionRules.Matches(Match(@"^left"), "Interaction attempts left: 0", out _).Should().BeFalse();
  }

  [Fact]
  public void APatternTimeOutIsAPlainMismatchWithTheFlag() {
    var condition = Match(@"^(a+)+$");
    var text = new string('a', 60) + "!";

    var result = OcrTextConditionRules.Matches(condition, text, out var timedOut);

    result.Should().BeFalse();
    timedOut.Should().BeTrue();
  }

  [Fact]
  public void AnEmptyTextIsComparedLikeAnyText() {
    OcrTextConditionRules.Matches(Expect(string.Empty), string.Empty, out _).Should().BeTrue();
    OcrTextConditionRules.Matches(Match("^$"), string.Empty, out _).Should().BeTrue();
    OcrTextConditionRules.Matches(Expect("0"), string.Empty, out _).Should().BeFalse();
    OcrTextConditionRules.Matches(Expect("0"), null, out _).Should().BeFalse();
  }
}

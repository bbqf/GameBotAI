using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Services;
using Xunit;
using static GameBot.UnitTests.Sequences.OcrTextTestKit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>The ocrText leaf in the shared condition evaluator (issue #338, FR-009, FR-011, FR-013, FR-017, FR-019).</summary>
public sealed class OcrTextConditionEvaluatorTests {
  [Fact]
  public async Task TheLeafIsTrueWhenTheTextMatchesAndFalseWhenItDoesNot() {
    using (OcrTextEvaluationScope.Push(TextScope("0"))) {
      (await EvaluateAsync(Expect("0"))).Value.Should().BeTrue();
      (await EvaluateAsync(Expect("3"))).Value.Should().BeFalse();
    }
  }

  [Fact]
  public async Task NegateInvertsTheResult() {
    using (OcrTextEvaluationScope.Push(TextScope("0"))) {
      (await EvaluateAsync(Expect("0", negate: true))).Value.Should().BeFalse();
      (await EvaluateAsync(Expect("3", negate: true))).Value.Should().BeTrue();
    }
  }

  [Fact]
  public async Task TheReaderGetsTheConditionOnce() {
    var reads = new System.Collections.Generic.List<GameBot.Domain.Commands.OcrTextStepCondition>();
    using (OcrTextEvaluationScope.Push(TextScope("0", reads))) {
      await EvaluateAsync(Expect("0"));
    }

    reads.Should().ContainSingle();
  }

  [Fact]
  public async Task WithNoScopeTheLeafIsFalseWithTheReasonNoSession() {
    OcrTextEvaluationScope.Current.Should().BeNull();

    (await EvaluateAsync(Expect("0"))).Value.Should().BeFalse();
  }

  [Theory]
  [InlineData(OcrTextFailureReasons.RegionInvalid)]
  [InlineData(OcrTextFailureReasons.RegionOutsideFrame)]
  [InlineData(OcrTextFailureReasons.CaptureFailed)]
  [InlineData(OcrTextFailureReasons.OcrUnavailable)]
  [InlineData(OcrTextFailureReasons.OcrError)]
  public async Task AReaderFailureIsFalseWithThatOneReasonCode(string reason) {
    var scope = FailScope(reason);
    using (OcrTextEvaluationScope.Push(scope)) {
      (await EvaluateAsync(Expect("0"))).Value.Should().BeFalse();
    }

    var record = scope.Drain().Should().ContainSingle().Subject;
    record.Reason.Should().Be(reason);
    record.Result.Should().BeFalse();
    record.ToLogLine().Should().Contain("reason=" + reason);
  }

  [Fact]
  public async Task ANegatedFailureIsTrueBecauseNegateAppliesAfterTheFalseResult() {
    using (OcrTextEvaluationScope.Push(FailScope(OcrTextFailureReasons.CaptureFailed))) {
      (await EvaluateAsync(Expect("0", negate: true))).Value.Should().BeTrue();
    }
  }

  [Fact]
  public async Task AMismatchHasNoReasonCode() {
    var scope = TextScope("3");
    using (OcrTextEvaluationScope.Push(scope)) {
      await EvaluateAsync(Expect("0"));
    }

    var record = scope.Drain().Should().ContainSingle().Subject;
    record.Reason.Should().BeNull();
    record.TextRead.Should().Be("3");
    record.ToLogLine().Should().Be("ocrText: read='3' expected='0' result=false");
  }

  [Fact]
  public async Task APatternTimeOutIsFalseWithNoReasonCodeAndTheNote() {
    var scope = TextScope(new string('a', 60) + "!");
    using (OcrTextEvaluationScope.Push(scope)) {
      (await EvaluateAsync(Match(@"^(a+)+$"))).Value.Should().BeFalse();
    }

    var record = scope.Drain().Should().ContainSingle().Subject;
    record.Reason.Should().BeNull();
    record.Note.Should().Be("pattern timed out");
    record.ToLogLine().Should().Contain("pattern timed out").And.NotContain("reason=");
  }

  [Fact]
  public async Task AnEmptyReadIsComparedLikeAnyText() {
    using (OcrTextEvaluationScope.Push(TextScope(string.Empty))) {
      (await EvaluateAsync(Expect(string.Empty))).Value.Should().BeTrue();
      (await EvaluateAsync(Expect("0"))).Value.Should().BeFalse();
    }
  }

  [Fact]
  public void TheDescriptionShowsTheRegionAndTheMatch() {
    SequenceStepConditionEvaluator.Describe(Expect("0", negate: true)).Should().StartWith("NOT ocrText(").And.Contain("expected=0");
    SequenceStepConditionEvaluator.Describe(Match("a+", parser: "hh:mm:ss")).Should().Contain("pattern=a+").And.Contain("parser=hh:mm:ss");
  }
}

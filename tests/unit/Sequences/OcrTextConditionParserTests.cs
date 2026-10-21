using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Services;
using Xunit;
using static GameBot.UnitTests.Sequences.OcrTextTestKit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>The parser use of the ocrText leaf (issue #338, User Story 3, FR-018).</summary>
public sealed class OcrTextConditionParserTests {
  [Fact]
  public async Task TheParsedValueIsComparedWithExpected() {
    var scope = TextScope("2:10:35", parsed: "02:10:35");
    using (OcrTextEvaluationScope.Push(scope)) {
      (await EvaluateAsync(Expect("02:10:35", parser: "hh:mm:ss"))).Value.Should().BeTrue();
      (await EvaluateAsync(Expect("2:10:35", parser: "hh:mm:ss"))).Value.Should().BeFalse();
    }

    var records = scope.Drain();
    records[0].Compared.Should().Be("02:10:35");
    records[0].TextRead.Should().Be("2:10:35");
  }

  [Fact]
  public async Task TheParsedValueIsComparedWithPattern() {
    using (OcrTextEvaluationScope.Push(TextScope("02:10:35", parsed: "02:10:35"))) {
      (await EvaluateAsync(Match("^02:", parser: "hh:mm:ss"))).Value.Should().BeTrue();
      (await EvaluateAsync(Match("^03:", parser: "hh:mm:ss"))).Value.Should().BeFalse();
    }
  }

  [Fact]
  public async Task AParseFailureIsFalseWithTheReasonAndTheTextRead() {
    var scope = new OcrTextEvaluationScope((_, _) => Task.FromResult(
      new OcrTextReadResult("abc", null, OcrTextFailureReasons.ParseFailed)));
    using (OcrTextEvaluationScope.Push(scope)) {
      (await EvaluateAsync(Expect("02:10:35", parser: "hh:mm:ss"))).Value.Should().BeFalse();
    }

    var record = scope.Drain().Should().ContainSingle().Subject;
    record.Reason.Should().Be("parse-failed");
    record.TextRead.Should().Be("abc");
  }

  [Fact]
  public async Task AReaderThatGivesNoParsedValueForAnUnknownParserIsParseFailed() {
    // A sequence that did not enter by the sequence endpoint can name a parser the reader does not know.
    var scope = TextScope("02:10:35", parsed: null);
    using (OcrTextEvaluationScope.Push(scope)) {
      (await EvaluateAsync(Expect("02:10:35", parser: "no-such-parser"))).Value.Should().BeFalse();
    }

    scope.Drain().Should().ContainSingle().Which.Reason.Should().Be("parse-failed");
  }
}

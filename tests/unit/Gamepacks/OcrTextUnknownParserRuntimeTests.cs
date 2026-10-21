using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Services.Ocr;
using GameBot.Service.Services.SequenceExecution;
using Xunit;
using static GameBot.UnitTests.Sequences.OcrTextTestKit;

#pragma warning disable CA2007, CA1416, CA2000

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// A sequence that did not enter by the sequence endpoint (for example a gamepack import) can name a parser that
/// the service does not know. At run time the condition is false with the reason parse-failed (issue #338, FR-018).
/// </summary>
public sealed class OcrTextUnknownParserRuntimeTests {
  private sealed class FakeFrames : ISessionFrameSource {
    public Bitmap? Capture(string sessionId) => new Bitmap(540, 960);
  }

  private sealed class FakeOcr : ITextOcr {
    public OcrResult Recognize(Bitmap image) => new("02:10:35", 0.9);
    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
  }

  [Fact]
  public async Task AnUnknownParserIsFalseWithParseFailedAtRunTime() {
    var reader = new OcrTextConditionReader(new FakeFrames(), new FakeOcr());
    var scope = new OcrTextEvaluationScope((condition, ct) => reader.ReadAsync(condition, "sess-1", ct));
    var condition = Expect("02:10:35", parser: "no-such-parser");

    using (OcrTextEvaluationScope.Push(scope)) {
      var result = await SequenceStepConditionEvaluator.EvaluateAsync(
        condition, null, new System.Collections.Generic.Dictionary<string, string>(), CancellationToken.None);

      result.Value.Should().BeFalse();
    }

    var record = scope.Drain().Should().ContainSingle().Subject;
    record.Reason.Should().Be(OcrTextFailureReasons.ParseFailed);
    record.TextRead.Should().Be("02:10:35");
  }
}

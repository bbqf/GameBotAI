using System;
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

#pragma warning disable CA2007, CA1416, CA2000

namespace GameBot.UnitTests.Services.Ocr;

/// <summary>The reader of the ocrText condition: reason codes, raw text and parser (issue #338, FR-007, FR-013).</summary>
public sealed class OcrTextConditionReaderTests {
  private sealed class FakeFrames : ISessionFrameSource {
    private readonly bool _null;
    private readonly bool _throw;
    public FakeFrames(bool returnNull = false, bool @throw = false) { _null = returnNull; _throw = @throw; }
    public Bitmap? Capture(string sessionId) =>
      _throw ? throw new InvalidOperationException("boom") : _null ? null : new Bitmap(540, 960);
  }

  private sealed class FakeOcr : ITextOcr {
    private readonly string _text;
    private readonly bool _throw;
    public FakeOcr(string text, bool @throw = false) { _text = text; _throw = @throw; }
    public OcrResult Recognize(Bitmap image) =>
      _throw ? throw new InvalidOperationException("boom") : new OcrResult(_text, 0.9);
    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
  }

  private static OcrTextStepCondition Condition(int x = 10, int y = 10, int w = 100, int h = 50, string? parser = null) => new() {
    Region = new PixelRegion(x, y, w, h),
    Expected = "x",
    Parser = parser
  };

  private static Task<OcrTextReadResult> ReadAsync(OcrTextStepCondition condition, FakeFrames? frames, ITextOcr? ocr)
    => new OcrTextConditionReader(frames, ocr).ReadAsync(condition, "sess-1", CancellationToken.None);

  [Fact]
  public async Task NoFrameGivesCaptureFailed() {
    (await ReadAsync(Condition(), new FakeFrames(returnNull: true), new FakeOcr("0"))).FailureReason.Should().Be("capture-failed");
    (await ReadAsync(Condition(), new FakeFrames(@throw: true), new FakeOcr("0"))).FailureReason.Should().Be("capture-failed");
    (await ReadAsync(Condition(), null, new FakeOcr("0"))).FailureReason.Should().Be("capture-failed");
  }

  [Fact]
  public async Task NoEngineGivesOcrUnavailable() {
    (await ReadAsync(Condition(), new FakeFrames(), null)).FailureReason.Should().Be("ocr-unavailable");
  }

  [Fact]
  public async Task ARegionOutsideTheFrameGivesRegionOutsideFrame() {
    (await ReadAsync(Condition(x: 500, w: 100), new FakeFrames(), new FakeOcr("0"))).FailureReason.Should().Be("region-outside-frame");
    (await ReadAsync(Condition(x: -1), new FakeFrames(), new FakeOcr("0"))).FailureReason.Should().Be("region-outside-frame");
  }

  [Fact]
  public async Task ARegionWithNoAreaGivesRegionInvalid() {
    (await ReadAsync(Condition(w: 0), new FakeFrames(), new FakeOcr("0"))).FailureReason.Should().Be("region-invalid");
    var noRegion = Condition();
    noRegion.Region = null;
    (await ReadAsync(noRegion, new FakeFrames(), new FakeOcr("0"))).FailureReason.Should().Be("region-invalid");
  }

  [Fact]
  public async Task AnEngineFaultGivesOcrError() {
    (await ReadAsync(Condition(), new FakeFrames(), new FakeOcr("0", @throw: true))).FailureReason.Should().Be("ocr-error");
  }

  [Fact]
  public async Task AnEmptyReadGivesAnEmptyTextAndNoReason() {
    var result = await ReadAsync(Condition(), new FakeFrames(), new FakeOcr(string.Empty));

    result.FailureReason.Should().BeNull();
    result.Text.Should().BeEmpty();
  }

  [Fact]
  public async Task TheRawTextIsGivenUntouched() {
    var result = await ReadAsync(Condition(), new FakeFrames(), new FakeOcr("  Refresh time \n"));

    result.Text.Should().Be("  Refresh time \n");
  }

  [Fact]
  public async Task ParsedIsSetWhenTheTextParses() {
    var result = await ReadAsync(Condition(parser: "HH:MM:SS"), new FakeFrames(), new FakeOcr("2:10:35"));

    result.FailureReason.Should().BeNull();
    result.Parsed.Should().Be("02:10:35");
    result.Text.Should().Be("2:10:35");
  }

  [Fact]
  public async Task AParseFailureGivesParseFailedAndKeepsTheText() {
    var result = await ReadAsync(Condition(parser: "hh:mm:ss"), new FakeFrames(), new FakeOcr("abc"));

    result.FailureReason.Should().Be("parse-failed");
    result.Text.Should().Be("abc");
  }

  [Fact]
  public async Task AnUnknownParserGivesParseFailed() {
    var result = await ReadAsync(Condition(parser: "no-such-parser"), new FakeFrames(), new FakeOcr("2:10:35"));

    result.FailureReason.Should().Be("parse-failed");
  }
}

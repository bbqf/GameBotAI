using System;
using System.Collections.Generic;
using System.Drawing;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Services.Quiz;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1416

namespace GameBot.UnitTests.Quiz;

/// <summary>Feature 142 (R2): the real screen reader takes one frame and reads each region with the shared OCR code.</summary>
public sealed class QuizScreenReaderTests {
  private sealed class QueueOcr : ITextOcr {
    private readonly Queue<string> _texts;
    public int Calls { get; private set; }
    public bool Throw { get; set; }

    public QueueOcr(params string[] texts) { _texts = new Queue<string>(texts); }

    public OcrResult Recognize(Bitmap image) {
      Calls++;
      if (Throw) throw new InvalidOperationException("engine fault");
      return new OcrResult(_texts.Count > 0 ? _texts.Dequeue() : string.Empty, 0.9);
    }

    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
  }

  private sealed class FakeFrames : ISessionFrameSource {
    public int Captures { get; private set; }
    public bool NoFrame { get; set; }
    public bool Throw { get; set; }
    public string? LastSession { get; private set; }

    public Bitmap? Capture(string sessionId) {
      Captures++;
      LastSession = sessionId;
      if (Throw) throw new InvalidOperationException("capture fault");
      return NoFrame ? null : new Bitmap(200, 100);
    }
  }

  private static OcrOffsetRegion[] Regions(int count) {
    var list = new OcrOffsetRegion[count];
    for (var i = 0; i < count; i++) list[i] = new OcrOffsetRegion(0, i * 20, 100, 20);
    return list;
  }

  [Fact]
  public void ReadsEveryRegionFromOneFrame() {
    var frames = new FakeFrames();
    var reader = new QuizScreenReader(frames, new QueueOcr("Question?", "A", "B"));

    var texts = reader.ReadRegions("session-1", Regions(3));

    texts.Should().Equal("Question?", "A", "B");
    frames.Captures.Should().Be(1);
    frames.LastSession.Should().Be("session-1");
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("  ")]
  public void NoSessionGivesNoRead(string? session) {
    var frames = new FakeFrames();

    new QuizScreenReader(frames, new QueueOcr("x")).ReadRegions(session, Regions(1)).Should().BeNull();

    frames.Captures.Should().Be(0);
  }

  [Fact]
  public void NoFrameGivesNoRead() {
    var reader = new QuizScreenReader(new FakeFrames { NoFrame = true }, new QueueOcr("x"));

    reader.ReadRegions("s", Regions(1)).Should().BeNull();
  }

  [Fact]
  public void ACaptureFaultGivesNoRead() {
    var reader = new QuizScreenReader(new FakeFrames { Throw = true }, new QueueOcr("x"));

    reader.ReadRegions("s", Regions(1)).Should().BeNull();
  }

  [Fact]
  public void AnOcrFaultGivesNoRead() {
    var ocr = new QueueOcr("x") { Throw = true };

    new QuizScreenReader(new FakeFrames(), ocr).ReadRegions("s", Regions(2)).Should().BeNull();
    ocr.Calls.Should().Be(1);
  }

  [Fact]
  public void ARegionOutsideTheFrameGivesANullEntry() {
    var reader = new QuizScreenReader(new FakeFrames(), new QueueOcr("inside"));
    var regions = new[] { new OcrOffsetRegion(0, 0, 50, 20), new OcrOffsetRegion(500, 500, 50, 20) };

    var texts = reader.ReadRegions("s", regions);

    texts.Should().NotBeNull();
    texts![0].Should().Be("inside");
    texts[1].Should().BeNull();
  }

  [Fact]
  public void NoRegionsThrowsOnANullList() {
    var reader = new QuizScreenReader(new FakeFrames(), new QueueOcr());

    Action act = () => reader.ReadRegions("s", null!);

    act.Should().Throw<ArgumentNullException>();
  }
}

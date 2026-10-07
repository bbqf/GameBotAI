using System;
using System.Drawing;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Services.Ocr;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1416

namespace GameBot.UnitTests.Ocr;

/// <summary>Feature 128: the shared crop and read code that the ocrOffset step and the OCR read endpoint use.</summary>
public sealed class OcrRegionReaderTests {
  private sealed class RecordingOcr : ITextOcr {
    private readonly string _text;
    public Size? LastSize { get; private set; }
    public RecordingOcr(string text) { _text = text; }
    public OcrResult Recognize(Bitmap image) {
      LastSize = image.Size;
      return new OcrResult(_text, 0.9);
    }
    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
  }

  private sealed class FakeFrameSource : ISessionFrameSource {
    public Bitmap? Capture(string sessionId) => new Bitmap(200, 100);
  }

  [Fact]
  public void ValidRegionIsCroppedAndRead() {
    using var frame = new Bitmap(200, 100);
    var ocr = new RecordingOcr("00:05:42");

    var result = OcrRegionReader.Read(frame, new OcrOffsetRegion(10, 20, 120, 40), ocr);

    result.CropFailed.Should().BeFalse();
    result.Text.Should().Be("00:05:42");
    result.Confidence.Should().Be(0.9);
    ocr.LastSize.Should().Be(new Size(120, 40));
  }

  [Fact]
  public void RegionThatStartsOutsideTheFrameGivesCropFailed() {
    using var frame = new Bitmap(200, 100);
    var ocr = new RecordingOcr("x");

    var result = OcrRegionReader.Read(frame, new OcrOffsetRegion(200, 0, 10, 10), ocr);

    result.CropFailed.Should().BeTrue();
    ocr.LastSize.Should().BeNull();
  }

  [Fact]
  public void RegionWithNoSizeGivesCropFailed() {
    using var frame = new Bitmap(200, 100);

    var result = OcrRegionReader.Read(frame, new OcrOffsetRegion(0, 0, 0, 10), new RecordingOcr("x"));

    result.CropFailed.Should().BeTrue();
  }

  [Fact]
  public void RawTextEqualsTheTextThatTheOcrOffsetStepReads() {
    var spec = new SelfRescheduleOcrOffset {
      Region = new OcrOffsetRegion(0, 0, 120, 40),
      Fallback = TimeSpan.FromMinutes(6),
      Min = TimeSpan.FromSeconds(1),
      Max = TimeSpan.FromHours(24)
    };
    var ocr = new RecordingOcr("00:05:42");
    var step = new OcrOffsetResolver(new FakeFrameSource(), ocr).Resolve("sess", spec);

    using var frame = new Bitmap(200, 100);
    var read = OcrRegionReader.Read(frame, spec.Region, ocr);

    read.Text.Should().Be(step.RecognizedText);
  }
}

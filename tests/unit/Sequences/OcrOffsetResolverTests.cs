using System;
using System.Drawing;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 068: the OCR-offset resolver's decision logic (capture → crop → OCR → parse →
/// bounds-check → source), exercised deterministically with a fake frame source and fake OCR.
/// </summary>
public sealed class OcrOffsetResolverTests {
  private sealed class FakeFrameSource : ISessionFrameSource {
    private readonly int _width;
    private readonly int _height;
    private readonly bool _returnNull;
    public FakeFrameSource(int width = 200, int height = 100, bool returnNull = false) {
      _width = width; _height = height; _returnNull = returnNull;
    }
    public Bitmap? Capture(string sessionId) => _returnNull ? null : new Bitmap(_width, _height);
  }

  private sealed class FakeOcr : ITextOcr {
    private readonly string _text;
    private readonly bool _throw;
    public FakeOcr(string text, bool @throw = false) { _text = text; _throw = @throw; }
    public OcrResult Recognize(Bitmap image) =>
      _throw ? throw new InvalidOperationException("ocr boom") : new OcrResult(_text, 0.99);
    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
  }

  private static SelfRescheduleOcrOffset Spec(
      TimeSpan? min = null, TimeSpan? max = null, TimeSpan? fallback = null,
      int x = 0, int y = 0, int w = 120, int h = 40) =>
    new() {
      Region = new OcrOffsetRegion(x, y, w, h),
      Fallback = fallback ?? TimeSpan.FromMinutes(6),
      Min = min ?? TimeSpan.FromSeconds(1),
      Max = max ?? TimeSpan.FromHours(24)
    };

  private static OcrOffsetResolver Resolver(ISessionFrameSource frames, ITextOcr ocr) =>
    new(frames, ocr);

  [Fact]
  public void GoodTimerReadUsesOcrOffset() {
    var res = Resolver(new FakeFrameSource(), new FakeOcr("00:05:42")).Resolve("sess", Spec());
    res.Source.Should().Be(OcrOffsetSource.Ocr);
    res.EffectiveOffset.Should().Be(new TimeSpan(0, 5, 42));
    res.RecognizedText.Should().Be("00:05:42");
    res.Reason.Should().BeNull();
  }

  [Fact]
  public void EmptyOcrFallsBack() {
    var res = Resolver(new FakeFrameSource(), new FakeOcr("   ")).Resolve("sess", Spec());
    res.Source.Should().Be(OcrOffsetSource.Fallback);
    res.EffectiveOffset.Should().Be(TimeSpan.FromMinutes(6));
    res.Reason.Should().Be("ocr-empty");
  }

  [Fact]
  public void GarbageOcrFallsBack() {
    var res = Resolver(new FakeFrameSource(), new FakeOcr("no timer")).Resolve("sess", Spec());
    res.Source.Should().Be(OcrOffsetSource.Fallback);
    res.Reason.Should().Be("parse-failed");
    res.RecognizedText.Should().Be("no timer");
  }

  [Fact]
  public void ZeroReadIsBelowMinAndFallsBack() {
    var res = Resolver(new FakeFrameSource(), new FakeOcr("00:00:00")).Resolve("sess", Spec());
    res.Source.Should().Be(OcrOffsetSource.Fallback);
    res.Reason.Should().Be("out-of-bounds");
  }

  [Fact]
  public void AboveMaxFallsBack() {
    var res = Resolver(new FakeFrameSource(), new FakeOcr("23:00:00"))
      .Resolve("sess", Spec(max: TimeSpan.FromHours(1)));
    res.Source.Should().Be(OcrOffsetSource.Fallback);
    res.Reason.Should().Be("out-of-bounds");
  }

  [Fact]
  public void NoCaptureFallsBack() {
    var res = Resolver(new FakeFrameSource(returnNull: true), new FakeOcr("00:05:42")).Resolve("sess", Spec());
    res.Source.Should().Be(OcrOffsetSource.Fallback);
    res.Reason.Should().Be("no-capture");
  }

  [Fact]
  public void NoSessionFallsBack() {
    var res = Resolver(new FakeFrameSource(), new FakeOcr("00:05:42")).Resolve(null, Spec());
    res.Source.Should().Be(OcrOffsetSource.Fallback);
    res.Reason.Should().Be("no-session");
  }

  [Fact]
  public void OcrErrorFallsBackWithoutThrowing() {
    var res = Resolver(new FakeFrameSource(), new FakeOcr("x", @throw: true)).Resolve("sess", Spec());
    res.Source.Should().Be(OcrOffsetSource.Fallback);
    res.Reason.Should().Be("ocr-error");
  }

  [Fact]
  public void RegionEntirelyOffFrameFallsBack() {
    // Frame is 200x100; region starts at x=500 which is off-frame.
    var res = Resolver(new FakeFrameSource(200, 100), new FakeOcr("00:05:42"))
      .Resolve("sess", Spec(x: 500, y: 10, w: 50, h: 20));
    res.Source.Should().Be(OcrOffsetSource.Fallback);
    res.Reason.Should().Be("region-invalid");
  }

  [Fact]
  public void MmSsReadUsesOcrOffset() {
    var res = Resolver(new FakeFrameSource(), new FakeOcr("01:20")).Resolve("sess", Spec());
    res.Source.Should().Be(OcrOffsetSource.Ocr);
    res.EffectiveOffset.Should().Be(new TimeSpan(0, 1, 20));
  }
}

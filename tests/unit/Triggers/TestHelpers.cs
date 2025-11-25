using System;
using System.Drawing;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Tests.Unit.Triggers {
  // Stub for ITextOcr
  internal sealed class StubTextOcr : ITextOcr {
    private readonly Func<Bitmap, OcrResult> _recognize;
    public StubTextOcr(Func<Bitmap, OcrResult>? recognize = null) {
      _recognize = recognize ?? (_ => new OcrResult("", 0.0));
    }
    public OcrResult Recognize(Bitmap image) => _recognize(image);
    public OcrResult Recognize(Bitmap image, string? language) => _recognize(image);
  }

  // Stub for IScreenSource
  internal sealed class StubScreenSource : IScreenSource {
    private readonly Bitmap _bmp;
    public StubScreenSource(Bitmap bmp) { _bmp = bmp; }
    public Bitmap GetLatestScreenshot() => (Bitmap)_bmp.Clone();
  }

  // Stub for IReferenceImageStore
  internal sealed class StubReferenceImageStore : IReferenceImageStore {
    private readonly Bitmap _bmp;
    public StubReferenceImageStore(Bitmap bmp) { _bmp = bmp; }
    public bool TryGet(string id, out Bitmap bmp) {
      bmp = (Bitmap)_bmp.Clone();
      return true;
    }
    public void AddOrUpdate(string id, Bitmap bmp) {
      // No-op for stub
    }
  }

  // Optional: Test clock abstraction
  internal interface ITestClock {
    DateTimeOffset UtcNow { get; }
  }
  internal sealed class FixedTestClock : ITestClock {
    public DateTimeOffset UtcNow { get; }
    public FixedTestClock(DateTimeOffset now) { UtcNow = now; }
  }
}

using System.Drawing;
using System.Runtime.Versioning;
using FluentAssertions;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Domain.Vision;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Vision;

/// <summary>
/// Feature 166 (FR-012): the ambient scope that keeps the last detection pass of a <c>WaitForImage</c>
/// wait, and the write of <see cref="ImageMatchEvaluator"/> into it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ImageDetectionPassScopeTests {
  [Fact]
  public void NoScopeGivesNullCurrent() {
    ImageDetectionPassScope.Current.Should().BeNull();
  }

  [Fact]
  public void PushMakesAnEmptyScopeCurrent() {
    using (ImageDetectionPassScope.Push()) {
      ImageDetectionPassScope.Current.Should().NotBeNull();
      ImageDetectionPassScope.Current!.LastPass.Should().BeNull();
    }
  }

  [Fact]
  public async Task AWrittenPassIsVisibleAfterAnAwait() {
    using (ImageDetectionPassScope.Push()) {
      await WritePassAsync();
      ImageDetectionPassScope.Current!.LastPass.Should().Be(new ImageDetectionPass(true, 0.5));
    }
  }

  [Fact]
  public void DisposeRestoresTheEarlierScope() {
    using (ImageDetectionPassScope.Push()) {
      var outer = ImageDetectionPassScope.Current;
      using (ImageDetectionPassScope.Push()) {
        ImageDetectionPassScope.Current.Should().NotBeSameAs(outer);
      }
      ImageDetectionPassScope.Current.Should().BeSameAs(outer);
    }
    ImageDetectionPassScope.Current.Should().BeNull();
  }

  [Fact]
  public void EvaluatorWritesNoFrameWhenTheScreenSourceGivesNull() {
    using var reference = CreateBitmap(4, 4);
    var evaluator = new ImageMatchEvaluator(new StoreStub(reference), new ScreenStub(null));

    using (ImageDetectionPassScope.Push()) {
      evaluator.Evaluate(CreateTrigger(), DateTimeOffset.UtcNow);

      var pass = ImageDetectionPassScope.Current!.LastPass;
      pass.Should().NotBeNull();
      pass!.FrameAvailable.Should().BeFalse();
      pass.BestScore.Should().BeNull();
    }
  }

  [Fact]
  public void EvaluatorWritesFrameAndBestScoreWhenTheScreenSourceGivesAFrame() {
    using var reference = CreateBitmap(4, 4);
    var evaluator = new ImageMatchEvaluator(new StoreStub(reference), new ScreenStub(() => CreateBitmap(16, 16)));

    using (ImageDetectionPassScope.Push()) {
      var result = evaluator.Evaluate(CreateTrigger(), DateTimeOffset.UtcNow);

      var pass = ImageDetectionPassScope.Current!.LastPass;
      pass.Should().NotBeNull();
      pass!.FrameAvailable.Should().BeTrue();
      pass.BestScore.Should().Be(result.Similarity);
    }
  }

  [Fact]
  public void EvaluatorWithNoScopeWritesNothingAndKeepsItsResult() {
    using var reference = CreateBitmap(4, 4);
    var evaluator = new ImageMatchEvaluator(new StoreStub(reference), new ScreenStub(() => CreateBitmap(16, 16)));

    var withoutScope = evaluator.Evaluate(CreateTrigger(), DateTimeOffset.UtcNow);
    ImageDetectionPassScope.Current.Should().BeNull();

    TriggerEvaluationResult withScope;
    using (ImageDetectionPassScope.Push()) {
      withScope = evaluator.Evaluate(CreateTrigger(), DateTimeOffset.UtcNow);
    }
    withScope.Similarity.Should().Be(withoutScope.Similarity);
    withScope.Status.Should().Be(withoutScope.Status);
  }

  private static async Task WritePassAsync() {
    await Task.Yield();
    ImageDetectionPassScope.Current!.LastPass = new ImageDetectionPass(true, 0.5);
  }

  private static Trigger CreateTrigger() => new() {
    Id = "t-166",
    Type = TriggerType.ImageMatch,
    Enabled = true,
    Params = new ImageMatchParams {
      ReferenceImageId = "ref",
      Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 },
      SimilarityThreshold = 0.85
    }
  };

  private static Bitmap CreateBitmap(int width, int height) {
    var bmp = new Bitmap(width, height);
    for (var x = 0; x < width; x++) {
      for (var y = 0; y < height; y++) {
        bmp.SetPixel(x, y, Color.FromArgb(255, (x * 37) % 256, (y * 53) % 256, ((x + y) * 19) % 256));
      }
    }
    return bmp;
  }

  private sealed class StoreStub : IReferenceImageStore {
    private readonly Bitmap _bitmap;
    public StoreStub(Bitmap bitmap) => _bitmap = bitmap;
    public bool TryGet(string id, out Bitmap bitmap) { bitmap = _bitmap; return id == "ref"; }
    public void AddOrUpdate(string id, Bitmap bitmap) { }
    public bool Exists(string id) => id == "ref";
    public bool Delete(string id) => false;
  }

  private sealed class ScreenStub : IScreenSource {
    private readonly Func<Bitmap>? _factory;
    public ScreenStub(Func<Bitmap>? factory) => _factory = factory;
    public Bitmap? GetLatestScreenshot() => _factory?.Invoke();
  }
}

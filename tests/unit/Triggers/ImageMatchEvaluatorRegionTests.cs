using System.Drawing;
using GameBot.Domain.Commands;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;
using DomainRegion = GameBot.Domain.Triggers.Region;

// The seeded Random makes the same test picture on each run. It is not used for security.
#pragma warning disable CA5394

namespace GameBot.UnitTests.Triggers;

/// <summary>
/// Feature 130: an image condition with a pixel region matches only inside the region.
/// The screen is 200 x 100 noise. The image is 20 x 20 noise, so a match is clear and a miss is low.
/// </summary>
public sealed class ImageMatchEvaluatorRegionTests {
  private const int ScreenWidth = 200;
  private const int ScreenHeight = 100;
  private const int ImageSize = 20;

  private static Bitmap Noise(int width, int height, int seed) {
    var random = new Random(seed);
    var bmp = new Bitmap(width, height);
    for (var y = 0; y < height; y++) {
      for (var x = 0; x < width; x++) {
        bmp.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
      }
    }
    return bmp;
  }

  private static Bitmap ScreenWithImageAt(Bitmap image, params (int X, int Y)[] places) {
    var screen = Noise(ScreenWidth, ScreenHeight, seed: 1);
    using var g = Graphics.FromImage(screen);
    foreach (var (x, y) in places) {
      g.DrawImageUnscaled(image, x, y);
    }
    return screen;
  }

  private static TriggerEvaluationResult Evaluate(Bitmap image, Bitmap screen, PixelRegion? pixelRegion, DomainRegion? fraction = null) {
    var store = new MemoryReferenceImageStore();
    store.AddOrUpdate("tpl", (Bitmap)image.Clone());
    var source = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
    var evaluator = new ImageMatchEvaluator(store, source);
    var trigger = new Trigger {
      Id = "region-test",
      Type = TriggerType.ImageMatch,
      Enabled = true,
      Params = new ImageMatchParams {
        ReferenceImageId = "tpl",
        Region = fraction ?? new DomainRegion { X = 0, Y = 0, Width = 1, Height = 1 },
        PixelRegion = pixelRegion,
        SimilarityThreshold = 0.95
      }
    };
    return evaluator.Evaluate(trigger, DateTimeOffset.UtcNow);
  }

  [Fact]
  public void MatchInsideRegionIsSatisfied() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (10, 10));

    var result = Evaluate(image, screen, new PixelRegion(0, 0, 100, 100));

    Assert.Equal(TriggerStatus.Satisfied, result.Status);
  }

  [Fact]
  public void MatchOnlyOutsideRegionIsNotSatisfied() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (150, 10));

    Assert.Equal(TriggerStatus.Satisfied, Evaluate(image, screen, null).Status);

    var result = Evaluate(image, screen, new PixelRegion(0, 0, 100, 100));

    Assert.Equal(TriggerStatus.Pending, result.Status);
    Assert.True(result.Similarity!.Value < 0.95);
  }

  [Fact]
  public void TwoMatchesOnlyTheOneInsideTheRegionCounts() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (10, 10), (150, 10));

    Assert.Equal(TriggerStatus.Satisfied, Evaluate(image, screen, new PixelRegion(120, 0, 80, 100)).Status);
    Assert.Equal(TriggerStatus.Satisfied, Evaluate(image, screen, new PixelRegion(0, 0, 100, 100)).Status);
    // A region between the two copies holds neither.
    Assert.Equal(TriggerStatus.Pending, Evaluate(image, screen, new PixelRegion(40, 0, 100, 100)).Status);
  }

  [Fact]
  public void RegionSmallerThanImageIsNotSatisfied() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (10, 10));

    var result = Evaluate(image, screen, new PixelRegion(10, 10, 10, 10));

    Assert.Equal(TriggerStatus.Pending, result.Status);
    Assert.Equal(0d, result.Similarity!.Value);
  }

  [Fact]
  public void ImagePartlyInsideRegionIsNotSatisfied() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (90, 10));

    // The region ends at x = 99, so the image (x 90 to 109) is not fully inside.
    var result = Evaluate(image, screen, new PixelRegion(0, 0, 100, 100));

    Assert.Equal(TriggerStatus.Pending, result.Status);
  }

  [Fact]
  public void RegionPastTheCaptureIsClipped() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (150, 10));

    var result = Evaluate(image, screen, new PixelRegion(120, 0, 500, 500));

    Assert.Equal(TriggerStatus.Satisfied, result.Status);
  }

  [Fact]
  public void RegionFullyOutsideTheCaptureIsNotSatisfiedAndGivesNoError() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (150, 10));

    var result = Evaluate(image, screen, new PixelRegion(500, 500, 50, 50));

    Assert.Equal(TriggerStatus.Pending, result.Status);
    Assert.Equal(0d, result.Similarity!.Value);
  }

  [Fact]
  public void PixelRegionWinsOverTheFractionRegion() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (150, 10));
    // The fraction region covers the left strip only, where the image is not.
    var fraction = new DomainRegion { X = 0, Y = 0, Width = 0.25, Height = 1 };

    Assert.Equal(TriggerStatus.Pending, Evaluate(image, screen, null, fraction).Status);
    Assert.Equal(TriggerStatus.Satisfied, Evaluate(image, screen, new PixelRegion(120, 0, 80, 100), fraction).Status);
  }

  [Fact]
  public void NoRegionGivesTheOldResult() {
    using var image = Noise(ImageSize, ImageSize, seed: 2);
    using var screen = ScreenWithImageAt(image, (150, 10));

    var result = Evaluate(image, screen, null);

    Assert.Equal(TriggerStatus.Satisfied, result.Status);
    Assert.True(result.Similarity!.Value >= 0.95);
  }
}

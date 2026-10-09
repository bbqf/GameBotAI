using System.Runtime.InteropServices;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Execution;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;

// The seeded Random makes the same test picture on each run. It is not used for security.
#pragma warning disable CA5394

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 130: a detection target with a pixel region taps the match inside the region, in
/// full-capture pixels. The screen is 100 rows by 200 columns of noise with two copies of a 20 x 20 image:
/// an exact copy at (150, 10) and a weaker copy (a changed corner) at (20, 30).
/// </summary>
public sealed class DetectionCoordinateResolverRegionTests {
  private static readonly TemplateMatcherConfig Config = new(Threshold: 0.8, MaxResults: 10, Overlap: 0.3);

  private static Mat Noise(int rows, int cols, int seed) {
    var bytes = new byte[rows * cols * 3];
    new Random(seed).NextBytes(bytes);
    var mat = new Mat(rows, cols, MatType.CV_8UC3);
    Marshal.Copy(bytes, 0, mat.Data, bytes.Length);
    return mat;
  }

  private static void Paste(Mat screen, Mat image, int x, int y, bool weaken) {
    using var roi = new Mat(screen, new Rect(x, y, image.Cols, image.Rows));
    image.CopyTo(roi);
    if (weaken) {
      using var corner = new Mat(roi, new Rect(0, 0, 4, 4));
      corner.SetTo(new Scalar(0, 0, 0));
    }
  }

  private static (Mat Screen, Mat Image) Scene() {
    var screen = Noise(100, 200, seed: 1);
    var image = Noise(20, 20, seed: 2);
    Paste(screen, image, 150, 10, weaken: false);
    Paste(screen, image, 20, 30, weaken: true);
    return (screen, image);
  }

  private static DetectionCoordinateResolver Resolver() => new(new TemplateMatcher());

  [Fact]
  public void WithoutRegionTheBetterMatchOutsideWins() {
    var (screen, image) = Scene();
    using var _s = screen;
    using var _i = image;

    var result = Resolver().ResolveCenter(new DetectionTarget("tpl", 0.8), screen, image, Config, out var error, DetectionSelectionStrategy.HighestConfidence);

    error.Should().BeNull();
    result.Should().NotBeNull();
    (result!.X, result.Y).Should().Be((160, 20));
  }

  [Fact]
  public void RegionAroundTheWeakerMatchGivesItsPointInFullCapturePixels() {
    var (screen, image) = Scene();
    using var _s = screen;
    using var _i = image;
    var target = new DetectionTarget("tpl", 0.8, region: new PixelRegion(0, 0, 100, 100));

    var result = Resolver().ResolveCenter(target, screen, image, Config, out var error, DetectionSelectionStrategy.HighestConfidence);

    error.Should().BeNull();
    result.Should().NotBeNull();
    (result!.X, result.Y).Should().Be((30, 40));
    result.BBox.X.Should().Be(20);
    result.BBox.Y.Should().Be(30);
  }

  [Fact]
  public void RegionWithAnOffsetOriginGivesFullCapturePixels() {
    var (screen, image) = Scene();
    using var _s = screen;
    using var _i = image;
    var target = new DetectionTarget("tpl", 0.8, offsetX: 2, offsetY: 3, region: new PixelRegion(120, 0, 80, 100));

    var result = Resolver().ResolveCenter(target, screen, image, Config, out var error, DetectionSelectionStrategy.HighestConfidence);

    error.Should().BeNull();
    result.Should().NotBeNull();
    (result!.X, result.Y).Should().Be((162, 23));
  }

  [Fact]
  public void SingleResultOverloadUsesTheRegionToo() {
    var (screen, image) = Scene();
    using var _s = screen;
    using var _i = image;
    var target = new DetectionTarget("tpl", 0.8, region: new PixelRegion(0, 0, 100, 100));

    // Without a region this overload reports two matches. With the region only one is left.
    Resolver().ResolveCenter(new DetectionTarget("tpl", 0.8), screen, image, Config, out var multiError).Should().BeNull();
    multiError.Should().Contain("multiple detections");

    var result = Resolver().ResolveCenter(target, screen, image, Config, out var error);

    error.Should().BeNull();
    result.Should().NotBeNull();
    (result!.X, result.Y).Should().Be((30, 40));
  }

  [Theory]
  [InlineData(DetectionSelectionStrategy.HighestConfidence)]
  [InlineData(DetectionSelectionStrategy.FirstMatch)]
  public void ImageNotInsideTheRegionGivesNotFoundAndNoTap(DetectionSelectionStrategy strategy) {
    var (screen, image) = Scene();
    using var _s = screen;
    using var _i = image;
    var target = new DetectionTarget("tpl", 0.8, region: new PixelRegion(0, 60, 100, 40));

    var result = Resolver().ResolveCenter(target, screen, image, Config, out var error, strategy);

    result.Should().BeNull();
    error.Should().Be("no detection above threshold");
  }

  [Theory]
  [InlineData(DetectionSelectionStrategy.HighestConfidence)]
  [InlineData(DetectionSelectionStrategy.FirstMatch)]
  public void EmptyClipGivesNotFoundWithoutAnException(DetectionSelectionStrategy strategy) {
    var (screen, image) = Scene();
    using var _s = screen;
    using var _i = image;
    var target = new DetectionTarget("tpl", 0.8, region: new PixelRegion(500, 500, 40, 40));

    var result = Resolver().ResolveCenter(target, screen, image, Config, out var error, strategy);

    result.Should().BeNull();
    error.Should().Be("no detection above threshold");
  }

  [Fact]
  public void ClipSmallerThanTheImageGivesNotFound() {
    var (screen, image) = Scene();
    using var _s = screen;
    using var _i = image;
    var target = new DetectionTarget("tpl", 0.8, region: new PixelRegion(15, 25, 10, 10));

    var result = Resolver().ResolveCenter(target, screen, image, Config, out var error, DetectionSelectionStrategy.HighestConfidence);

    result.Should().BeNull();
    error.Should().Be("no detection above threshold");
  }

  [Fact]
  public void FirstMatchStrategyWithRegionPicksInsideMatch() {
    var (screen, image) = Scene();
    using var _s = screen;
    using var _i = image;
    var target = new DetectionTarget("tpl", 0.8, region: new PixelRegion(120, 0, 80, 100));

    var result = Resolver().ResolveCenter(target, screen, image, Config, out var error, DetectionSelectionStrategy.FirstMatch);

    error.Should().BeNull();
    result.Should().NotBeNull();
    (result!.X, result.Y).Should().Be((160, 20));
  }
}

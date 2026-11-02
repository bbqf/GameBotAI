using System.Drawing;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Vision;
using Xunit;

#pragma warning disable CA1416, CA2007

namespace GameBot.UnitTests.WaitForStill;

/// <summary>Feature 174: the frame difference (mean absolute R, G, B difference, 0 to 1).</summary>
public sealed class FrameDifferenceTests {
  private static Bitmap Solid(int width, int height, Color color) {
    var bitmap = new Bitmap(width, height);
    using var g = Graphics.FromImage(bitmap);
    g.Clear(color);
    return bitmap;
  }

  [Fact]
  public void IdenticalFramesGiveZero() {
    using var a = Solid(40, 30, Color.FromArgb(10, 20, 30));
    using var b = Solid(40, 30, Color.FromArgb(10, 20, 30));

    FrameDifference.Compute(a, b, null).Should().Be(0);
  }

  [Fact]
  public void BlackAgainstWhiteGivesOne() {
    using var a = Solid(40, 30, Color.Black);
    using var b = Solid(40, 30, Color.White);

    FrameDifference.Compute(a, b, null).Should().BeApproximately(1d, 1e-9);
  }

  [Fact]
  public void OnlyPixelsInsideTheRegionCount() {
    using var a = Solid(40, 30, Color.Black);
    using var b = Solid(40, 30, Color.Black);
    b.SetPixel(35, 25, Color.White);

    FrameDifference.Compute(a, b, new PixelRegion(0, 0, 20, 20)).Should().Be(0);
    FrameDifference.Compute(a, b, new PixelRegion(30, 20, 10, 10)).Should().BeApproximately(1d / 100d, 1e-9);
    FrameDifference.Compute(a, b, null).Should().BeApproximately(1d / 1200d, 1e-9);
  }

  [Fact]
  public void FramesOfDifferentSizesGiveOne() {
    using var a = Solid(40, 30, Color.Black);
    using var b = Solid(41, 30, Color.Black);

    FrameDifference.Compute(a, b, null).Should().Be(1);
  }

  [Fact]
  public void ARegionOutsideTheFrameGivesOneAndAPartlyOutsideRegionIsCut() {
    using var a = Solid(40, 30, Color.Black);
    using var b = Solid(40, 30, Color.Black);
    b.SetPixel(39, 29, Color.White);

    FrameDifference.Compute(a, b, new PixelRegion(100, 100, 5, 5)).Should().Be(1);
    FrameDifference.Compute(a, b, new PixelRegion(38, 28, 50, 50)).Should().BeApproximately(1d / 4d, 1e-9);
  }
}

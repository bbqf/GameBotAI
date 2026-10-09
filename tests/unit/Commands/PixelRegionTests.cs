using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Commands;

public class PixelRegionTests {
  [Fact]
  public void Validate_ValidRegion_ReturnsNoErrors() {
    PixelRegion.Validate(0, 0, 1, 1).Should().BeEmpty();
    new PixelRegion(120, 640, 300, 90).Validate().Should().BeEmpty();
  }

  [Fact]
  public void Validate_ZeroWidth_NamesWidth() {
    PixelRegion.Validate(0, 0, 0, 10).Should().ContainSingle().Which.Should().Be("region.width must be greater than 0");
  }

  [Fact]
  public void Validate_NegativeOrigin_NamesField() {
    PixelRegion.Validate(-1, 0, 10, 10).Should().ContainSingle().Which.Should().Be("region.x must be 0 or more");
    PixelRegion.Validate(0, -5, 10, 10).Should().ContainSingle().Which.Should().Be("region.y must be 0 or more");
  }

  [Fact]
  public void Validate_MissingField_NamesField() {
    PixelRegion.Validate(0, 0, 10, null).Should().ContainSingle().Which.Should().Be("region.height is required");
  }

  [Fact]
  public void Validate_EveryInvalidField_IsNamedAtOnce() {
    var errors = PixelRegion.Validate(-1, null, 0, -3, "x.region");
    errors.Should().HaveCount(4);
    errors.Should().Contain(e => e.StartsWith("x.region.x "));
    errors.Should().Contain(e => e.StartsWith("x.region.y "));
    errors.Should().Contain(e => e.StartsWith("x.region.width "));
    errors.Should().Contain(e => e.StartsWith("x.region.height "));
  }

  [Fact]
  public void ClipTo_Inside_ReturnsSame() {
    var clipped = new PixelRegion(10, 20, 30, 40).ClipTo(100, 100);
    clipped.Should().NotBeNull();
    (clipped!.X, clipped.Y, clipped.Width, clipped.Height).Should().Be((10, 20, 30, 40));
  }

  [Fact]
  public void ClipTo_PastEdge_ReturnsInsidePart() {
    var clipped = new PixelRegion(90, 95, 30, 40).ClipTo(100, 100);
    clipped.Should().NotBeNull();
    (clipped!.X, clipped.Y, clipped.Width, clipped.Height).Should().Be((90, 95, 10, 5));
  }

  [Fact]
  public void ClipTo_FullyOutside_ReturnsNull() {
    new PixelRegion(100, 0, 10, 10).ClipTo(100, 100).Should().BeNull();
    new PixelRegion(0, 100, 10, 10).ClipTo(100, 100).Should().BeNull();
    new PixelRegion(500, 500, 10, 10).ClipTo(100, 100).Should().BeNull();
  }

  [Fact]
  public void ClipTo_EdgePixels_AreKept() {
    // X + Width - 1 is the last pixel of the region: x = 99 is the last pixel of a 100 pixel capture.
    var clipped = new PixelRegion(99, 99, 5, 5).ClipTo(100, 100);
    clipped.Should().NotBeNull();
    (clipped!.X, clipped.Y, clipped.Width, clipped.Height).Should().Be((99, 99, 1, 1));
  }
}

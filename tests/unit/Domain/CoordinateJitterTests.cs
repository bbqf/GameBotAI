using FluentAssertions;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Domain;

public sealed class CoordinateJitterTests {
  [Fact]
  public void RadiusZeroReturnsInputUnchanged() {
    var (x, y) = CoordinateJitter.Apply(100, 200, 0);
    x.Should().Be(100);
    y.Should().Be(200);
  }

  [Fact]
  public void NegativeRadiusReturnsInputUnchanged() {
    var (x, y) = CoordinateJitter.Apply(100, 200, -3);
    x.Should().Be(100);
    y.Should().Be(200);
  }

  [Fact]
  public void PositiveRadiusStaysWithinBoundsOnEachAxis() {
    const int radius = 5;
    for (var i = 0; i < 500; i++) {
      var (x, y) = CoordinateJitter.Apply(100, 200, radius);
      x.Should().BeInRange(100 - radius, 100 + radius);
      y.Should().BeInRange(200 - radius, 200 + radius);
    }
  }

  [Fact]
  public void NearZeroTargetNeverProducesNegativeCoordinates() {
    for (var i = 0; i < 500; i++) {
      var (x, y) = CoordinateJitter.Apply(2, 0, 5);
      x.Should().BeGreaterThanOrEqualTo(0);
      y.Should().BeGreaterThanOrEqualTo(0);
    }
  }

  [Fact]
  public void ConsecutiveCallsProduceVaryingOffsets() {
    var distinct = new HashSet<(int X, int Y)>();
    for (var i = 0; i < 50; i++) {
      distinct.Add(CoordinateJitter.Apply(100, 200, 5));
    }
    distinct.Count.Should().BeGreaterThan(1, "back-to-back calls must not all reuse the same clock-tick seed");
  }

  [Fact]
  public void LargerRadiusProducesOffsetsBeyondDefaultRange() {
    const int radius = 20;
    var sawBeyondDefault = false;
    for (var i = 0; i < 2000 && !sawBeyondDefault; i++) {
      var (x, y) = CoordinateJitter.Apply(100, 200, radius);
      x.Should().BeInRange(100 - radius, 100 + radius);
      y.Should().BeInRange(200 - radius, 200 + radius);
      if (Math.Abs(x - 100) > 5 || Math.Abs(y - 200) > 5) sawBeyondDefault = true;
    }
    sawBeyondDefault.Should().BeTrue("with radius 20, offsets larger than the default ±5 range must occur");
  }
}

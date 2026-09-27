using System;
using FluentAssertions;
using GameBot.Service.Endpoints;
using Xunit;

namespace GameBot.UnitTests.Images {
  /// <summary>
  /// Issue #223: each reference of a detect call gets the full configured time limit, so an image
  /// with alternates does not go past the limit in some calls only.
  /// </summary>
  public sealed class DetectionTimeLimitTests {
    [Theory]
    [InlineData(500, 1, 500)]
    [InlineData(500, 3, 1500)]
    [InlineData(0, 3, 3)]
    [InlineData(-5, 2, 2)]
    [InlineData(500, 0, 500)]
    public void LimitIsTimeoutForEachReference(int timeoutMs, int referenceCount, int expectedMs) {
      ImageDetectionsValidation.DetectionTimeLimit(timeoutMs, referenceCount)
        .Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void LimitDoesNotGoAboveIntMaxValue() {
      ImageDetectionsValidation.DetectionTimeLimit(int.MaxValue, 8)
        .Should().Be(TimeSpan.FromMilliseconds(int.MaxValue));
    }
  }
}

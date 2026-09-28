using FluentAssertions;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 111 (issue #235): the input of a PrimitiveTap step. A single tap is a swipe to the same point
/// for 200 ms. A press and hold is a swipe to the same point for the hold duration.
/// </summary>
public sealed class PrimitiveTapInputTests {
  [Theory]
  [InlineData(null)]
  [InlineData(0)]
  public void CreateGivesTheTapOfTodayWhenHoldIsAbsentOrZero(int? holdMs) {
    var input = PrimitiveTapInput.Create(540, 1210, holdMs);

    input.Type.Should().Be("swipe");
    input.DurationMs.Should().Be(200);
    input.DelayMs.Should().BeNull();
    input.Args["x1"].Should().Be(540);
    input.Args["y1"].Should().Be(1210);
    input.Args["x2"].Should().Be(540);
    input.Args["y2"].Should().Be(1210);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(700)]
  [InlineData(5000)]
  public void CreateGivesAPressAndHoldAtTheSamePointForTheHoldDuration(int holdMs) {
    var input = PrimitiveTapInput.Create(12, 34, holdMs);

    input.Type.Should().Be("swipe");
    input.DurationMs.Should().Be(holdMs);
    input.Args["x1"].Should().Be(12);
    input.Args["y1"].Should().Be(34);
    input.Args["x2"].Should().Be(12);
    input.Args["y2"].Should().Be(34);
  }

  [Theory]
  [InlineData(null, null)]
  [InlineData(0, null)]
  [InlineData(700, 700)]
  [InlineData(5000, 5000)]
  public void EffectiveHoldMsIsSetOnlyForAPressAndHold(int? holdMs, int? expected) {
    PrimitiveTapInput.EffectiveHoldMs(holdMs).Should().Be(expected);
  }
}

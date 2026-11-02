using System;
using FluentAssertions;
using GameBot.Domain.WaitForStill;
using Xunit;

namespace GameBot.UnitTests.WaitForStill;

/// <summary>Feature 174: the stable-time rule of the tracker.</summary>
public sealed class WaitForStillTrackerTests {
  private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

  private static DateTimeOffset At(int ms) => T0.AddMilliseconds(ms);

  [Fact]
  public void TheFirstFrameIsOnlyABaseline() {
    var tracker = new WaitForStillTracker(300, 0.01);

    tracker.HasBaseline.Should().BeFalse();
    tracker.Baseline(At(0));
    tracker.HasBaseline.Should().BeTrue();
  }

  [Fact]
  public void TwoSameFramesOneIntervalApartAreStillWhenTheIntervalIsAtLeastTheStableTime() {
    var tracker = new WaitForStillTracker(300, 0.01);
    tracker.Baseline(At(0));

    tracker.Observe(0, At(500)).Should().BeTrue();
  }

  [Fact]
  public void StableTimeIsCountedFromTheFrameBeforeTheFirstSameFrame() {
    var tracker = new WaitForStillTracker(600, 0.01);
    tracker.Baseline(At(0));

    tracker.Observe(0, At(500)).Should().BeFalse();
    tracker.Observe(0, At(1000)).Should().BeTrue();
  }

  [Fact]
  public void ALargeDifferenceStartsTheStableTimeAgain() {
    var tracker = new WaitForStillTracker(600, 0.01);
    tracker.Baseline(At(0));
    tracker.Observe(0, At(500)).Should().BeFalse();

    tracker.Observe(0.5, At(1000)).Should().BeFalse();
    tracker.Observe(0, At(1500)).Should().BeFalse();
    tracker.Observe(0, At(2000)).Should().BeTrue();
  }

  [Fact]
  public void ADifferenceEqualToMaxDiffCountsAsTheSame() {
    var tracker = new WaitForStillTracker(100, 0.05);
    tracker.Baseline(At(0));

    tracker.Observe(0.05, At(200)).Should().BeTrue();
  }

  [Fact]
  public void ResetClearsTheBaselineAndTheStableTime() {
    var tracker = new WaitForStillTracker(600, 0.01);
    tracker.Baseline(At(0));
    tracker.Observe(0, At(500)).Should().BeFalse();

    tracker.Reset();

    tracker.HasBaseline.Should().BeFalse();
    tracker.Observe(0, At(1000)).Should().BeFalse("the first frame after a reset is a new baseline");
    tracker.Observe(0, At(1500)).Should().BeFalse();
    tracker.Observe(0, At(2000)).Should().BeTrue();
  }

  [Fact]
  public void MaxDiffZeroNeedsExactlyEqualFrames() {
    var tracker = new WaitForStillTracker(100, 0);
    tracker.Baseline(At(0));

    tracker.Observe(0.0001, At(200)).Should().BeFalse();
    tracker.Observe(0, At(400)).Should().BeTrue();
  }
}

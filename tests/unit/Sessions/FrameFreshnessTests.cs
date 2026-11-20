using System;
using System.Drawing;
using FluentAssertions;
using GameBot.Domain.Sessions;
using Xunit;

namespace GameBot.UnitTests.Sessions;

/// <summary>Feature 197 (FR-003): the freshness rule of a frame in memory.</summary>
public sealed class FrameFreshnessTests {
  private static readonly DateTimeOffset T0 = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void AgeIsZeroAtTheCaptureTime() {
    using var bmp = new Bitmap(1, 1);
    var frame = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1);

    FrameFreshness.AgeMs(frame, T0).Should().Be(0);
    FrameFreshness.IsStale(frame, T0, 5000).Should().BeFalse();
  }

  [Theory]
  [InlineData(4999, false)]
  [InlineData(5000, false)]
  [InlineData(5001, true)]
  public void FrameIsStaleOnlyAboveTheLimit(int ageMs, bool stale) {
    using var bmp = new Bitmap(1, 1);
    var frame = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1);
    var now = T0.AddMilliseconds(ageMs);

    FrameFreshness.AgeMs(frame, now).Should().Be(ageMs);
    FrameFreshness.IsStale(frame, now, 5000).Should().Be(stale);
  }

  [Fact]
  public void StaleRepeatIsStaleAtAgeZero() {
    using var bmp = new Bitmap(1, 1);
    var frame = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1) { StaleRepeat = true, StaleRepeatSince = T0 };

    FrameFreshness.IsStale(frame, T0, 5000).Should().BeTrue();
  }

  [Fact]
  public void NegativeTimeGivesAgeZero() {
    using var bmp = new Bitmap(1, 1);
    var frame = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1);

    FrameFreshness.AgeMs(frame, T0.AddSeconds(-3)).Should().Be(0);
  }

  [Theory]
  [InlineData(5000, 500, 5000)]
  [InlineData(1000, 2000, 4000)]
  public void EffectiveLimitIsTheMaximumOfTheLimitAndTwoIntervals(int frameMaxAgeMs, int intervalMs, int expected) {
    FrameFreshness.EffectiveLimitMs(frameMaxAgeMs, intervalMs).Should().Be(expected);
  }

  [Fact]
  public void DescribeGivesTheIdTheAgeAndTheFlag() {
    using var bmp = new Bitmap(1, 1);
    var frame = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1) { FrameId = "f1" };

    FrameFreshness.Describe(frame, T0.AddMilliseconds(6000), 5000).Should().Be(new FrameReadInfo("f1", 6000, true));
  }

  [Fact]
  public void EachNewFrameHasItsOwnId() {
    using var bmp = new Bitmap(1, 1);
    var a = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1);
    var b = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1);

    a.FrameId.Should().NotBeNullOrWhiteSpace();
    a.FrameId.Should().NotBe(b.FrameId);
    (a with { StaleRepeat = true }).FrameId.Should().Be(a.FrameId);
  }
}

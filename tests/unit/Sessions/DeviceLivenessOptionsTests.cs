#pragma warning disable CA2007 // test code: no ConfigureAwait
using FluentAssertions;
using GameBot.Domain.Sessions;
using Xunit;

namespace GameBot.UnitTests.Sessions;

/// <summary>Feature 106 (FR-019): the defaults and the minimums of the liveness options.</summary>
public sealed class DeviceLivenessOptionsTests {
  [Fact]
  public void DefaultsEqualTheSpecAssumptions() {
    var o = new DeviceLivenessOptions();

    DeviceLivenessOptions.SectionName.Should().Be("Service:DeviceLiveness");
    o.StaleLimitMs.Should().Be(300000);
    o.CaptureStallLimitMs.Should().Be(60000);
    o.InputTimeoutMs.Should().Be(10000);
    o.CaptureTimeoutMs.Should().Be(10000);
    o.TransportCheckTimeoutMs.Should().Be(5000);
    o.QueueGracePeriodMs.Should().Be(120000);
    o.QueueCheckIntervalMs.Should().Be(30000);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-5)]
  public void NormalizedClampsValuesBelowTheMinimum(int value) {
    var o = new DeviceLivenessOptions {
      StaleLimitMs = value,
      CaptureStallLimitMs = value,
      InputTimeoutMs = value,
      CaptureTimeoutMs = value,
      TransportCheckTimeoutMs = value,
      QueueGracePeriodMs = value,
      QueueCheckIntervalMs = value
    }.Normalized();

    o.StaleLimitMs.Should().Be(1000);
    o.CaptureStallLimitMs.Should().Be(1000);
    o.InputTimeoutMs.Should().Be(100);
    o.CaptureTimeoutMs.Should().Be(100);
    o.TransportCheckTimeoutMs.Should().Be(100);
    o.QueueGracePeriodMs.Should().Be(0);
    o.QueueCheckIntervalMs.Should().Be(1000);
  }

  [Fact]
  public void NormalizedKeepsValuesAboveTheMinimum() {
    var o = new DeviceLivenessOptions { StaleLimitMs = 1234, QueueGracePeriodMs = 7 }.Normalized();

    o.StaleLimitMs.Should().Be(1234);
    o.QueueGracePeriodMs.Should().Be(7);
  }

  [Fact]
  public void NormalizedDoesNotChangeTheSource() {
    var source = new DeviceLivenessOptions { InputTimeoutMs = 1 };

    var copy = source.Normalized();

    copy.Should().NotBeSameAs(source);
    source.InputTimeoutMs.Should().Be(1);
    copy.InputTimeoutMs.Should().Be(100);
  }
}

using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 094: the bound the engine applies is the one the API publishes.</summary>
public sealed class SequenceTimeLimitsTests {
  [Theory]
  [InlineData(null, 240000)]
  [InlineData(0, 240000)]
  [InlineData(-5, 240000)]
  [InlineData(1200000, 1200000)]
  public void ResolveUsesAPositiveOverrideOtherwiseTheDefault(int? overrideMs, int expected) {
    SequenceTimeLimits.Resolve(overrideMs).Should().Be(expected);
  }

  [Fact]
  public void PublishedBoundsMatchTheDocumentedValues() {
    SequenceTimeLimits.DefaultWatchdogTimeoutMs.Should().Be(240000);
    SequenceTimeLimits.MaxWatchdogTimeoutMs.Should().Be(1800000);
  }
}

using FluentAssertions;
using GameBot.Domain.WaitForStill;
using Xunit;

namespace GameBot.UnitTests.WaitForStill;

/// <summary>Feature 174 (FR-010): the step has exactly two outcome names.</summary>
public sealed class WaitForStillOutcomesTests {
  [Fact]
  public void TheOutcomeNamesAreStillAndTimeoutElapsed() {
    WaitForStillOutcomes.Still.Should().Be("still");
    WaitForStillOutcomes.TimeoutElapsed.Should().Be("timeout_elapsed");
  }

  [Theory]
  [InlineData("still", true)]
  [InlineData("STILL", true)]
  [InlineData("timeout_elapsed", true)]
  [InlineData("failed", false)]
  [InlineData("image_unavailable", false)]
  [InlineData("success", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  public void OnlyTheTwoNamesAreOutcomes(string? name, bool expected) {
    WaitForStillOutcomes.IsWaitForStillOutcome(name).Should().Be(expected);
  }
}

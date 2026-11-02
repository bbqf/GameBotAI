using FluentAssertions;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 174: the WaitForStill outcome names as expected states of a commandOutcome condition.</summary>
public sealed class StepOutcomeStatesTests {
  [Theory]
  [InlineData("still", true)]
  [InlineData("timeout_elapsed", true)]
  [InlineData("STILL", true)]
  [InlineData("success", true)]
  [InlineData("pick_2", true)]
  [InlineData("maybe", false)]
  [InlineData("image_unavailable", false)]
  [InlineData(null, false)]
  public void TheExpectedStateIsAllowedForTheTwoNewNames(string? state, bool allowed) {
    StepOutcomeStates.IsExpectedStateAllowed(state).Should().Be(allowed);
  }

  [Theory]
  [InlineData("still", "success", true)]
  [InlineData("still", "still", true)]
  [InlineData("still", "timeout_elapsed", false)]
  [InlineData("timeout_elapsed", "timeout_elapsed", true)]
  [InlineData("timeout_elapsed", "success", false)]
  [InlineData("timeout_elapsed", "still", false)]
  public void StillMatchesSuccessAndTimeoutElapsedMatchesOnlyItself(string actual, string expected, bool matches) {
    StepOutcomeStates.Matches(actual, expected).Should().Be(matches);
  }

  [Fact]
  public void TheListOfBaseStatesStaysTheSame() {
    StepOutcomeStates.AllText.Should().Be("success|failed|skipped|break|no_break|restarted");
  }
}

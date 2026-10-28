using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 167: the helper that lists the four readable states of a step inside a command.</summary>
public sealed class CommandStepOutcomeStatesTests {
  [Theory]
  [InlineData("executed")]
  [InlineData("EXECUTED")]
  [InlineData("Skipped_Detection_Failed")]
  [InlineData("completed_timeout")]
  [InlineData("Image_Detected")]
  public void IsAllowedAcceptsTheFourStatesInAnyCase(string state) {
    CommandStepOutcomeStates.IsAllowed(state).Should().BeTrue();
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("  ")]
  [InlineData("detection_failed_after_3_retries")]
  [InlineData("failed")]
  [InlineData("success")]
  public void IsAllowedRejectsEmptyTextAndOtherValues(string? state) {
    CommandStepOutcomeStates.IsAllowed(state).Should().BeFalse();
  }

  [Fact]
  public void AllTextListsTheFourStatesInOrder() {
    CommandStepOutcomeStates.AllText.Should().Be("executed|skipped_detection_failed|completed_timeout|image_detected");
    CommandStepOutcomeStates.All.Should().HaveCount(4);
  }
}

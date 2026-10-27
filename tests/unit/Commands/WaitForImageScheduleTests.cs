using FluentAssertions;
using GameBot.Domain.Commands.Execution;
using Xunit;

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 166 (FR-010): the poll schedule of a <c>WaitForImage</c> step. No pass starts at or after the
/// deadline, and the last wait ends 50 ms before the deadline.
/// </summary>
public sealed class WaitForImageScheduleTests {
  private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);

  [Fact]
  public void RemainingMoreThanIntervalWaitsOneInterval() {
    var next = WaitForImageSchedule.NextWait(TimeSpan.FromMilliseconds(1500), PollInterval);

    next.Stop.Should().BeFalse();
    next.Wait.Should().Be(TimeSpan.FromMilliseconds(1000));
    next.IsLastWait.Should().BeFalse();
  }

  [Fact]
  public void RemainingEqualToIntervalIsTheLastWait() {
    var next = WaitForImageSchedule.NextWait(TimeSpan.FromMilliseconds(1000), PollInterval);

    next.Stop.Should().BeFalse();
    next.Wait.Should().Be(TimeSpan.FromMilliseconds(950));
    next.IsLastWait.Should().BeTrue();
  }

  [Fact]
  public void LastWaitEndsFiftyMillisecondsBeforeTheDeadline() {
    var next = WaitForImageSchedule.NextWait(TimeSpan.FromMilliseconds(120), PollInterval);

    next.Stop.Should().BeFalse();
    next.Wait.Should().Be(TimeSpan.FromMilliseconds(70));
    next.IsLastWait.Should().BeTrue();
  }

  [Fact]
  public void RemainingLessThanFiftyMillisecondsGivesNoWait() {
    var next = WaitForImageSchedule.NextWait(TimeSpan.FromMilliseconds(30), PollInterval);

    next.Stop.Should().BeFalse();
    next.Wait.Should().Be(TimeSpan.Zero);
    next.IsLastWait.Should().BeTrue();
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-25)]
  public void NoRemainingTimeStops(int remainingMs) {
    var next = WaitForImageSchedule.NextWait(TimeSpan.FromMilliseconds(remainingMs), PollInterval);

    next.Stop.Should().BeTrue();
    next.Wait.Should().Be(TimeSpan.Zero);
  }
}

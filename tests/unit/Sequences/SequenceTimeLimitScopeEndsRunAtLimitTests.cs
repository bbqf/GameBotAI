using System.Threading;
using FluentAssertions;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 170: the scope tells the finalize whether the run ends with success at the limit.</summary>
public sealed class SequenceTimeLimitScopeEndsRunAtLimitTests {
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void EndsRunAtLimitFollowsTheValueGivenToPush(bool ends) {
    using var timer = new CancellationTokenSource();
    using var scope = SequenceTimeLimitScope.Push(150, timer.Token, CancellationToken.None, ends);

    SequenceTimeLimitScope.Current!.EndsRunAtLimit.Should().Be(ends);
  }

  [Fact]
  public void EndsRunAtLimitIsFalseByDefault() {
    using var timer = new CancellationTokenSource();
    using var scope = SequenceTimeLimitScope.Push(150, timer.Token, CancellationToken.None);

    SequenceTimeLimitScope.Current!.EndsRunAtLimit.Should().BeFalse();
  }

  [Fact]
  public void EndsRunAtLimitStaysWithHasElapsed() {
    using var timer = new CancellationTokenSource();
    using var scope = SequenceTimeLimitScope.Push(150, timer.Token, CancellationToken.None, endsRunAtLimit: true);
    var current = SequenceTimeLimitScope.Current!;

    current.HasElapsed.Should().BeFalse();
    timer.Cancel();

    current.HasElapsed.Should().BeTrue();
    current.EndsRunAtLimit.Should().BeTrue();
  }

  [Fact]
  public void AStopRequestKeepsHasElapsedFalse() {
    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var scope = SequenceTimeLimitScope.Push(150, timer.Token, stop.Token, endsRunAtLimit: true);

    stop.Cancel();

    SequenceTimeLimitScope.Current!.HasElapsed.Should().BeFalse();
  }
}

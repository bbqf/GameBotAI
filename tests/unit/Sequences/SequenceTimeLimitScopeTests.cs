using System.Threading;
using FluentAssertions;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 094: the ambient time-limit scope is how a sequence's log entry learns that the queue's
/// time bound — and not a stop request — ended its firing.
/// </summary>
public sealed class SequenceTimeLimitScopeTests {
  [Fact]
  public void CurrentIsNullOutsideAnyScope() {
    SequenceTimeLimitScope.Current.Should().BeNull();
  }

  [Fact]
  public void PushSetsCurrentAndDisposeRestoresThePreviousScope() {
    using var outerTimer = new CancellationTokenSource();
    using var innerTimer = new CancellationTokenSource();

    using (SequenceTimeLimitScope.Push(1000, outerTimer.Token, CancellationToken.None)) {
      SequenceTimeLimitScope.Current!.TimeLimitMs.Should().Be(1000);

      using (SequenceTimeLimitScope.Push(2000, innerTimer.Token, CancellationToken.None)) {
        SequenceTimeLimitScope.Current!.TimeLimitMs.Should().Be(2000);
      }

      SequenceTimeLimitScope.Current!.TimeLimitMs.Should().Be(1000);
    }

    SequenceTimeLimitScope.Current.Should().BeNull();
  }

  [Fact]
  public void HasElapsedOnlyWhenTheTimerFiredAndNoStopWasRequested() {
    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var scope = SequenceTimeLimitScope.Push(150, timer.Token, stop.Token);
    var current = SequenceTimeLimitScope.Current!;

    current.HasElapsed.Should().BeFalse("nothing has fired yet");

    timer.Cancel();
    current.HasElapsed.Should().BeTrue("the bound fired and nobody stopped the run");

    stop.Cancel();
    current.HasElapsed.Should().BeFalse("a stop request is never reported as a time-limit cancellation");
  }

  [Fact]
  public void AStopAloneIsNotElapsed() {
    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var scope = SequenceTimeLimitScope.Push(150, timer.Token, stop.Token);

    stop.Cancel();

    SequenceTimeLimitScope.Current!.HasElapsed.Should().BeFalse();
  }
}

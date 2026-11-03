using FluentAssertions;
using GameBot.Domain.Commands.Execution;
using Xunit;

namespace GameBot.UnitTests.Commands;

public sealed class AbsentCaptureCounterTests {
  [Fact]
  public void ReachesTheNeededCountAfterConsecutiveAbsentPasses() {
    var counter = new AbsentCaptureCounter(2);
    counter.Observe(PassKind.Absent).Should().BeFalse();
    counter.Observe(PassKind.Absent).Should().BeTrue();
  }

  [Fact]
  public void NeededCountOfOneReachesAtTheFirstAbsentPass() {
    new AbsentCaptureCounter(1).Observe(PassKind.Absent).Should().BeTrue();
  }

  [Fact]
  public void SeenResetsTheCount() {
    var counter = new AbsentCaptureCounter(2);
    counter.Observe(PassKind.Absent);
    counter.Observe(PassKind.Seen).Should().BeFalse();
    counter.Count.Should().Be(0);
    counter.Observe(PassKind.Absent).Should().BeFalse();
    counter.Observe(PassKind.Absent).Should().BeTrue();
  }

  [Fact]
  public void FailedResetsTheCount() {
    var counter = new AbsentCaptureCounter(2);
    counter.Observe(PassKind.Absent);
    counter.Observe(PassKind.Failed).Should().BeFalse();
    counter.Count.Should().Be(0);
  }

  [Fact]
  public void FailedPassesAloneNeverReach() {
    var counter = new AbsentCaptureCounter(1);
    counter.Observe(PassKind.Failed).Should().BeFalse();
    counter.Observe(PassKind.Failed).Should().BeFalse();
  }

  [Fact]
  public void FlickerDoesNotReachTheNeededCount() {
    var counter = new AbsentCaptureCounter(2);
    foreach (var pass in new[] { PassKind.Seen, PassKind.Absent, PassKind.Seen, PassKind.Absent, PassKind.Seen }) {
      counter.Observe(pass).Should().BeFalse();
    }
  }

  [Fact]
  public void NeededCountBelowOneCountsAsOne() {
    new AbsentCaptureCounter(0).Observe(PassKind.Absent).Should().BeTrue();
  }
}

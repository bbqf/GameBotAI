#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.UnitTests.Queues;
using Xunit;

namespace GameBot.UnitTests.Sessions;

/// <summary>Feature 106 (data-model section 2): the transitions of the liveness tracker.</summary>
public sealed class DeviceLivenessTrackerTests {
  private static readonly DateTimeOffset T0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

  private readonly FakeTimeProvider _clock = new(T0);
  private readonly DeviceLivenessTracker _tracker;

  public DeviceLivenessTrackerTests() {
    _tracker = new DeviceLivenessTracker(_clock);
  }

  private DeviceLivenessSample S(string id = "s1") => _tracker.Sample(id, hasDevice: true);

  [Fact]
  public void LoopStartedClearsTheEarlierData() {
    _tracker.LoopStarted("s1");
    _tracker.RecordCapture("s1", changed: true);
    _tracker.RecordInputStarted("s1");
    _clock.Advance(TimeSpan.FromSeconds(5));

    _tracker.LoopStarted("s1");

    var s = S();
    s.CaptureLoopRunning.Should().BeTrue();
    s.LoopStartedAt.Should().Be(T0.AddSeconds(5));
    s.LastCaptureAt.Should().BeNull();
    s.LastChangeAt.Should().BeNull();
    s.FirstInputAfterChangeAt.Should().BeNull();
    s.LastInputAt.Should().BeNull();
    s.LastInputOutcome.Should().BeNull();
  }

  [Fact]
  public void LoopStoppedKeepsTheData() {
    _tracker.LoopStarted("s1");
    _tracker.RecordCapture("s1", changed: true);

    _tracker.LoopStopped("s1");

    var s = S();
    s.CaptureLoopRunning.Should().BeFalse();
    s.LastCaptureAt.Should().Be(T0);
    s.LoopStartedAt.Should().Be(T0);
  }

  [Fact]
  public void RecordCaptureWithAndWithoutAChange() {
    _tracker.LoopStarted("s1");
    _tracker.RecordCapture("s1", changed: true);
    _clock.Advance(TimeSpan.FromSeconds(2));

    _tracker.RecordCapture("s1", changed: false);

    var s = S();
    s.LastCaptureAt.Should().Be(T0.AddSeconds(2));
    s.LastChangeAt.Should().Be(T0);
  }

  [Fact]
  public void TheFirstInputAfterAChangeSetsTheMarkAndASecondInputDoesNotMoveIt() {
    _tracker.LoopStarted("s1");
    _tracker.RecordCapture("s1", changed: true);
    _clock.Advance(TimeSpan.FromSeconds(1));
    _tracker.RecordInputStarted("s1");
    _clock.Advance(TimeSpan.FromSeconds(1));

    _tracker.RecordInputStarted("s1");

    var s = S();
    s.FirstInputAfterChangeAt.Should().Be(T0.AddSeconds(1));
    s.LastInputAt.Should().Be(T0.AddSeconds(2));
  }

  [Fact]
  public void AFrameChangeClearsTheFirstInputMark() {
    _tracker.LoopStarted("s1");
    _tracker.RecordInputStarted("s1");

    _tracker.RecordCapture("s1", changed: true);

    S().FirstInputAfterChangeAt.Should().BeNull();
  }

  [Fact]
  public void AnInputGoesFromPendingToCompleted() {
    _tracker.RecordInputStarted("s1");
    S().LastInputOutcome.Should().Be(InputOutcome.Pending);

    _tracker.RecordInputCompleted("s1", InputOutcome.Completed);

    S().LastInputOutcome.Should().Be(InputOutcome.Completed);
    S().LastInputAt.Should().Be(T0);
  }

  [Fact]
  public void RemoveDeletesTheRecord() {
    _tracker.LoopStarted("s1");
    _tracker.RecordInputStarted("s1");

    _tracker.Remove("s1");

    S().Should().Be(new DeviceLivenessSample(true, null, false, null, null, null, null, null, null));
    _tracker.HasCaptureData("s1").Should().BeFalse();
  }

  [Fact]
  public void WritesAfterRemoveDoNotMakeTheRecordAgain() {
    _tracker.LoopStarted("s1");
    _tracker.Remove("s1");

    _tracker.RecordCapture("s1", changed: true);
    _tracker.RecordInputCompleted("s1", InputOutcome.TimedOut);
    _tracker.LoopStopped("s1");

    _tracker.HasCaptureData("s1").Should().BeFalse();
    S().LastInputOutcome.Should().BeNull();
    S().LastCaptureAt.Should().BeNull();
  }

  [Fact]
  public void RecordCaptureAfterLoopStoppedChangesNothing() {
    _tracker.LoopStarted("s1");
    _tracker.LoopStopped("s1");
    _clock.Advance(TimeSpan.FromSeconds(3));

    _tracker.RecordCapture("s1", changed: true);

    S().LastCaptureAt.Should().BeNull();
    S().LastChangeAt.Should().BeNull();
  }

  [Fact]
  public void HasCaptureDataFollowsTheLoop() {
    _tracker.HasCaptureData("s1").Should().BeFalse();
    _tracker.RecordInputStarted("s1");
    _tracker.HasCaptureData("s1").Should().BeFalse("an input record has no loop data");

    _tracker.LoopStarted("s1");
    _tracker.HasCaptureData("s1").Should().BeTrue();

    _tracker.LoopStopped("s1");
    _tracker.HasCaptureData("s1").Should().BeTrue("the loop ran for the session");
  }

  [Fact]
  public void TwoSessionsOnOneDeviceStaySeparate() {
    _tracker.LoopStarted("s1");
    _tracker.RecordCapture("s1", changed: true);
    _tracker.RecordInputStarted("s2");

    S("s1").LastInputAt.Should().BeNull();
    S("s2").LastCaptureAt.Should().BeNull();
    S("s2").LastInputAt.Should().Be(T0);
  }

  [Fact]
  public void AnUnknownSessionGivesAnEmptySample() {
    var s = _tracker.Sample("nope", hasDevice: false, transportReady: true);

    s.Should().Be(new DeviceLivenessSample(false, true, false, null, null, null, null, null, null));
  }

  [Fact]
  public void NowIsTheTrackerClock() {
    _tracker.Now.Should().Be(T0);
  }

  [Fact]
  public async Task ManyParallelWritersDoNotThrow() {
    var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() => {
      var id = "s" + (i % 4);
      for (var n = 0; n < 500; n++) {
        _tracker.LoopStarted(id);
        _tracker.RecordCapture(id, n % 2 == 0);
        _tracker.RecordInputStarted(id);
        _tracker.RecordInputCompleted(id, InputOutcome.Completed);
        _ = _tracker.Sample(id, true);
        if (n % 50 == 0) _tracker.Remove(id);
        _tracker.LoopStopped(id);
      }
    })).ToArray();

    var act = async () => await Task.WhenAll(tasks);

    await act.Should().NotThrowAsync();
  }
}

#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using FluentAssertions;
using GameBot.Domain.Sessions;
using Xunit;

namespace GameBot.UnitTests.Sessions;

/// <summary>
/// Feature 106 (FR-002 to FR-006, research R-002): one test for each rule of the evaluation table, the
/// order of the rules, the stale flag and the probe need.
/// </summary>
public sealed class DeviceLivenessEvaluatorTests {
  private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
  private static readonly DeviceLivenessOptions Options = new();

  private static DateTimeOffset Ago(double seconds) => Now.AddSeconds(-seconds);

  private static DeviceLivenessSample Sample(
    bool hasDevice = true,
    bool? transportReady = null,
    bool loop = true,
    DateTimeOffset? loopStartedAt = null,
    DateTimeOffset? lastCaptureAt = null,
    DateTimeOffset? lastChangeAt = null,
    DateTimeOffset? firstInputAfterChangeAt = null,
    DateTimeOffset? lastInputAt = null,
    InputOutcome? outcome = null) =>
    new(hasDevice, transportReady, loop, loopStartedAt ?? (loop ? Ago(1000) : null), lastCaptureAt, lastChangeAt,
      firstInputAfterChangeAt, lastInputAt, outcome);

  private static DeviceLivenessReport Evaluate(DeviceLivenessSample s) => DeviceLivenessEvaluator.Evaluate(s, Options, Now);

  // ── Rule 1: no device ──────────────────────────────────────────────────

  [Fact]
  public void NoDeviceGivesUnknown() {
    var r = Evaluate(Sample(hasDevice: false, loop: false));

    r.State.Should().Be(DeviceLivenessStates.Unknown);
    r.Reason.Should().BeNull();
    r.NeedsProbe.Should().BeFalse("a stub session has nothing to probe");
    r.Stale.Should().BeFalse();
  }

  // ── Rule 2: transport ──────────────────────────────────────────────────

  [Fact]
  public void TransportNotReadyGivesNotLive() {
    var r = Evaluate(Sample(transportReady: false, lastCaptureAt: Ago(1), lastChangeAt: Ago(1)));

    r.State.Should().Be(DeviceLivenessStates.NotLive);
    r.Reason.Should().Be(DeviceLivenessReasons.TransportNotReady);
  }

  [Fact]
  public void TransportComesBeforeInputTimeout() {
    var r = Evaluate(Sample(transportReady: false, lastCaptureAt: Ago(1), lastChangeAt: Ago(100),
      lastInputAt: Ago(20), outcome: InputOutcome.TimedOut));

    r.Reason.Should().Be(DeviceLivenessReasons.TransportNotReady);
  }

  // ── Rule 3: input time-out ─────────────────────────────────────────────

  [Fact]
  public void TimedOutInputGivesInputTimeout() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(1), lastChangeAt: Ago(100), lastInputAt: Ago(20), outcome: InputOutcome.TimedOut));

    r.State.Should().Be(DeviceLivenessStates.NotLive);
    r.Reason.Should().Be(DeviceLivenessReasons.InputTimeout);
    r.LastInputOutcome.Should().Be("timed_out");
    r.LastInputAt.Should().Be(Ago(20));
  }

  [Fact]
  public void PendingInputOlderThanTheLimitGivesInputTimeout() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(1), lastChangeAt: Ago(100), lastInputAt: Ago(11), outcome: InputOutcome.Pending));

    r.Reason.Should().Be(DeviceLivenessReasons.InputTimeout);
  }

  [Fact]
  public void PendingInputWithinTheLimitIsLive() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(1), lastChangeAt: Ago(100), lastInputAt: Ago(5), outcome: InputOutcome.Pending));

    r.State.Should().Be(DeviceLivenessStates.Live);
  }

  [Fact]
  public void AFrameChangeAfterATimedOutInputClearsInputTimeout() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(1), lastChangeAt: Ago(2), lastInputAt: Ago(20), outcome: InputOutcome.TimedOut));

    r.State.Should().Be(DeviceLivenessStates.Live);
  }

  [Fact]
  public void ALaterCompletedInputClearsInputTimeout() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(1), lastChangeAt: Ago(100), lastInputAt: Ago(3), outcome: InputOutcome.Completed));

    r.State.Should().Be(DeviceLivenessStates.Live);
  }

  [Theory]
  [InlineData(InputOutcome.Cancelled)]
  [InlineData(InputOutcome.Failed)]
  public void CancelledOrFailedInputDoesNotGiveInputTimeout(InputOutcome outcome) {
    var r = Evaluate(Sample(lastCaptureAt: Ago(1), lastChangeAt: Ago(100), lastInputAt: Ago(60), outcome: outcome));

    r.State.Should().Be(DeviceLivenessStates.Live);
    r.Reason.Should().BeNull();
  }

  [Fact]
  public void InputTimeoutComesBeforeCaptureStall() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(120), lastChangeAt: Ago(120), lastInputAt: Ago(20), outcome: InputOutcome.TimedOut));

    r.Reason.Should().Be(DeviceLivenessReasons.InputTimeout);
  }

  // ── Rule 4: capture stall ──────────────────────────────────────────────

  [Fact]
  public void NoCaptureForLongerThanTheStallLimitGivesCaptureStalled() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(61), lastChangeAt: Ago(61)));

    r.State.Should().Be(DeviceLivenessStates.NotLive);
    r.Reason.Should().Be(DeviceLivenessReasons.CaptureStalled);
    r.FrameAgeMs.Should().Be(61000);
    r.Stale.Should().BeTrue();
  }

  [Fact]
  public void StallUsesTheLoopStartBeforeTheFirstCapture() {
    Evaluate(Sample(loopStartedAt: Ago(61))).Reason.Should().Be(DeviceLivenessReasons.CaptureStalled);

    var young = Evaluate(Sample(loopStartedAt: Ago(5)));
    young.State.Should().Be(DeviceLivenessStates.Unknown);
    young.NeedsProbe.Should().BeTrue();
  }

  [Fact]
  public void StallComesBeforeNoChangeAfterInput() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(400), lastChangeAt: Ago(1000), firstInputAfterChangeAt: Ago(900),
      lastInputAt: Ago(900), outcome: InputOutcome.Completed));

    r.Reason.Should().Be(DeviceLivenessReasons.CaptureStalled);
  }

  // ── Rule 5: no change after input ──────────────────────────────────────

  [Fact]
  public void NoChangeForTheStaleLimitAfterTheFirstInputGivesNoChangeAfterInput() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(1), lastChangeAt: Ago(600), firstInputAfterChangeAt: Ago(301),
      lastInputAt: Ago(10), outcome: InputOutcome.Completed));

    r.State.Should().Be(DeviceLivenessStates.NotLive);
    r.Reason.Should().Be(DeviceLivenessReasons.NoChangeAfterInput);
    r.Stale.Should().BeTrue();
  }

  [Fact]
  public void AStaticScreenAndOneTapNowIsLiveAndStale() {
    // The screen is static for 10 minutes, and one tap arrives now: the tap has the full stale limit.
    var r = Evaluate(Sample(lastCaptureAt: Ago(0.5), lastChangeAt: Ago(600), firstInputAfterChangeAt: Now,
      lastInputAt: Now, outcome: InputOutcome.Completed));

    r.State.Should().Be(DeviceLivenessStates.Live);
    r.Stale.Should().BeTrue();
  }

  [Fact]
  public void TheSameSampleAfterTheStaleLimitGivesNoChangeAfterInput() {
    var tapAt = Ago(0);
    var sample = Sample(lastCaptureAt: Ago(0.5), lastChangeAt: Ago(600), firstInputAfterChangeAt: tapAt,
      lastInputAt: tapAt, outcome: InputOutcome.Completed);
    var later = Now.AddMilliseconds(Options.StaleLimitMs + 1000);
    var laterSample = sample with { LastCaptureAt = later.AddMilliseconds(-500) };

    var r = DeviceLivenessEvaluator.Evaluate(laterSample, Options, later);

    r.Reason.Should().Be(DeviceLivenessReasons.NoChangeAfterInput);
  }

  [Fact]
  public void OneInertTapWithinTheStaleLimitIsLive() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(1), lastChangeAt: Ago(200), firstInputAfterChangeAt: Ago(100),
      lastInputAt: Ago(100), outcome: InputOutcome.Completed));

    r.State.Should().Be(DeviceLivenessStates.Live);
  }

  // ── Rule 6 and the stale flag ──────────────────────────────────────────

  [Fact]
  public void AStaticScreenWithNoInputIsLiveAndStale() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(0.5), lastChangeAt: Ago(900)));

    r.State.Should().Be(DeviceLivenessStates.Live);
    r.Stale.Should().BeTrue("SC-006: a stale capture alone does not make the device not live");
    r.UnchangedMs.Should().Be(900000);
  }

  [Fact]
  public void ChangingFramesAreLiveAndNotStale() {
    var r = Evaluate(Sample(lastCaptureAt: Ago(0.5), lastChangeAt: Ago(0.5)));

    r.State.Should().Be(DeviceLivenessStates.Live);
    r.Stale.Should().BeFalse();
    r.FrameAgeMs.Should().Be(500);
    r.UnchangedMs.Should().Be(500);
    r.NeedsProbe.Should().BeFalse();
  }

  // ── Rule 7: unknown with a probe ───────────────────────────────────────

  [Fact]
  public void NoLoopAndNoDataGiveUnknownWithAProbe() {
    var r = Evaluate(Sample(loop: false));

    r.State.Should().Be(DeviceLivenessStates.Unknown);
    r.NeedsProbe.Should().BeTrue();
    r.FrameAgeMs.Should().BeNull();
    r.UnchangedMs.Should().BeNull();
  }

  [Fact]
  public void AStoppedLoopWithOldDataAndANewInputGivesUnknownWithAProbe() {
    var r = Evaluate(Sample(loop: false, loopStartedAt: Ago(3000), lastCaptureAt: Ago(2000), lastChangeAt: Ago(2000),
      firstInputAfterChangeAt: Ago(1), lastInputAt: Ago(1), outcome: InputOutcome.Completed));

    r.State.Should().Be(DeviceLivenessStates.Unknown);
    r.NeedsProbe.Should().BeTrue();
    r.Stale.Should().BeFalse("old frame data of a stopped loop is not a fault signal");
  }
}

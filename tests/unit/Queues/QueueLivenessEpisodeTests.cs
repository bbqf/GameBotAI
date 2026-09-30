using System;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 106 (data-model section 8): the fault episode of a queue run.</summary>
public sealed class QueueLivenessEpisodeTests {
  private static readonly DateTimeOffset T0 = new(2026, 9, 25, 3, 0, 0, TimeSpan.FromHours(2));
  private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

  private static DeviceLivenessReport NotLive(string reason) =>
    new(DeviceLivenessStates.NotLive, reason, 1000, 1000, true, null, null, false);

  private static DeviceLivenessReport Of(string state) => new(state, null, 10, 10, false, null, null, false);

  [Theory]
  [InlineData(DeviceLivenessReasons.CaptureStalled)]
  [InlineData(DeviceLivenessReasons.InputTimeout)]
  [InlineData(DeviceLivenessReasons.NoChangeAfterInput)]
  [InlineData(DeviceLivenessReasons.TransportNotReady)]
  public void NotLiveOpensAnEpisode(string reason) {
    var e = new QueueLivenessEpisode();

    e.Observe(NotLive(reason), T0);

    var s = e.Snapshot();
    s.State.Should().Be(DeviceLivenessStates.NotLive);
    s.Reason.Should().Be(reason);
    s.NotLiveSince.Should().Be(T0);
  }

  [Fact]
  public void ASecondNotLiveKeepsTheStartTime() {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);

    e.Observe(NotLive(DeviceLivenessReasons.InputTimeout), T0.AddMinutes(1));

    e.Snapshot().NotLiveSince.Should().Be(T0);
    e.Snapshot().Reason.Should().Be(DeviceLivenessReasons.InputTimeout);
  }

  [Theory]
  [InlineData(DeviceLivenessStates.Live)]
  [InlineData(DeviceLivenessStates.Unknown)]
  public void LiveOrUnknownClosesTheEpisodeAndClearsItsData(string state) {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);
    e.RecordGatedFiring("A");
    e.TryClaimFaultCycle(T0.AddMinutes(3), Grace).Should().BeTrue();

    e.Observe(Of(state), T0.AddMinutes(4));

    var s = e.Snapshot();
    s.State.Should().Be(state);
    s.Reason.Should().BeNull();
    s.NotLiveSince.Should().BeNull();
    s.GatedFirings.Should().Be(0);
  }

  [Fact]
  public void RecordGatedFiringIsTrueOnlyForTheFirstHeldFiringOfEachSequence() {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);

    e.RecordGatedFiring("A").Should().BeTrue();
    e.RecordGatedFiring("A").Should().BeFalse();
    e.RecordGatedFiring("B").Should().BeTrue("a different sequence gets its own entry");
    e.RecordGatedFiring("A").Should().BeFalse();

    e.Snapshot().GatedFirings.Should().Be(4, "each held firing counts");
  }

  [Fact]
  public void TryClaimFaultCycleIsFalseBeforeTheGraceAndTrueOneTimeAfterIt() {
    var e = new QueueLivenessEpisode();
    e.TryClaimFaultCycle(T0.AddHours(1), Grace).Should().BeFalse("no episode is open");
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);

    e.TryClaimFaultCycle(T0.AddMinutes(1), Grace).Should().BeFalse();
    e.TryClaimFaultCycle(T0.AddMinutes(3), Grace).Should().BeTrue();
    e.TryClaimFaultCycle(T0.AddMinutes(10), Grace).Should().BeFalse();
  }

  [Fact]
  public void TryClaimFaultCycleIsTrueAlsoAfterGateEntries() {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);
    e.RecordGatedFiring("A");
    e.RecordGatedFiring("B");

    e.TryClaimFaultCycle(T0.AddMinutes(3), Grace).Should().BeTrue("the two records are separate");
  }

  [Fact]
  public void ANewEpisodeAfterACloseCanRecordAgain() {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);
    e.RecordGatedFiring("A");
    e.TryClaimFaultCycle(T0.AddMinutes(3), Grace);
    e.Observe(Of(DeviceLivenessStates.Live), T0.AddMinutes(4));

    e.Observe(NotLive(DeviceLivenessReasons.InputTimeout), T0.AddMinutes(5));

    e.Snapshot().NotLiveSince.Should().Be(T0.AddMinutes(5));
    e.RecordGatedFiring("A").Should().BeTrue();
    e.TryClaimFaultCycle(T0.AddMinutes(8), Grace).Should().BeTrue();
  }

  private static readonly TimeSpan AlertAfter = TimeSpan.FromMinutes(5);

  [Fact]
  public void TryClaimAlertIsFalseBeforeTheTimeAndTrueOneTimeAfterIt() {
    var e = new QueueLivenessEpisode();
    e.TryClaimAlert(T0.AddHours(1), AlertAfter).Should().BeFalse("no episode is open");
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);

    e.TryClaimAlert(T0.AddMinutes(4), AlertAfter).Should().BeFalse();
    e.TryClaimAlert(T0.AddMinutes(6), AlertAfter).Should().BeTrue();
    e.TryClaimAlert(T0.AddMinutes(9), AlertAfter).Should().BeFalse();
    e.Snapshot().AlertSent.Should().BeTrue();
  }

  [Fact]
  public void LiveAfterAnAlertGivesOneLiveAgainClaim() {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);
    e.TryClaimAlert(T0.AddMinutes(6), AlertAfter).Should().BeTrue();

    e.Observe(Of(DeviceLivenessStates.Live), T0.AddMinutes(7));
    e.Observe(Of(DeviceLivenessStates.Live), T0.AddMinutes(8));

    e.Snapshot().AlertSent.Should().BeFalse("the open episode data is cleared");
    e.TryClaimLiveAgain().Should().BeTrue();
    e.TryClaimLiveAgain().Should().BeFalse();
  }

  [Fact]
  public void LiveWithNoAlertGivesNoLiveAgainClaim() {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);

    e.Observe(Of(DeviceLivenessStates.Live), T0.AddMinutes(1));

    e.TryClaimLiveAgain().Should().BeFalse();
  }

  [Fact]
  public void UnknownAfterAnAlertGivesNoLiveAgainClaim() {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);
    e.TryClaimAlert(T0.AddMinutes(6), AlertAfter);

    e.Observe(Of(DeviceLivenessStates.Unknown), T0.AddMinutes(7));

    e.TryClaimLiveAgain().Should().BeFalse();
  }

  [Fact]
  public void ANewEpisodeGivesANewAlertClaim() {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);
    e.TryClaimAlert(T0.AddMinutes(6), AlertAfter).Should().BeTrue();
    e.Observe(Of(DeviceLivenessStates.Live), T0.AddMinutes(7));

    e.Observe(NotLive(DeviceLivenessReasons.InputTimeout), T0.AddMinutes(8));

    e.TryClaimAlert(T0.AddMinutes(14), AlertAfter).Should().BeTrue();
  }

  [Fact]
  public void TryClaimRecoveryFailedIsTrueOneTimeForAnOpenEpisode() {
    var e = new QueueLivenessEpisode();
    e.TryClaimRecoveryFailed().Should().BeFalse("no episode is open");
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);

    e.TryClaimRecoveryFailed().Should().BeTrue();
    e.TryClaimRecoveryFailed().Should().BeFalse();
    e.Observe(Of(DeviceLivenessStates.Live), T0.AddMinutes(1));
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0.AddMinutes(2));
    e.TryClaimRecoveryFailed().Should().BeTrue("a new episode");
  }

  [Fact]
  public void RecoveryCountersFollowTheAttemptsAndTheCooldown() {
    var e = new QueueLivenessEpisode();
    var after = TimeSpan.FromMinutes(5);
    var cooldown = TimeSpan.FromMinutes(3);
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);

    e.TryBeginRecovery(T0.AddMinutes(4), after, 2, cooldown).Should().BeFalse("too early");
    e.TryBeginRecovery(T0.AddMinutes(6), after, 2, cooldown).Should().BeTrue();
    e.Snapshot().RecoveryRunning.Should().BeTrue();
    e.TryBeginRecovery(T0.AddMinutes(7), after, 2, cooldown).Should().BeFalse("one recovery runs");

    e.EndRecoveryAttempt(T0.AddMinutes(8));
    e.Snapshot().RecoveryRunning.Should().BeFalse();
    e.Snapshot().Attempts.Should().Be(1);
    e.TryBeginRecovery(T0.AddMinutes(9), after, 2, cooldown).Should().BeFalse("cooldown");
    e.TryBeginRecovery(T0.AddMinutes(11), after, 2, cooldown).Should().BeTrue();
    e.EndRecoveryAttempt(T0.AddMinutes(12));
    e.TryBeginRecovery(T0.AddMinutes(30), after, 2, cooldown).Should().BeFalse("attempts used up");
  }

  [Theory]
  [InlineData(DeviceLivenessStates.Live)]
  [InlineData(DeviceLivenessStates.Unknown)]
  public void LiveOrUnknownClearsTheRecoveryCounters(string state) {
    var e = new QueueLivenessEpisode();
    e.Observe(NotLive(DeviceLivenessReasons.CaptureStalled), T0);
    e.TryBeginRecovery(T0.AddMinutes(6), TimeSpan.FromMinutes(5), 2, TimeSpan.Zero).Should().BeTrue();
    e.EndRecoveryAttempt(T0.AddMinutes(7));
    e.TryBeginRecovery(T0.AddMinutes(8), TimeSpan.FromMinutes(5), 2, TimeSpan.Zero).Should().BeTrue();

    e.Observe(Of(state), T0.AddMinutes(9));

    var s = e.Snapshot();
    s.Attempts.Should().Be(0);
    s.RecoveryRunning.Should().BeFalse();
  }

  [Fact]
  public void SnapshotIsACopyWithTheGatedFirings() {
    var e = new QueueLivenessEpisode();
    var report = NotLive(DeviceLivenessReasons.TransportNotReady);
    e.Observe(report, T0);
    e.RecordGatedFiring("A");

    var s = e.Snapshot();
    e.RecordGatedFiring("A");

    s.GatedFirings.Should().Be(1, "a snapshot does not change later");
    s.LastReport.Should().Be(report);
    e.Snapshot().GatedFirings.Should().Be(2);
  }
}

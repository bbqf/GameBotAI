using System;
using FluentAssertions;
using GameBot.Emulator;
using GameBot.UnitTests.Queues;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>Feature 121 (FR-013, FR-014, data-model section 6): the per-device capture gate.</summary>
public sealed class DeviceCaptureGateTests {
  private static readonly DateTimeOffset T0 = new(2026, 9, 30, 3, 0, 0, TimeSpan.Zero);

  [Fact]
  public void IdleToInFlightToIdle() {
    var gate = new DeviceCaptureGate();
    gate.GetState("emu-1").Should().Be(DeviceCaptureState.Idle);

    gate.TryBegin("emu-1").Should().BeTrue();
    gate.GetState("emu-1").Should().Be(DeviceCaptureState.InFlight);
    gate.Completed("emu-1");

    gate.GetState("emu-1").Should().Be(DeviceCaptureState.Idle);
    gate.TryBegin("emu-1").Should().BeTrue();
  }

  [Fact]
  public void AFailedCaptureGivesIdle() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("emu-1");

    gate.Failed("emu-1");

    gate.GetState("emu-1").Should().Be(DeviceCaptureState.Idle);
  }

  [Fact]
  public void ATimedOutCaptureGivesSuspect() {
    var clock = new FakeTimeProvider(T0);
    var gate = new DeviceCaptureGate(clock);
    gate.TryBegin("emu-1");

    gate.TimedOut("emu-1");

    gate.GetState("emu-1").Should().Be(DeviceCaptureState.Suspect);
    gate.IsSuspect("emu-1").Should().BeTrue();
    gate.SuspectSince("emu-1").Should().Be(T0);
  }

  [Fact]
  public void ASecondTryBeginIsRefusedInFlightAndSuspect() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("emu-1").Should().BeTrue();
    gate.TryBegin("emu-1").Should().BeFalse("a capture is in flight");

    gate.TimedOut("emu-1");

    gate.TryBegin("emu-1").Should().BeFalse("the device is suspect");
  }

  [Fact]
  public void TheSerialKeyIgnoresCase() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("EMU-1").Should().BeTrue();

    gate.TryBegin("emu-1").Should().BeFalse();
    gate.Completed("Emu-1");
    gate.TryBegin("emu-1").Should().BeTrue();
  }

  [Fact]
  public void DifferentDevicesAreIndependent() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("emu-1");
    gate.TimedOut("emu-1");

    gate.TryBegin("emu-2").Should().BeTrue();
    gate.IsSuspect("emu-2").Should().BeFalse();
  }

  [Fact]
  public void AnCleanDeviceCheckClearsSuspect() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("emu-1");
    gate.TimedOut("emu-1");

    gate.Clear("emu-1");

    gate.GetState("emu-1").Should().Be(DeviceCaptureState.Idle);
    gate.SuspectSince("emu-1").Should().BeNull();
    gate.TryBegin("emu-1").Should().BeTrue();
  }

  [Fact]
  public void ALateCaptureEndDoesNotClearSuspectByItself() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("emu-1");
    gate.TimedOut("emu-1");

    gate.Completed("emu-1");

    gate.GetState("emu-1").Should().Be(DeviceCaptureState.Suspect, "only the device check or a repair clears it");
  }

  [Fact]
  public void RecordCheckSetsTheLastCheckTimeOfASuspectDevice() {
    var clock = new FakeTimeProvider(T0);
    var gate = new DeviceCaptureGate(clock);
    gate.TryBegin("emu-1");
    gate.TimedOut("emu-1");
    clock.Advance(TimeSpan.FromMinutes(2));

    gate.RecordCheck("emu-1");

    gate.LastCheckAt("emu-1").Should().Be(T0.AddMinutes(2));
    gate.SuspectSince("emu-1").Should().Be(T0);
  }

  [Fact]
  public void TimedOutOnAnIdleDeviceChangesNothing() {
    var gate = new DeviceCaptureGate();

    gate.TimedOut("emu-1");

    gate.GetState("emu-1").Should().Be(DeviceCaptureState.Idle);
  }

  [Fact]
  public void ABlankSerialIsRejectedByTryBegin() {
    var gate = new DeviceCaptureGate();

    FluentActions.Invoking(() => gate.TryBegin(" ")).Should().Throw<ArgumentException>();
    gate.GetState(" ").Should().Be(DeviceCaptureState.Idle);
  }
}

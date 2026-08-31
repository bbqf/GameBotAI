using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 079: exactly one queue run may hold an emulator at a time (FR-008..FR-014). Before this,
/// two queues bound to one serial both ran and fought over the same physical screen.
/// </summary>
public sealed class DeviceClaimRegistryTests {
  [Fact]
  public void AFreeDeviceCanBeClaimed() {
    var registry = new DeviceClaimRegistry();
    registry.TryClaim("emulator-5558", "q1", "Daily").Should().BeTrue();
    registry.TryGetHolder("emulator-5558", out var holder).Should().BeTrue();
    holder.QueueId.Should().Be("q1");
    holder.QueueName.Should().Be("Daily");
    holder.DeviceSerial.Should().Be("emulator-5558");
  }

  [Fact]
  public void ASecondQueueCannotClaimTheSameDevice() {
    var registry = new DeviceClaimRegistry();
    registry.TryClaim("emulator-5558", "q1", "Daily").Should().BeTrue();

    registry.TryClaim("emulator-5558", "q2", "Events").Should().BeFalse();

    registry.TryGetHolder("emulator-5558", out var holder).Should().BeTrue();
    holder.QueueId.Should().Be("q1", "the incumbent must keep the device");
  }

  [Fact]
  public void ReleasingMakesTheDeviceClaimableAgain() {
    var registry = new DeviceClaimRegistry();
    registry.TryClaim("emulator-5558", "q1", "Daily");

    registry.Release("emulator-5558", "q1");

    registry.TryGetHolder("emulator-5558", out _).Should().BeFalse();
    registry.TryClaim("emulator-5558", "q2", "Events").Should().BeTrue();
  }

  [Fact]
  public void SerialsAreTrimmedAndComparedCaseInsensitively() {
    var registry = new DeviceClaimRegistry();
    registry.TryClaim("  emulator-5558  ", "q1", "Daily").Should().BeTrue();

    registry.TryClaim("EMULATOR-5558", "q2", "Events").Should().BeFalse();
    registry.TryGetHolder("emulator-5558", out var holder).Should().BeTrue();
    holder.DeviceSerial.Should().Be("emulator-5558", "the stored serial is normalized");
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void QueuesWithNoBoundSerialNeverBlockEachOther(string? serial) {
    var registry = new DeviceClaimRegistry();

    registry.TryClaim(serial, "q1", "One").Should().BeTrue();
    registry.TryClaim(serial, "q2", "Two").Should().BeTrue();
    registry.TryGetHolder(serial, out _).Should().BeFalse("a blank serial has no device identity");
  }

  [Fact]
  public void ANonHolderCannotReleaseTheClaim() {
    var registry = new DeviceClaimRegistry();
    registry.TryClaim("emulator-5558", "q1", "Daily");

    // A late release from a run that already ended must not drop the claim a newer run now holds.
    registry.Release("emulator-5558", "q2");

    registry.TryGetHolder("emulator-5558", out var holder).Should().BeTrue();
    holder.QueueId.Should().Be("q1");
  }

  [Fact]
  public void ReleaseIsIdempotentAndSafeOnUnknownDevices() {
    var registry = new DeviceClaimRegistry();
    registry.TryClaim("emulator-5558", "q1", "Daily");

    registry.Release("emulator-5558", "q1");
    registry.Release("emulator-5558", "q1");
    registry.Release("never-claimed", "q1");
    registry.Release(null, "q1");

    registry.TryGetHolder("emulator-5558", out _).Should().BeFalse();
  }

  [Fact]
  public void DifferentDevicesDoNotBlockOneAnother() {
    var registry = new DeviceClaimRegistry();

    registry.TryClaim("emulator-5558", "q1", "A").Should().BeTrue();
    registry.TryClaim("emulator-5560", "q2", "B").Should().BeTrue();
    registry.TryClaim("emulator-5562", "q3", "C").Should().BeTrue();
  }

  [Fact]
  public async Task ConcurrentClaimsOnOneDeviceYieldExactlyOneWinner() {
    var registry = new DeviceClaimRegistry();
    const int Contenders = 32;
    using var gate = new Barrier(Contenders);

    var results = await Task.WhenAll(Enumerable.Range(0, Contenders).Select(i => Task.Run(() => {
      gate.SignalAndWait();
      return registry.TryClaim("emulator-5558", $"q{i}", $"Queue {i}");
    }))).ConfigureAwait(false);

    results.Count(won => won).Should().Be(1, "TryClaim must be atomic (FR-012)");
  }

  [Fact]
  public void AFreshRegistryHoldsNoClaims() {
    // FR-013: claims are in-memory only, so a restart (a new registry) voids every one of them.
    var before = new DeviceClaimRegistry();
    before.TryClaim("emulator-5558", "q1", "Daily").Should().BeTrue();

    var afterRestart = new DeviceClaimRegistry();

    afterRestart.TryGetHolder("emulator-5558", out _).Should().BeFalse();
    afterRestart.TryClaim("emulator-5558", "q2", "Events").Should().BeTrue();
  }

  [Fact]
  public void ClaimingRequiresAQueueId() {
    var registry = new DeviceClaimRegistry();
    var act = () => registry.TryClaim("emulator-5558", "  ", "Daily");
    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void TryGetHolderOnAnUnclaimedDeviceReturnsFalse() {
    var registry = new DeviceClaimRegistry();
    registry.TryGetHolder("emulator-5558", out _).Should().BeFalse();
    registry.TryGetHolder(null, out _).Should().BeFalse();
  }
}

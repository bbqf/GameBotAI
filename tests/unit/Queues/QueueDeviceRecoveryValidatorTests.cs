using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 121 (FR-010): the validator of the device recovery settings.</summary>
public sealed class QueueDeviceRecoveryValidatorTests {
  private static QueueDeviceRecovery Valid() => new() { Action = QueueDeviceRecovery.ActionRebootInstance };

  [Fact]
  public void NullSettingsAreValid() {
    QueueDeviceRecoveryValidator.Validate(null, null).Should().BeNull();
  }

  [Fact]
  public void TheDefaultsAreValid() {
    var recovery = new QueueDeviceRecovery();

    recovery.Action.Should().Be("none");
    recovery.AfterMs.Should().Be(300000);
    recovery.MaxAttempts.Should().Be(2);
    recovery.CooldownMs.Should().Be(180000);
    QueueDeviceRecoveryValidator.Validate(recovery, null).Should().BeNull();
  }

  [Fact]
  public void ARebootWithAnInstanceNameIsValid() {
    QueueDeviceRecoveryValidator.Validate(Valid(), "LDPlayer-1").Should().BeNull();
  }

  [Fact]
  public void AnUnknownActionGivesTheContractText() {
    var r = Valid();
    r.Action = "format-disk";

    QueueDeviceRecoveryValidator.Validate(r, "LDPlayer-1")
      .Should().Be("deviceRecovery.action must be one of: none, reboot-instance (was: 'format-disk')");
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void ARebootWithNoInstanceNameGivesTheContractText(string? name) {
    QueueDeviceRecoveryValidator.Validate(Valid(), name)
      .Should().Be("deviceRecovery.action 'reboot-instance' needs emulatorInstanceName. Set emulatorInstanceName on the queue.");
  }

  [Fact]
  public void AfterMsBelowTheMinimumGivesTheContractText() {
    var r = Valid();
    r.AfterMs = 59999;

    QueueDeviceRecoveryValidator.Validate(r, "x")
      .Should().Be("deviceRecovery.afterMs must be at least 60000 (was: 59999)");
  }

  [Theory]
  [InlineData(0)]
  [InlineData(6)]
  [InlineData(-1)]
  public void MaxAttemptsOutsideTheRangeGivesTheContractText(int value) {
    var r = Valid();
    r.MaxAttempts = value;

    QueueDeviceRecoveryValidator.Validate(r, "x")
      .Should().Be($"deviceRecovery.maxAttempts must be from 1 to 5 (was: {value})");
  }

  [Theory]
  [InlineData(1)]
  [InlineData(5)]
  public void MaxAttemptsOnTheLimitsIsValid(int value) {
    var r = Valid();
    r.MaxAttempts = value;

    QueueDeviceRecoveryValidator.Validate(r, "x").Should().BeNull();
  }

  [Fact]
  public void ACooldownBelowZeroGivesTheContractText() {
    var r = Valid();
    r.CooldownMs = -1;

    QueueDeviceRecoveryValidator.Validate(r, "x")
      .Should().Be("deviceRecovery.cooldownMs must be at least 0 (was: -1)");
  }

  [Fact]
  public void ACooldownOfZeroIsValid() {
    var r = Valid();
    r.CooldownMs = 0;

    QueueDeviceRecoveryValidator.Validate(r, "x").Should().BeNull();
  }

  [Fact]
  public void TheRangesAreCheckedAlsoWhenTheActionIsNone() {
    var r = new QueueDeviceRecovery { Action = "none", AfterMs = 5 };

    QueueDeviceRecoveryValidator.Validate(r, null)
      .Should().Be("deviceRecovery.afterMs must be at least 60000 (was: 5)");
  }

  [Fact]
  public void TheActionNoneNeedsNoInstanceName() {
    QueueDeviceRecoveryValidator.Validate(new QueueDeviceRecovery { Action = "none" }, null).Should().BeNull();
  }
}

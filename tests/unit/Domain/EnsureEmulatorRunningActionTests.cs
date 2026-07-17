using FluentAssertions;
using GameBot.Domain.Actions;
using Xunit;

namespace GameBot.UnitTests.Domain;

public sealed class EnsureEmulatorRunningActionTests {
  [Fact]
  public void ValidatePassesWithSerialAndInstanceName() {
    var action = new PrimitiveEnsureEmulatorRunningAction { InstanceName = "LDPlayer-5558", AdbSerial = "emulator-5558" };
    PrimitiveActionValidationService.Validate(action).Should().BeEmpty();
  }

  [Fact]
  public void ValidatePassesWithSerialAndInstanceIndex() {
    var action = new PrimitiveEnsureEmulatorRunningAction { InstanceIndex = 0, AdbSerial = "emulator-5558" };
    PrimitiveActionValidationService.Validate(action).Should().BeEmpty();
  }

  [Fact]
  public void ValidateRejectsMissingSerial() {
    var action = new PrimitiveEnsureEmulatorRunningAction { InstanceIndex = 1 };
    PrimitiveActionValidationService.Validate(action)
      .Should().Contain(e => e.Contains("adbSerial", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void ValidateRejectsMissingInstanceIdentifier() {
    var action = new PrimitiveEnsureEmulatorRunningAction { AdbSerial = "emulator-5558" };
    PrimitiveActionValidationService.Validate(action)
      .Should().Contain(e => e.Contains("instanceName or instanceIndex", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void ValidateRejectsNegativeIndex() {
    var action = new PrimitiveEnsureEmulatorRunningAction { InstanceIndex = -1, AdbSerial = "emulator-5558" };
    PrimitiveActionValidationService.Validate(action)
      .Should().Contain(e => e.Contains("instanceIndex", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void IsListedAsSupportedActionType() {
    PrimitiveActionTypes.All.Should().Contain(PrimitiveActionTypes.EnsureEmulatorRunning);
    PrimitiveActionTypes.EnsureEmulatorRunning.Should().Be(ActionTypes.EnsureEmulatorRunning);
  }

  [Fact]
  public void ArgsTryFromVariantSucceeds() {
    var action = new PrimitiveEnsureEmulatorRunningAction { InstanceName = "LDPlayer-5558", AdbSerial = "emulator-5558" };
    EnsureEmulatorRunningArgs.TryFrom(action, out var args).Should().BeTrue();
    args!.InstanceName.Should().Be("LDPlayer-5558");
    args.AdbSerial.Should().Be("emulator-5558");
  }

  [Fact]
  public void ArgsTryFromVariantFailsWithoutIdentifier() {
    var action = new PrimitiveEnsureEmulatorRunningAction { AdbSerial = "emulator-5558" };
    EnsureEmulatorRunningArgs.TryFrom(action, out _).Should().BeFalse();
  }
}

using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Service.Services.EnsureEmulatorRunning;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Services.EnsureEmulatorRunning;

/// <summary>
/// Covers the two behaviors the sequence dispatcher (DispatchEnsureEmulatorRunningAsync) relies on:
/// building args from the step's Parameters dictionary, and classifying each handler outcome as an
/// "executed" (success/unsupported) or "failed" step result.
/// </summary>
public sealed class EnsureEmulatorRunningDispatchMappingTests {
  [Fact]
  public void BuildsArgsFromParametersWithName() {
    var parameters = new Dictionary<string, object?> {
      ["instanceName"] = "LDPlayer-5558",
      ["adbSerial"] = "emulator-5558"
    };
    EnsureEmulatorRunningArgs.TryFrom(parameters, out var args).Should().BeTrue();
    args!.InstanceName.Should().Be("LDPlayer-5558");
    args.AdbSerial.Should().Be("emulator-5558");
  }

  [Fact]
  public void BuildsArgsFromJsonElementIndex() {
    using var doc = JsonDocument.Parse("{\"instanceIndex\":2,\"adbSerial\":\"emulator-5558\"}");
    var parameters = new Dictionary<string, object?> {
      ["instanceIndex"] = doc.RootElement.GetProperty("instanceIndex"),
      ["adbSerial"] = doc.RootElement.GetProperty("adbSerial")
    };
    EnsureEmulatorRunningArgs.TryFrom(parameters, out var args).Should().BeTrue();
    args!.InstanceIndex.Should().Be(2);
    args.AdbSerial.Should().Be("emulator-5558");
  }

  [Fact]
  public void RejectsWhenSerialMissing() {
    var parameters = new Dictionary<string, object?> { ["instanceName"] = "LDPlayer-5558" };
    EnsureEmulatorRunningArgs.TryFrom(parameters, out _).Should().BeFalse();
  }

  [Fact]
  public void RejectsWhenNoInstanceIdentifier() {
    var parameters = new Dictionary<string, object?> { ["adbSerial"] = "emulator-5558" };
    EnsureEmulatorRunningArgs.TryFrom(parameters, out _).Should().BeFalse();
  }

  [Theory]
  [InlineData(EnsureEmulatorRunningOutcome.AlreadyHealthy, true)]
  [InlineData(EnsureEmulatorRunningOutcome.Started, true)]
  [InlineData(EnsureEmulatorRunningOutcome.Restarted, true)]
  [InlineData(EnsureEmulatorRunningOutcome.PlatformUnsupported, true)]
  [InlineData(EnsureEmulatorRunningOutcome.ControlUnavailable, true)]
  [InlineData(EnsureEmulatorRunningOutcome.RecoveryTimedOut, false)]
  [InlineData(EnsureEmulatorRunningOutcome.InstanceNotFound, false)]
  internal void OutcomeMapsToExpectedStepResult(EnsureEmulatorRunningOutcome outcome, bool expectExecuted) {
    var result = new EnsureEmulatorRunningActionResult(outcome);
    // Dispatcher rule: executed when success or a neutral unsupported outcome; otherwise failed.
    (result.IsSuccess || result.IsUnsupported).Should().Be(expectExecuted);
    result.Message.Should().NotBeNullOrWhiteSpace();
  }
}

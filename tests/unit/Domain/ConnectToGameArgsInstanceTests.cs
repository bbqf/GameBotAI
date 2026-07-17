using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Actions;
using Xunit;

namespace GameBot.UnitTests.Domain;

public sealed class ConnectToGameArgsInstanceTests {
  private static InputAction ConnectInput(params (string Key, object Value)[] args) {
    var a = new InputAction { Type = ActionTypes.ConnectToGame };
    foreach (var (k, v) in args) a.Args[k] = v;
    return a;
  }

  [Fact]
  public void TryFromParametersReadsInstanceName() {
    var action = ConnectInput(("gameId", "pns"), ("adbSerial", "emulator-5558"), ("instanceName", "LDPlayer-5558"));
    ConnectToGameArgs.TryFrom(action, null, out var args).Should().BeTrue();
    args!.InstanceName.Should().Be("LDPlayer-5558");
    args.HasInstanceIdentifier.Should().BeTrue();
  }

  [Fact]
  public void TryFromParametersReadsInstanceIndexFromJsonElement() {
    using var doc = JsonDocument.Parse("{\"instanceIndex\":2}");
    var action = ConnectInput(("gameId", "pns"), ("adbSerial", "emulator-5558"),
      ("instanceIndex", doc.RootElement.GetProperty("instanceIndex")));
    ConnectToGameArgs.TryFrom(action, null, out var args).Should().BeTrue();
    args!.InstanceIndex.Should().Be(2);
    args.HasInstanceIdentifier.Should().BeTrue();
  }

  [Fact]
  public void TryFromWithoutInstanceHasNoIdentifier() {
    var action = ConnectInput(("gameId", "pns"), ("adbSerial", "emulator-5558"));
    ConnectToGameArgs.TryFrom(action, null, out var args).Should().BeTrue();
    args!.InstanceName.Should().BeNull();
    args.InstanceIndex.Should().BeNull();
    args.HasInstanceIdentifier.Should().BeFalse();
  }

  [Fact]
  public void TryFromVariantCarriesInstanceFields() {
    var variant = new PrimitiveConnectToGameAction { GameId = "pns", AdbSerial = "emulator-5558", InstanceIndex = 1 };
    ConnectToGameArgs.TryFrom(variant, out var args).Should().BeTrue();
    args!.InstanceIndex.Should().Be(1);
    args.HasInstanceIdentifier.Should().BeTrue();
  }

  [Fact]
  public void ValidationAcceptsConnectWithoutInstance() {
    var action = new PrimitiveConnectToGameAction { GameId = "pns", AdbSerial = "emulator-5558" };
    PrimitiveActionValidationService.Validate(action).Should().BeEmpty();
  }

  [Fact]
  public void ValidationAcceptsConnectWithInstance() {
    var action = new PrimitiveConnectToGameAction { GameId = "pns", AdbSerial = "emulator-5558", InstanceName = "LDPlayer-5558" };
    PrimitiveActionValidationService.Validate(action).Should().BeEmpty();
  }

  [Fact]
  public void ValidationRejectsNegativeInstanceIndex() {
    var action = new PrimitiveConnectToGameAction { GameId = "pns", AdbSerial = "emulator-5558", InstanceIndex = -1 };
    PrimitiveActionValidationService.Validate(action)
      .Should().Contain(e => e.Contains("instanceIndex", StringComparison.OrdinalIgnoreCase));
  }
}

using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Actions;
using Xunit;

namespace GameBot.UnitTests.Domain;

public sealed class ConnectToGameArgsTests {
  [Fact]
  public void TryFromReturnsArgsWhenTypeMatchesAndFieldsPresent() {
    var input = new InputAction {
      Type = ActionTypes.ConnectToGame,
      Args = new Dictionary<string, object> {
        ["gameId"] = "game-1",
        ["adbSerial"] = "emulator-5554"
      }
    };

    var ok = ConnectToGameArgs.TryFrom(input, null, out var args);

    ok.Should().BeTrue();
    args.Should().NotBeNull();
    args!.GameId.Should().Be("game-1");
    args.AdbSerial.Should().Be("emulator-5554");
  }

  [Fact]
  public void TryFromFallsBackToActionGameIdWhenArgsMissingGameId() {
    var input = new InputAction {
      Type = ActionTypes.ConnectToGame,
      Args = new Dictionary<string, object> {
        ["adbSerial"] = "device-123"
      }
    };

    var ok = ConnectToGameArgs.TryFrom(input, "game-from-action", out var args);

    ok.Should().BeTrue();
    args!.GameId.Should().Be("game-from-action");
    args.AdbSerial.Should().Be("device-123");
  }

  [Fact]
  public void TryFromReturnsFalseWhenRequiredFieldsMissing() {
    var input = new InputAction {
      Type = ActionTypes.ConnectToGame,
      Args = new Dictionary<string, object>()
    };

    var ok = ConnectToGameArgs.TryFrom(input, null, out var args);

    ok.Should().BeFalse();
    args.Should().BeNull();
  }

  [Fact]
  public void ToArgsDictionaryContainsGameAndSerial() {
    var args = new ConnectToGameArgs { GameId = "g", AdbSerial = "s" };

    var dict = args.ToArgsDictionary();

    dict.Should().ContainKey("gameId").WhoseValue.Should().Be("g");
    dict.Should().ContainKey("adbSerial").WhoseValue.Should().Be("s");
  }
}
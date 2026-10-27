#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Config;
using GameBot.Emulator.Adb;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 164: the key text of a key action reaches the device as the right Android key code. The
/// session input route reports a clear reason for a bad key text. The old input path
/// (<see cref="SessionManager.SendInputsAsync"/>) is the path that the <c>key</c> action of a sequence
/// step uses, so these tests cover that step too.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionManagerKeyInputTests {
  private static InputAction KeyByName(string key) => new("key", new Dictionary<string, object> { ["key"] = key });

  private static InputAction KeyByCode(int keyCode) => new("key", new Dictionary<string, object> { ["keyCode"] = keyCode });

  private static (SessionManager Manager, FakeAdbSessionClient Device, string Id) Build() {
    var device = new FakeAdbSessionClient();
    var mgr = SeamSessionManager.Create(device, null);
    return (mgr, device, SeamSessionManager.CreateDeviceSession(mgr));
  }

  [Fact]
  public async Task SessionInputKeySixReachesTheDeviceAsKeyCode13() {
    var (mgr, device, id) = Build();

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { KeyByName("6") });

    result.Results.Should().ContainSingle().Which.Dispatched.Should().BeTrue();
    device.KeyCodes.Should().Equal(13);
  }

  [Fact]
  public async Task SessionInputNumberPadSixReachesTheDeviceAsKeyCode150() {
    var (mgr, device, id) = Build();

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { KeyByName("NUMPAD_6") });

    result.Results.Should().ContainSingle().Which.Dispatched.Should().BeTrue();
    device.KeyCodes.Should().Equal(150);
  }

  [Fact]
  public async Task SessionInputUnknownKeyReportsTheReasonAndSendsNothing() {
    var (mgr, device, id) = Build();

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { KeyByName("6a") });

    var item = result.Results.Should().ContainSingle().Subject;
    item.Dispatched.Should().BeFalse();
    item.FailureReason.Should().Be("key: unknown key name '6a'");
    device.KeyCodes.Should().BeEmpty();
  }

  [Fact]
  public async Task SessionInputEmptyKeyReportsTheReasonAndSendsNothing() {
    var (mgr, device, id) = Build();

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { KeyByName("  ") });

    var item = result.Results.Should().ContainSingle().Subject;
    item.Dispatched.Should().BeFalse();
    item.FailureReason.Should().Be("key: key name is empty");
    device.KeyCodes.Should().BeEmpty();
  }

  [Fact]
  public async Task SessionInputNumericKeyCodeArgumentStillWorks() {
    var (mgr, device, id) = Build();

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { KeyByCode(13) });

    result.Results.Should().ContainSingle().Which.Dispatched.Should().BeTrue();
    device.KeyCodes.Should().Equal(13);
  }

  [Fact]
  public async Task SessionInputUnknownKeyAlsoReportsTheReasonWithoutADevice() {
    var previous = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      var mgr = new SessionManager(
        Options.Create(new SessionOptions()),
        NullLogger<SessionManager>.Instance,
        NullLogger<AdbClient>.Instance,
        new AppConfig());
      var session = mgr.CreateSession("game-1");

      var result = await mgr.SendInputsWithResultsAsync(session.Id, new[] { KeyByName("6a") });

      var item = result.Results.Should().ContainSingle().Subject;
      item.Dispatched.Should().BeFalse();
      item.FailureReason.Should().Be("key: unknown key name '6a'");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", previous);
    }
  }

  [Fact]
  public async Task SessionInputRawKeyCode85ReachesTheDeviceAsKeyCode85() {
    var (mgr, device, id) = Build();

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { KeyByName("85") });

    result.Results.Should().ContainSingle().Which.Dispatched.Should().BeTrue();
    device.KeyCodes.Should().Equal(85);
  }

  [Theory]
  [InlineData("301", "key: key code 301 is outside 0 to 300")]
  [InlineData(" 301 ", "key: key code 301 is outside 0 to 300")]
  [InlineData("99999999999", "key: key code 99999999999 is outside 0 to 300")]
  public async Task SessionInputOutOfRangeKeyCodeReportsTheReasonAndSendsNothing(string key, string expectedReason) {
    var (mgr, device, id) = Build();

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { KeyByName(key) });

    var item = result.Results.Should().ContainSingle().Subject;
    item.Dispatched.Should().BeFalse();
    item.FailureReason.Should().Be(expectedReason);
    device.KeyCodes.Should().BeEmpty();
  }

  [Fact]
  public async Task OldInputPathSendsRawKeyCode85AsKeyCode85() {
    var (mgr, device, id) = Build();

    var accepted = await mgr.SendInputsAsync(id, new[] { KeyByName("85") });

    accepted.Should().Be(1);
    device.KeyCodes.Should().Equal(85);
  }

  [Fact]
  public async Task OldInputPathAcceptsNothingForAnOutOfRangeKeyCode() {
    var (mgr, device, id) = Build();

    var accepted = await mgr.SendInputsAsync(id, new[] { KeyByName("301") });

    accepted.Should().Be(0);
    device.KeyCodes.Should().BeEmpty();
  }

  [Fact]
  public async Task OldInputPathSendsKeySixAsKeyCode13() {
    var (mgr, device, id) = Build();

    var accepted = await mgr.SendInputsAsync(id, new[] { KeyByName("6") });

    accepted.Should().Be(1);
    device.KeyCodes.Should().Equal(13);
  }

  [Fact]
  public async Task OldInputPathAcceptsNothingForAnUnknownKey() {
    var (mgr, device, id) = Build();

    var accepted = await mgr.SendInputsAsync(id, new[] { KeyByName("6a") });

    accepted.Should().Be(0);
    device.KeyCodes.Should().BeEmpty();
  }
}

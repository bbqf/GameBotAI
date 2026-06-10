using System.Runtime.Versioning;
using FluentAssertions;
using GameBot.Domain.Config;
using GameBot.Emulator.Adb;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Verifies the tap-point jitter normalization in SessionManager.SendInputsAsync.
/// Runs in stub mode (GAMEBOT_USE_ADB=false) — the normalization pass executes before the
/// ADB-mode branch, so no device is required.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionManagerJitterTests : IDisposable {
  private readonly string? _previousUseAdb;

  public SessionManagerJitterTests() {
    _previousUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
  }

  public void Dispose() => Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _previousUseAdb);

  private static SessionManager BuildManager(int jitterRadiusPx) =>
    new(Options.Create(new SessionOptions()),
        NullLogger<SessionManager>.Instance,
        NullLogger<AdbClient>.Instance,
        new AppConfig { TapJitterRadiusPx = jitterRadiusPx });

  private static InputAction TapAction(int x, int y) =>
    new("tap", new Dictionary<string, object> { ["x"] = x, ["y"] = y }, null, null);

  private static InputAction SwipeAction(int x1, int y1, int x2, int y2) =>
    new("swipe", new Dictionary<string, object> { ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 }, null, null);

  [Fact]
  public async Task TapArgsAreJitteredWithinDefaultRadius() {
    var mgr = BuildManager(5);
    var session = mgr.CreateSession("game-1");

    for (var i = 0; i < 100; i++) {
      var action = TapAction(100, 200);
      await mgr.SendInputsAsync(session.Id, new[] { action });
      ((int)action.Args["x"]).Should().BeInRange(95, 105);
      ((int)action.Args["y"]).Should().BeInRange(195, 205);
    }
  }

  [Fact]
  public async Task TapArgsVaryAcrossDispatches() {
    var mgr = BuildManager(5);
    var session = mgr.CreateSession("game-1");

    var observed = new HashSet<(int X, int Y)>();
    for (var i = 0; i < 50; i++) {
      var action = TapAction(100, 200);
      await mgr.SendInputsAsync(session.Id, new[] { action });
      observed.Add(((int)action.Args["x"], (int)action.Args["y"]));
    }
    observed.Count.Should().BeGreaterThan(1);
  }

  [Fact]
  public async Task SwipeArgsAreJitteredWithinDefaultRadius() {
    var mgr = BuildManager(5);
    var session = mgr.CreateSession("game-1");

    for (var i = 0; i < 100; i++) {
      var action = SwipeAction(100, 200, 300, 400);
      await mgr.SendInputsAsync(session.Id, new[] { action });
      ((int)action.Args["x1"]).Should().BeInRange(95, 105);
      ((int)action.Args["y1"]).Should().BeInRange(195, 205);
      ((int)action.Args["x2"]).Should().BeInRange(295, 305);
      ((int)action.Args["y2"]).Should().BeInRange(395, 405);
    }
  }

  [Fact]
  public async Task SwipeEndpointsAreJitteredIndependently() {
    var mgr = BuildManager(5);
    var session = mgr.CreateSession("game-1");

    // If start and end were jittered with the same offset, (x1-100,y1-200) would always equal
    // (x2-300,y2-400). Across many samples at least one pair must differ.
    var sawIndependentOffsets = false;
    for (var i = 0; i < 100 && !sawIndependentOffsets; i++) {
      var action = SwipeAction(100, 200, 300, 400);
      await mgr.SendInputsAsync(session.Id, new[] { action });
      var startOffset = ((int)action.Args["x1"] - 100, (int)action.Args["y1"] - 200);
      var endOffset = ((int)action.Args["x2"] - 300, (int)action.Args["y2"] - 400);
      if (startOffset != endOffset) sawIndependentOffsets = true;
    }
    sawIndependentOffsets.Should().BeTrue();
  }

  [Fact]
  public async Task NearZeroTargetsNeverProduceNegativeCoordinates() {
    var mgr = BuildManager(5);
    var session = mgr.CreateSession("game-1");

    for (var i = 0; i < 100; i++) {
      var action = SwipeAction(2, 0, 1, 3);
      await mgr.SendInputsAsync(session.Id, new[] { action });
      ((int)action.Args["x1"]).Should().BeGreaterThanOrEqualTo(0);
      ((int)action.Args["y1"]).Should().BeGreaterThanOrEqualTo(0);
      ((int)action.Args["x2"]).Should().BeGreaterThanOrEqualTo(0);
      ((int)action.Args["y2"]).Should().BeGreaterThanOrEqualTo(0);
    }
  }

  [Fact]
  public async Task RadiusZeroDisablesJitterEntirely() {
    var mgr = BuildManager(0);
    var session = mgr.CreateSession("game-1");

    for (var i = 0; i < 20; i++) {
      var tap = TapAction(100, 200);
      var swipe = SwipeAction(100, 200, 300, 400);
      await mgr.SendInputsAsync(session.Id, new[] { tap, swipe });
      tap.Args["x"].Should().Be(100);
      tap.Args["y"].Should().Be(200);
      swipe.Args["x1"].Should().Be(100);
      swipe.Args["y1"].Should().Be(200);
      swipe.Args["x2"].Should().Be(300);
      swipe.Args["y2"].Should().Be(400);
    }
  }

  [Fact]
  public async Task ConfiguredRadiusGovernsOffsetRangeNotTheDefault() {
    var mgr = BuildManager(20);
    var session = mgr.CreateSession("game-1");

    var sawBeyondDefaultRange = false;
    for (var i = 0; i < 2000; i++) {
      var action = TapAction(100, 200);
      await mgr.SendInputsAsync(session.Id, new[] { action });
      var x = (int)action.Args["x"];
      var y = (int)action.Args["y"];
      x.Should().BeInRange(80, 120);
      y.Should().BeInRange(180, 220);
      if (Math.Abs(x - 100) > 5 || Math.Abs(y - 200) > 5) sawBeyondDefaultRange = true;
    }
    sawBeyondDefaultRange.Should().BeTrue("with radius 20, offsets beyond the default ±5 range must occur");
  }

  [Fact]
  public async Task KeyActionsAreNotJittered() {
    var mgr = BuildManager(5);
    var session = mgr.CreateSession("game-1");

    var action = new InputAction("key", new Dictionary<string, object> { ["keyCode"] = 4 }, null, null);
    await mgr.SendInputsAsync(session.Id, new[] { action });

    action.Args["keyCode"].Should().Be(4);
  }
}

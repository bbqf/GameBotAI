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
/// Unit tests for bug B-001: <see cref="SessionManager.SendInputsWithResultsAsync"/> must report,
/// per action, whether it dispatched — unlike <see cref="SessionManager.SendInputsAsync"/>'s
/// stub-mode branch, which never validates action args at all and would blindly accept a
/// malformed swipe. Runs in stub mode (GAMEBOT_USE_ADB=false), matching the jitter tests.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionManagerInputResultsTests : IDisposable {
  private readonly string? _previousUseAdb;

  public SessionManagerInputResultsTests() {
    _previousUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
  }

  public void Dispose() => Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _previousUseAdb);

  private static SessionManager BuildManager() =>
    new(Options.Create(new SessionOptions()),
        NullLogger<SessionManager>.Instance,
        NullLogger<AdbClient>.Instance,
        new AppConfig());

  [Fact]
  public async Task MalformedSwipeMissingX2Y2ReportsNotDispatchedWithAReason() {
    var mgr = BuildManager();
    var session = mgr.CreateSession("game-1");
    var swipe = new InputAction("swipe", new Dictionary<string, object> { ["x1"] = 1, ["y1"] = 2 }, null, null);

    var result = await mgr.SendInputsWithResultsAsync(session.Id, new[] { swipe });

    result.SessionFound.Should().BeTrue();
    result.Results.Should().ContainSingle();
    result.Results[0].Dispatched.Should().BeFalse();
    result.Results[0].FailureReason.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task WellFormedTapReportsDispatched() {
    var mgr = BuildManager();
    var session = mgr.CreateSession("game-1");
    var tap = new InputAction("tap", new Dictionary<string, object> { ["x"] = 10, ["y"] = 20 }, null, null);

    var result = await mgr.SendInputsWithResultsAsync(session.Id, new[] { tap });

    result.SessionFound.Should().BeTrue();
    result.Results.Should().ContainSingle();
    result.Results[0].Dispatched.Should().BeTrue();
    result.Results[0].FailureReason.Should().BeNull();
  }

  [Fact]
  public async Task MixedWellFormedAndMalformedActionsReportEachIndependently() {
    var mgr = BuildManager();
    var session = mgr.CreateSession("game-1");
    var tap = new InputAction("tap", new Dictionary<string, object> { ["x"] = 10, ["y"] = 20 }, null, null);
    var badSwipe = new InputAction("swipe", new Dictionary<string, object> { ["x1"] = 1, ["y1"] = 2 }, null, null);

    var result = await mgr.SendInputsWithResultsAsync(session.Id, new[] { tap, badSwipe });

    result.Results.Should().HaveCount(2);
    result.Results[0].Dispatched.Should().BeTrue();
    result.Results[1].Dispatched.Should().BeFalse();
  }

  [Fact]
  public async Task UnknownSessionIdReportsSessionNotFound() {
    var mgr = BuildManager();
    var tap = new InputAction("tap", new Dictionary<string, object> { ["x"] = 10, ["y"] = 20 }, null, null);

    var result = await mgr.SendInputsWithResultsAsync("no-such-session", new[] { tap });

    result.SessionFound.Should().BeFalse();
    result.Results.Should().BeEmpty();
  }
}

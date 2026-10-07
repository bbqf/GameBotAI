using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1307, CA1308

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 129 (FR-015, SC-007): the "go to home screen" step and the "ensure emulator running" step give the
/// same outcomes as before. The list of action types and the list of paths have no new member.
/// </summary>
public sealed class GoHomeAndEnsureEmulatorUnchangedTests {
  private sealed class AcceptingSessions : ISessionManager {
    public int KeyInputs { get; private set; }
    public int ActiveCount => 1;
    public bool CanCreateSession => false;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
    public EmulatorSession? GetSession(string id) => new() { Id = id, GameId = "g", Status = SessionStatus.Running, DeviceSerial = "emulator-5554" };
    public IReadOnlyCollection<EmulatorSession> ListSessions() => Array.Empty<EmulatorSession>();
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) {
      KeyInputs += actions.Count();
      return Task.FromResult(1);
    }
    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default)
      => Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private sealed class FakeEmulatorHandler : IEnsureEmulatorRunningActionHandler {
    public Task<EnsureEmulatorRunningActionResult> ExecuteAsync(EnsureEmulatorRunningArgs args, CancellationToken ct = default) =>
      Task.FromResult(new EnsureEmulatorRunningActionResult(EnsureEmulatorRunningOutcome.AlreadyHealthy));
  }

  private sealed class UnusedGameHandler : IEnsureGameRunningActionHandler {
    public Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<EnsureGameRunningActionResult> RestartAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();
  }

  private static SequenceExecutionService Build(ISessionManager sessions) =>
    new(
      runner: null!, evalSvc: null!, imageVisibleConditionAdapter: null!, imageRepository: null!,
      executionLogService: null!, commandRepository: null!, sequenceRepository: null!, commandExecutor: null!,
      selfRescheduleCoordinator: null!, sessionManager: sessions, ensureGameRunning: new UnusedGameHandler(),
      ensureEmulatorRunning: new FakeEmulatorHandler(), sessionService: null!, ocrOffsetResolver: null!);

  [Fact]
  public async Task GoToHomeScreenStillPressesHomeAndReportsTheSameOutcome() {
    var sessions = new AcceptingSessions();
    var service = Build(sessions);

    var result = await service.DispatchActionAsync(
      new SequenceActionPayload { Type = ActionTypes.GoToHomeScreen }, "seq", null, "s1", ParameterScope.Empty, null, CancellationToken.None);

    result.Outcome.Should().Be("executed");
    result.Message.Should().Be("pressed HOME; device returned to the home screen (game left running)");
    sessions.KeyInputs.Should().Be(1);
  }

  [Fact]
  public async Task GoToHomeScreenIgnoresAForceRestartKey() {
    var sessions = new AcceptingSessions();
    var action = new SequenceActionPayload { Type = ActionTypes.GoToHomeScreen };
    action.Parameters["forceRestart"] = true;

    var result = await Build(sessions).DispatchActionAsync(action, "seq", null, "s1", ParameterScope.Empty, null, CancellationToken.None);

    result.Outcome.Should().Be("executed");
  }

  [Fact]
  public async Task EnsureEmulatorRunningStillReportsTheSameOutcome() {
    var action = new SequenceActionPayload { Type = ActionTypes.EnsureEmulatorRunning };
    action.Parameters["adbSerial"] = "emulator-5554";
    action.Parameters["instanceName"] = "LDPlayer";

    var result = await Build(new AcceptingSessions()).DispatchActionAsync(
      action, "seq", null, "s1", ParameterScope.Empty, null, CancellationToken.None);

    result.Outcome.Should().Be("executed");
    result.Message.Should().Be("emulator already running and responsive");
  }

  [Fact]
  public void TheActionTypeListHasNoNewMember() {
    SequenceActionTypes.All.Should().Equal(
      "tap", "swipe", "key", "command", "connect-to-game", "WaitForImage", "ensure-game-running",
      "go-to-home-screen", "ensure-emulator-running", "reschedule-self", "notify");
  }

  [Fact]
  public async Task TheSwaggerPathListHasNoRestartPath() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var text = await client.GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    using var document = JsonDocument.Parse(text);

    var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
    // The only path with "restart" is the step-through restart of feature 127. Feature 129 adds no path.
    paths.Where(p => p.Contains("restart", StringComparison.OrdinalIgnoreCase)).Should().Equal("/api/step-through/{id}/restart");
    paths.Should().NotContain(p => p.Contains("force-stop", StringComparison.OrdinalIgnoreCase));
  }
}

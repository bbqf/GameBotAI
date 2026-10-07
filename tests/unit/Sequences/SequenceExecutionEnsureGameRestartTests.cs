using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 129: the dispatch of an ensure-game-running action step. With forceRestart true the step
/// calls RestartAsync. Otherwise it calls ExecuteAsync, as before.
/// </summary>
public sealed class SequenceExecutionEnsureGameRestartTests {
  private sealed class FakeHandler : IEnsureGameRunningActionHandler {
    public int ExecuteCalls { get; private set; }
    public int RestartCalls { get; private set; }
    public EnsureGameRunningOutcome ExecuteOutcome { get; set; } = EnsureGameRunningOutcome.GameRunning;
    public EnsureGameRunningOutcome RestartOutcome { get; set; } = EnsureGameRunningOutcome.Restarted;

    public Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default) {
      ExecuteCalls++;
      return Task.FromResult(new EnsureGameRunningActionResult(ExecuteOutcome));
    }

    public Task<EnsureGameRunningActionResult> RestartAsync(string sessionId, CancellationToken ct = default) {
      RestartCalls++;
      return Task.FromResult(new EnsureGameRunningActionResult(RestartOutcome));
    }
  }

  // The dispatch of this action type uses the handler and the session rule only. All other
  // dependencies stay null. An explicit session id never reads the session manager.
  private static SequenceExecutionService BuildService(IEnsureGameRunningActionHandler handler) =>
    new(
      runner: null!,
      evalSvc: null!,
      imageVisibleConditionAdapter: null!,
      imageRepository: null!,
      executionLogService: null!,
      commandRepository: null!,
      sequenceRepository: null!,
      commandExecutor: null!,
      selfRescheduleCoordinator: null!,
      sessionManager: null!,
      ensureGameRunning: handler,
      ensureEmulatorRunning: null!,
      sessionService: null!,
      ocrOffsetResolver: null!);

  private static SequenceActionPayload Action(bool hasKey, object? value = null) {
    var action = new SequenceActionPayload { Type = ActionTypes.EnsureGameRunning };
    if (hasKey) action.Parameters["forceRestart"] = value;
    return action;
  }

  private static Task<GameBot.Domain.Services.ActionDispatchResult> Dispatch(SequenceExecutionService service, SequenceActionPayload action) =>
    service.DispatchActionAsync(action, "seq-1", null, "s1", ParameterScope.Empty, null, CancellationToken.None);

  [Fact]
  public async Task ForceRestartTrueCallsRestartAndGivesTheOutcomeRestarted() {
    var handler = new FakeHandler();

    var result = await Dispatch(BuildService(handler), Action(true, true));

    handler.RestartCalls.Should().Be(1);
    handler.ExecuteCalls.Should().Be(0);
    result.Outcome.Should().Be("restarted");
  }

  [Fact]
  public async Task AJsonElementTrueAfterAFileReadCallsRestart() {
    var handler = new FakeHandler();
    var element = JsonDocument.Parse("true").RootElement.Clone();

    var result = await Dispatch(BuildService(handler), Action(true, element));

    handler.RestartCalls.Should().Be(1);
    result.Outcome.Should().Be("restarted");
  }

  [Theory]
  [InlineData(true, false)]
  [InlineData(false, false)]
  public async Task ForceRestartFalseAndAnAbsentKeyCallExecute(bool hasKey, bool value) {
    var handler = new FakeHandler();

    var result = await Dispatch(BuildService(handler), Action(hasKey, value));

    handler.ExecuteCalls.Should().Be(1);
    handler.RestartCalls.Should().Be(0);
    result.Outcome.Should().Be("executed");
  }

  public static IEnumerable<object?[]> NonBooleans() {
    yield return new object?[] { "true" };
    yield return new object?[] { 1 };
    yield return new object?[] { null };
    yield return new object?[] { new Dictionary<string, object>() };
    yield return new object?[] { "{{x}}" };
  }

  [Theory]
  [MemberData(nameof(NonBooleans))]
  public async Task ANonBooleanValueAtRunTimeFailsTheStepWithAMessageAndNoCall(object? bad) {
    var handler = new FakeHandler();

    var act = async () => await Dispatch(BuildService(handler), Action(true, bad));

    var result = await act.Should().NotThrowAsync();
    result.Subject.Outcome.Should().Be("failed");
    result.Subject.Message.Should().Contain("forceRestart");
    handler.RestartCalls.Should().Be(0);
    handler.ExecuteCalls.Should().Be(0);
  }

  [Theory]
  [InlineData("RestartNoDevice", "restart_no_device")]
  [InlineData("RestartStopFailed", "restart_stop_failed")]
  [InlineData("RestartStartFailed", "restart_start_failed")]
  [InlineData("RestartForegroundTimeout", "restart_foreground_timeout")]
  [InlineData("NoQueueContext", "no_queue_context")]
  [InlineData("NoLinkedGame", "no_linked_game")]
  [InlineData("NoPackageName", "no_package_name")]
  [InlineData("PlatformUnsupported", "platform_unsupported")]
  public async Task EachFailureOfTheRestartGivesAFailedResultWithItsReasonCode(string outcomeName, string reason) {
    var outcome = System.Enum.Parse<EnsureGameRunningOutcome>(outcomeName);
    var handler = new FakeHandler { RestartOutcome = outcome };

    var result = await Dispatch(BuildService(handler), Action(true, true));

    result.Outcome.Should().Be("failed");
    result.Outcome.Should().NotBe("restarted");
    result.Message.Should().Contain(reason);
  }
}

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Service.Services.EnsureGameRunning;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Commands;

// Feature 129: a command step of type EnsureGameRunning with ForceRestart true. This part of the class
// shares the fakes of CommandExecutorEnsureGameRunningTests.
public sealed partial class CommandExecutorEnsureGameRunningTests {
  private static Command RestartCommand(bool withReadiness = false, string id = "cmd1") => new() {
    Id = id,
    Name = "Restart",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() {
        Type = CommandStepType.EnsureGameRunning,
        TargetId = string.Empty,
        Order = 1,
        EnsureGameRunning = new EnsureGameRunningConfig {
          ForceRestart = true,
          ReadinessImage = withReadiness ? new DetectionTarget("city-sentinel") : null,
          ReadinessTimeoutMs = 1000
        }
      }
    }
  };

  [Fact]
  public async Task ForceRestartCallsRestartAndNotExecuteAndGivesExecutedRestarted() {
    var cmds = new FakeCommandRepository();
    cmds.Seed(RestartCommand());
    var handler = new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning));
    var executor = BuildExecutor(handler, cmds, new FakeSessionManager(RunningSession()));

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    handler.RestartCalls.Should().Be(1);
    handler.ExecuteCalls.Should().Be(0);
    result.Accepted.Should().Be(1);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("executed");
    outcome.Reason.Should().Be("restarted");
  }

  [Fact]
  public async Task TheReadinessWaitRunsAfterAGoodRestart() {
    var cmds = new FakeCommandRepository();
    cmds.Seed(RestartCommand(withReadiness: true));
    var handler = new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning));
    var probe = new StubReadinessProbe(ready: true);
    var executor = BuildExecutor(handler, cmds, new FakeSessionManager(RunningSession()), probe);

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    handler.RestartCalls.Should().Be(1);
    probe.CallCount.Should().Be(1);
    result.StepOutcomes.Should().ContainSingle().Which.Reason.Should().Be("game_ready");
  }

  [Theory]
  [InlineData("RestartNoDevice", "restart_no_device")]
  [InlineData("RestartStopFailed", "restart_stop_failed")]
  [InlineData("RestartStartFailed", "restart_start_failed")]
  [InlineData("RestartForegroundTimeout", "restart_foreground_timeout")]
  public async Task EachRestartFailureGivesAFailedOutcomeWithItsReasonCode(string outcomeName, string reason) {
    var outcome = System.Enum.Parse<EnsureGameRunningOutcome>(outcomeName);
    var cmds = new FakeCommandRepository();
    cmds.Seed(RestartCommand());
    var handler = new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning)) {
      RestartResult = new EnsureGameRunningActionResult(outcome)
    };
    var executor = BuildExecutor(handler, cmds, new FakeSessionManager(RunningSession()));

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    result.Accepted.Should().Be(0);
    var step = result.StepOutcomes.Should().ContainSingle().Subject;
    step.Status.Should().Be(reason);
    step.Reason.Should().Be(reason);
    step.Reason.Should().NotBe("restarted");
  }

  [Theory]
  [InlineData("RestartStopFailed", "restart_stop_failed")]
  [InlineData("RestartForegroundTimeout", "restart_foreground_timeout")]
  public async Task ARestartFailureSkipsTheReadinessWait(string outcomeName, string reason) {
    var outcome = System.Enum.Parse<EnsureGameRunningOutcome>(outcomeName);
    var cmds = new FakeCommandRepository();
    cmds.Seed(RestartCommand(withReadiness: true));
    var handler = new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning)) {
      RestartResult = new EnsureGameRunningActionResult(outcome)
    };
    var probe = new StubReadinessProbe(ready: true);
    var executor = BuildExecutor(handler, cmds, new FakeSessionManager(RunningSession()), probe);

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    probe.CallCount.Should().Be(0);
    result.StepOutcomes.Should().ContainSingle().Which.Reason.Should().Be(reason);
  }

  [Fact]
  public async Task AStepWithoutForceRestartStillCallsExecute() {
    var cmds = new FakeCommandRepository();
    cmds.Seed(EnsureRunningCommand());
    var handler = new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning));
    var executor = BuildExecutor(handler, cmds, new FakeSessionManager(RunningSession()));

    await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    handler.ExecuteCalls.Should().Be(1);
    handler.RestartCalls.Should().Be(0);
  }

  [Fact]
  public void TheJsonOfAStepWithForceRestartFalseHasNoForceRestartKey() {
    var json = JsonSerializer.Serialize(new EnsureGameRunningConfig { ForceRestart = false });

    json.Should().NotContain("ForceRestart", "a plain step must keep the stored JSON that it had before feature 129");
  }

  [Fact]
  public void TheJsonOfAStepWithForceRestartTrueKeepsTheValue() {
    var json = JsonSerializer.Serialize(new EnsureGameRunningConfig { ForceRestart = true });
    var back = JsonSerializer.Deserialize<EnsureGameRunningConfig>(json);

    back!.ForceRestart.Should().BeTrue();
  }

  [Fact]
  public async Task AnOldStoredStepWithoutTheKeyReadsAsFalseAndCallsExecute() {
    var old = JsonSerializer.Deserialize<EnsureGameRunningConfig>("{\"ReadinessTimeoutMs\":90000}");
    old!.ForceRestart.Should().BeFalse();

    var cmds = new FakeCommandRepository();
    cmds.Seed(new Command {
      Id = "cmd1",
      Name = "Old",
      Steps = new Collection<CommandStep> {
        new() { Type = CommandStepType.EnsureGameRunning, TargetId = string.Empty, Order = 1, EnsureGameRunning = old }
      }
    });
    var handler = new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning));
    var executor = BuildExecutor(handler, cmds, new FakeSessionManager(RunningSession()));

    await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    handler.ExecuteCalls.Should().Be(1);
    handler.RestartCalls.Should().Be(0);
  }
}

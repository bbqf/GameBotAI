using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 174: the runner treats a WaitForStill step as a service-level action and records its outcome.</summary>
public sealed class WaitForStillRunnerTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static CommandSequence Build(string? expectedState) {
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "wait",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = ActionTypes.WaitForStill }
      },
      new SequenceStep {
        Order = 1,
        StepId = "gate",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 1 } },
        Condition = expectedState is null ? null : new CommandOutcomeStepCondition { StepRef = "wait", ExpectedState = expectedState }
      }
    });
    return sequence;
  }

  private static async Task<(SequenceExecutionResult Result, bool GateRan)> RunAsync(string waitOutcome, string? expectedState, bool dryRun = false) {
    var gateRan = false;
    var runner = new SequenceRunner(new StubRepo(Build(expectedState)));
    var result = await runner.ExecuteAsync(
      "seq",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (action, _) => {
        if (action.Type == ActionTypes.WaitForStill) {
          return Task.FromResult(new ActionDispatchResult(waitOutcome, "wait message"));
        }

        gateRan = true;
        return Task.FromResult(new ActionDispatchResult("executed", "tap"));
      },
      dryRun: dryRun,
      ct: CancellationToken.None);
    return (result, gateRan);
  }

  [Theory]
  [InlineData("still")]
  [InlineData("timeout_elapsed")]
  public async Task TheStepSucceedsAndRecordsItsOutcome(string outcome) {
    var (result, gateRan) = await RunAsync(outcome, null);

    result.Status.Should().Be("Succeeded");
    result.Steps.Should().Contain(s => s.ActionOutcome == outcome && s.Message == "wait message" && s.Status == "Succeeded");
    gateRan.Should().BeTrue();
  }

  [Theory]
  [InlineData("still", "still", true)]
  [InlineData("still", "success", true)]
  [InlineData("still", "timeout_elapsed", false)]
  [InlineData("timeout_elapsed", "timeout_elapsed", true)]
  [InlineData("timeout_elapsed", "still", false)]
  [InlineData("timeout_elapsed", "success", false)]
  public async Task AConditionOnTheStepOutcomeBranches(string outcome, string expected, bool gateRuns) {
    var (_, gateRan) = await RunAsync(outcome, expected);

    gateRan.Should().Be(gateRuns);
  }

  [Fact]
  public async Task AFailedResultFailsTheStepAndStopsTheSequence() {
    var (result, gateRan) = await RunAsync("failed", null);

    result.Status.Should().NotBe("Succeeded");
    result.Steps.Should().Contain(s => s.ActionOutcome == "failed" && s.Status == "Failed");
    gateRan.Should().BeFalse();
  }

  [Fact]
  public async Task ADryRunSkipsTheStep() {
    var dispatched = false;
    var runner = new SequenceRunner(new StubRepo(Build(null)));

    var result = await runner.ExecuteAsync(
      "seq",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (action, _) => {
        dispatched |= action.Type == ActionTypes.WaitForStill;
        return Task.FromResult(new ActionDispatchResult("still", "x"));
      },
      dryRun: true,
      ct: CancellationToken.None);

    dispatched.Should().BeFalse();
    result.Steps.Should().Contain(s => s.ActionOutcome == "skipped_dry_run");
  }

  [Fact]
  public void TheTypeIsServiceLevel() {
    SequenceRunner.IsServiceLevelAction(new SequenceActionPayload { Type = "waitforstill" }).Should().BeTrue();
    SequenceRunner.IsWaitForStillAction(new SequenceActionPayload { Type = ActionTypes.WaitForStill }).Should().BeTrue();
    SequenceRunner.IsWaitForStillAction(new SequenceActionPayload { Type = ActionTypes.WaitForImage }).Should().BeFalse();
    SequenceRunner.IsWaitForStillAction(null).Should().BeFalse();
  }
}

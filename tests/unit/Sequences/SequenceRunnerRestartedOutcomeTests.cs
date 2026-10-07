using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 129: the recorded state "restarted". A condition on restarted matches only a restarted
/// step. A condition on success matches a restarted step too, so existing sequences keep working.
/// </summary>
public sealed class SequenceRunnerRestartedOutcomeTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceStep GameStep() => new() {
    Order = 0,
    StepId = "game",
    CommandId = "game",
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = ActionTypes.EnsureGameRunning }
  };

  private static SequenceStep Gate(string expectedState) => new() {
    Order = 1,
    StepId = "gate",
    CommandId = "gate",
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 1 } },
    Condition = new CommandOutcomeStepCondition { StepRef = "game", ExpectedState = expectedState }
  };

  private static async Task<bool> GateRanAsync(string gameOutcome, string expectedState) {
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(new[] { GameStep(), Gate(expectedState) });
    var gateRan = false;
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "seq",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (action, _) => {
        if (action.Type == ActionTypes.EnsureGameRunning) {
          return Task.FromResult(new ActionDispatchResult(gameOutcome, "game step"));
        }
        gateRan = true;
        return Task.FromResult(new ActionDispatchResult("executed", "tap"));
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    return gateRan;
  }

  [Theory]
  [InlineData("restarted", "restarted", true)]
  [InlineData("restarted", "success", true)]
  [InlineData("restarted", "failed", false)]
  [InlineData("executed", "success", true)]
  [InlineData("executed", "restarted", false)]
  public async Task AConditionMatchesTheRecordedStateOfAnActionStep(string dispatchOutcome, string expectedState, bool gateRuns) {
    (await GateRanAsync(dispatchOutcome, expectedState)).Should().Be(gateRuns);
  }

  [Fact]
  public async Task ACommandStepGivesSuccessToASequenceCondition() {
    // A command step is one unit for the sequence runner. It has no restarted state.
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "game", CommandId = "restart-cmd", StepType = SequenceStepType.Command },
      Gate("success")
    });
    var gateRan = false;
    var runner = new SequenceRunner(new StubRepo(sequence));

    await runner.ExecuteAsync(
      "seq",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (_, _) => {
        gateRan = true;
        return Task.FromResult(new ActionDispatchResult("executed", "tap"));
      },
      ct: CancellationToken.None);

    gateRan.Should().BeTrue();
  }

  [Theory]
  [InlineData("restarted", true)]
  [InlineData("Restarted", true)]
  [InlineData("success", false)]
  [InlineData("failed", false)]
  public void StepOutcomeStatesMatchesRestartedOnlyAgainstRestarted(string actual, bool matchesRestarted) {
    StepOutcomeStates.Matches(actual, "restarted").Should().Be(matchesRestarted);
  }

  [Theory]
  [InlineData("restarted", "success", true)]
  [InlineData("success", "success", true)]
  [InlineData("failed", "success", false)]
  [InlineData("skipped", "success", false)]
  [InlineData("break", "break", true)]
  public void StepOutcomeStatesMatchesSuccessAgainstRestartedToo(string actual, string expected, bool matches) {
    StepOutcomeStates.Matches(actual, expected).Should().Be(matches);
  }

  [Fact]
  public void TheStateListHasSixMembersAndRestartedIsAllowed() {
    StepOutcomeStates.All.Should().Equal("success", "failed", "skipped", "break", "no_break", "restarted");
    StepOutcomeStates.IsAllowed("RESTARTED").Should().BeTrue();
    StepOutcomeStates.IsAllowed("maybe").Should().BeFalse();
    StepOutcomeStates.AllText.Should().Be("success|failed|skipped|break|no_break|restarted");
  }

  [Fact]
  public void ARestartActionIsDetectedOnlyWithForceRestartTrue() {
    var restart = new SequenceActionPayload { Type = ActionTypes.EnsureGameRunning, Parameters = { ["forceRestart"] = true } };
    var plain = new SequenceActionPayload { Type = ActionTypes.EnsureGameRunning };
    var bad = new SequenceActionPayload { Type = ActionTypes.EnsureGameRunning, Parameters = { ["forceRestart"] = "yes" } };
    var other = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["forceRestart"] = true } };

    SequenceRunner.IsForceRestartAction(restart).Should().BeTrue();
    SequenceRunner.IsForceRestartAction(plain).Should().BeFalse();
    SequenceRunner.IsForceRestartAction(bad).Should().BeFalse();
    SequenceRunner.IsForceRestartAction(other).Should().BeFalse();
    SequenceRunner.IsForceRestartAction(null).Should().BeFalse();
  }
}

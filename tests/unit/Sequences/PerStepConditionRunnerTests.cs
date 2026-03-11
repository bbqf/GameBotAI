using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

public sealed class PerStepConditionRunnerTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;

    public StubRepo(CommandSequence sequence) {
      _sequence = sequence;
    }

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  [Fact]
  public async Task ExecutesAndSkipsStepsBasedOnPerStepConditionResult() {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(BuildSequence()));

    var result = await runner.ExecuteAsync(
      "per-step-runner",
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-open-menu");
  }

  [Fact]
  public async Task CommandOutcomeConditionUsesPriorStepOutcome() {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(BuildSequence()));

    var result = await runner.ExecuteAsync(
      "per-step-runner",
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(true),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-go-home");
  }

  [Fact]
  public async Task ConditionEvaluationErrorStopsSequence() {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(BuildSequence()));

    var result = await runner.ExecuteAsync(
      "per-step-runner",
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => throw new InvalidOperationException("eval failed"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    executed.Should().BeEmpty();
  }

  private static CommandSequence BuildSequence() {
    var sequence = new CommandSequence {
      Id = "per-step-runner",
      Name = "Per Step Runner"
    };

    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "go-home",
        CommandId = "cmd-go-home",
        Action = new SequenceActionPayload {
          Type = "command",
          Parameters = { ["commandId"] = "cmd-go-home" }
        },
        Condition = new ImageVisibleStepCondition {
          ImageId = "map-image",
          MinSimilarity = 0.80
        }
      },
      new SequenceStep {
        Order = 1,
        StepId = "open-menu",
        CommandId = "cmd-open-menu",
        Action = new SequenceActionPayload {
          Type = "command",
          Parameters = { ["commandId"] = "cmd-open-menu" }
        },
        Condition = new CommandOutcomeStepCondition {
          StepRef = "go-home",
          ExpectedState = "skipped"
        }
      }
    });

    return sequence;
  }
}

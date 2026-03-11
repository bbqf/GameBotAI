using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class PerStepCommandOutcomeExecutionIntegrationTests {
  [Fact]
  public async Task CommandOutcomeConditionReferencesPriorStepResult() {
    var sequence = new CommandSequence {
      Id = "per-step-command-outcome",
      Name = "Per Step Command Outcome"
    };

    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "go-home",
        CommandId = "cmd-go-home",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-go-home" } },
        Condition = new ImageVisibleStepCondition { ImageId = "map-image", MinSimilarity = 0.80 }
      },
      new SequenceStep {
        Order = 1,
        StepId = "open-menu",
        CommandId = "cmd-open-menu",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-open-menu" } },
        Condition = new CommandOutcomeStepCondition { StepRef = "go-home", ExpectedState = "skipped" }
      }
    });

    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-open-menu");
  }

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
}

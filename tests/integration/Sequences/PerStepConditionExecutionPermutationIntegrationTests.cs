using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class PerStepConditionExecutionPermutationIntegrationTests {
  [Theory]
  [InlineData(true, true, 2)]
  [InlineData(true, false, 1)]
  [InlineData(false, true, 1)]
  [InlineData(false, false, 0)]
  public async Task ExecutesExpectedCountAcrossImageVisibilityPermutations(bool mapVisible, bool bagVisible, int expectedExecuted) {
    var sequence = BuildImageSequence();
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (condition, _) => Task.FromResult(condition.TargetId == "map-image" ? mapVisible : bagVisible),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(expectedExecuted);
  }

  private static CommandSequence BuildImageSequence() {
    var sequence = new CommandSequence {
      Id = "per-step-permutations",
      Name = "Per Step Permutations"
    };

    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "map-step",
        CommandId = "cmd-map",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-map" } },
        Condition = new ImageVisibleStepCondition { ImageId = "map-image", MinSimilarity = 0.85 }
      },
      new SequenceStep {
        Order = 1,
        StepId = "bag-step",
        CommandId = "cmd-bag",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-bag" } },
        Condition = new ImageVisibleStepCondition { ImageId = "bag-image", MinSimilarity = 0.85 }
      }
    });

    return sequence;
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

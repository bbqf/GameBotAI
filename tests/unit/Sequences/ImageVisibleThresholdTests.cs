using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

public sealed class ImageVisibleThresholdTests {
  [Fact]
  public async Task MissingThresholdUsesDefaultFromEvaluator() {
    var runner = new SequenceRunner(new StubRepo(BuildFlowWithoutThreshold()));
    double? observedThreshold = null;

    var result = await runner.ExecuteAsync(
      "threshold-seq",
      _ => Task.CompletedTask,
      conditionEvaluator: (condition, _) => {
        observedThreshold = condition.ConfidenceThreshold;
        return Task.FromResult(true);
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    observedThreshold.Should().BeNull();
  }

  [Fact]
  public async Task ExplicitThresholdIsForwardedToEvaluator() {
    var runner = new SequenceRunner(new StubRepo(BuildFlowWithThreshold(0.93)));
    double? observedThreshold = null;

    var result = await runner.ExecuteAsync(
      "threshold-seq",
      _ => Task.CompletedTask,
      conditionEvaluator: (condition, _) => {
        observedThreshold = condition.ConfidenceThreshold;
        return Task.FromResult(true);
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    observedThreshold.Should().Be(0.93);
  }

  private static CommandSequence BuildFlowWithoutThreshold() => BuildFlowWithThreshold(null);

  private static CommandSequence BuildFlowWithThreshold(double? threshold) {
    var sequence = new CommandSequence {
      Id = "threshold-seq",
      Name = "threshold",
      EntryStepId = "cond"
    };

    sequence.SetFlowSteps(new[] {
      new FlowStep {
        StepId = "cond",
        Label = "Conditional",
        StepType = FlowStepType.Condition,
        Condition = new ConditionExpression {
          NodeType = ConditionNodeType.Operand,
          Operand = new ConditionOperand {
            OperandType = ConditionOperandType.ImageDetection,
            TargetRef = "img-a",
            ExpectedState = "present",
            Threshold = threshold
          }
        }
      },
      new FlowStep { StepId = "done", Label = "Done", StepType = FlowStepType.Terminal }
    });

    sequence.SetFlowLinks(new[] {
      new BranchLink { LinkId = "t", SourceStepId = "cond", TargetStepId = "done", BranchType = BranchType.True },
      new BranchLink { LinkId = "f", SourceStepId = "cond", TargetStepId = "done", BranchType = BranchType.False }
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

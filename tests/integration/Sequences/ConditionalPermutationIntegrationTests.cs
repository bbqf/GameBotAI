using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class ConditionalPermutationIntegrationTests {
  [Theory]
  [InlineData(true, true, new[] { "cmd-a", "cmd-b", "cmd-final" })]
  [InlineData(true, false, new[] { "cmd-a", "cmd-final" })]
  [InlineData(false, true, new[] { "cmd-b", "cmd-final" })]
  [InlineData(false, false, new[] { "cmd-final" })]
  public async Task ExecuteMixedConditionalSequenceReturnsExpectedCommands(
    bool imageAVisible,
    bool imageBVisible,
    string[] expectedCommands) {
    var sequence = BuildFlow();
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (condition, _) => {
        if (condition.TargetId == "image-a") {
          return Task.FromResult(imageAVisible);
        }

        if (condition.TargetId == "image-b") {
          return Task.FromResult(imageBVisible);
        }

        return Task.FromResult(false);
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal(expectedCommands);
  }

  private static CommandSequence BuildFlow() {
    var sequence = new CommandSequence {
      Id = "cond-us2-permutations",
      Name = "conditional permutations",
      EntryStepId = "cond-a"
    };

    sequence.SetFlowSteps(new[] {
      new FlowStep {
        StepId = "cond-a",
        Label = "Condition A",
        StepType = FlowStepType.Condition,
        Condition = new ConditionExpression {
          NodeType = ConditionNodeType.Operand,
          Operand = new ConditionOperand {
            OperandType = ConditionOperandType.ImageDetection,
            TargetRef = "image-a",
            ExpectedState = "present",
            Threshold = 0.8
          }
        }
      },
      new FlowStep { StepId = "cmd-a", Label = "Command A", StepType = FlowStepType.Command, PayloadRef = "cmd-a" },
      new FlowStep {
        StepId = "cond-b",
        Label = "Condition B",
        StepType = FlowStepType.Condition,
        Condition = new ConditionExpression {
          NodeType = ConditionNodeType.Operand,
          Operand = new ConditionOperand {
            OperandType = ConditionOperandType.ImageDetection,
            TargetRef = "image-b",
            ExpectedState = "present",
            Threshold = 0.8
          }
        }
      },
      new FlowStep { StepId = "cmd-b", Label = "Command B", StepType = FlowStepType.Command, PayloadRef = "cmd-b" },
      new FlowStep { StepId = "cmd-final", Label = "Command Final", StepType = FlowStepType.Command, PayloadRef = "cmd-final" },
      new FlowStep { StepId = "end", Label = "End", StepType = FlowStepType.Terminal }
    });

    sequence.SetFlowLinks(new[] {
      new BranchLink { LinkId = "a-true", SourceStepId = "cond-a", TargetStepId = "cmd-a", BranchType = BranchType.True },
      new BranchLink { LinkId = "a-false", SourceStepId = "cond-a", TargetStepId = "cond-b", BranchType = BranchType.False },
      new BranchLink { LinkId = "a-next", SourceStepId = "cmd-a", TargetStepId = "cond-b", BranchType = BranchType.Next },
      new BranchLink { LinkId = "b-true", SourceStepId = "cond-b", TargetStepId = "cmd-b", BranchType = BranchType.True },
      new BranchLink { LinkId = "b-false", SourceStepId = "cond-b", TargetStepId = "cmd-final", BranchType = BranchType.False },
      new BranchLink { LinkId = "b-next", SourceStepId = "cmd-b", TargetStepId = "cmd-final", BranchType = BranchType.Next },
      new BranchLink { LinkId = "final-next", SourceStepId = "cmd-final", TargetStepId = "end", BranchType = BranchType.Next }
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

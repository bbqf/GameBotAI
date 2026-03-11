using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class ConditionalStepExecutionIntegrationTests {
  [Theory]
  [InlineData(true, "cmd-true")]
  [InlineData(false, "cmd-false")]
  public async Task ExecutesExpectedBranchForConditionResult(bool conditionResult, string expectedCommand) {
    var sequence = BuildFlow();
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(conditionResult),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be(expectedCommand);
  }

  [Fact]
  public async Task StopsExecutionWhenConditionEvaluationThrows() {
    var sequence = BuildFlow();
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => throw new System.InvalidOperationException("bad eval"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    executed.Should().BeEmpty();
  }

  private static CommandSequence BuildFlow() {
    var sequence = new CommandSequence {
      Id = "cond-int-us1",
      Name = "conditional integration",
      EntryStepId = "condition"
    };

    sequence.SetFlowSteps(new[] {
      new FlowStep {
        StepId = "condition",
        Label = "Condition",
        StepType = FlowStepType.Condition,
        Condition = new ConditionExpression {
          NodeType = ConditionNodeType.Operand,
          Operand = new ConditionOperand {
            OperandType = ConditionOperandType.ImageDetection,
            TargetRef = "image-1",
            ExpectedState = "present",
            Threshold = 0.85
          }
        }
      },
      new FlowStep { StepId = "true-step", Label = "True", StepType = FlowStepType.Command, PayloadRef = "cmd-true" },
      new FlowStep { StepId = "false-step", Label = "False", StepType = FlowStepType.Command, PayloadRef = "cmd-false" },
      new FlowStep { StepId = "end", Label = "End", StepType = FlowStepType.Terminal }
    });

    sequence.SetFlowLinks(new[] {
      new BranchLink { LinkId = "t", SourceStepId = "condition", TargetStepId = "true-step", BranchType = BranchType.True },
      new BranchLink { LinkId = "f", SourceStepId = "condition", TargetStepId = "false-step", BranchType = BranchType.False },
      new BranchLink { LinkId = "n1", SourceStepId = "true-step", TargetStepId = "end", BranchType = BranchType.Next },
      new BranchLink { LinkId = "n2", SourceStepId = "false-step", TargetStepId = "end", BranchType = BranchType.Next }
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

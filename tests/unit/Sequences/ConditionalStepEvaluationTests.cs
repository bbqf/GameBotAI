using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

public sealed class ConditionalStepEvaluationTests {
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

  private static CommandSequence BuildFlow() {
    var sequence = new CommandSequence {
      Id = "cond-us1-seq",
      Name = "US1",
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
            Threshold = 0.80
          }
        }
      },
      new FlowStep { StepId = "run", Label = "Run", StepType = FlowStepType.Command, PayloadRef = "cmd-run" },
      new FlowStep { StepId = "skip", Label = "Skip", StepType = FlowStepType.Command, PayloadRef = "cmd-skip" },
      new FlowStep { StepId = "end", Label = "End", StepType = FlowStepType.Terminal }
    });

    sequence.SetFlowLinks(new[] {
      new BranchLink { LinkId = "t", SourceStepId = "cond", TargetStepId = "run", BranchType = BranchType.True },
      new BranchLink { LinkId = "f", SourceStepId = "cond", TargetStepId = "skip", BranchType = BranchType.False },
      new BranchLink { LinkId = "n1", SourceStepId = "run", TargetStepId = "end", BranchType = BranchType.Next },
      new BranchLink { LinkId = "n2", SourceStepId = "skip", TargetStepId = "end", BranchType = BranchType.Next }
    });

    return sequence;
  }

  [Fact]
  public async Task ConditionalTrueRoutesToTrueBranch() {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(BuildFlow()));

    var result = await runner.ExecuteAsync(
      "cond-us1-seq",
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(true),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-run");
  }

  [Fact]
  public async Task ConditionalFalseRoutesToFalseBranch() {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(BuildFlow()));

    var result = await runner.ExecuteAsync(
      "cond-us1-seq",
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-skip");
  }

  [Fact]
  public async Task ConditionalEvaluationErrorFailsStop() {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(BuildFlow()));

    var result = await runner.ExecuteAsync(
      "cond-us1-seq",
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => throw new InvalidOperationException("evaluation failed"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    executed.Should().BeEmpty();
  }
}

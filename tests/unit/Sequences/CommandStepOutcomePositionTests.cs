using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 167: the condition works in each slot where commandOutcome works.</summary>
public sealed class CommandStepOutcomePositionTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceStep CommandStep(string id, SequenceStepCondition? condition = null) => new() {
    StepId = id,
    CommandId = id,
    StepType = SequenceStepType.Action,
    Condition = condition
  };

  private static CommandStepOutcomeStepCondition Leaf(string expected = "executed") => new() {
    StepRef = "send-1",
    CommandStepOrder = 12,
    ExpectedState = expected
  };

  private static async Task<(SequenceExecutionResult Result, List<string> Ran)> RunAsync(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    sequence.SetSteps(steps.Select((s, i) => { s.Order = i; return s; }).ToArray());
    sequence.InterStepDelayRangeMs = new DelayRangeMs { Min = 0, Max = 0 };
    var runner = new SequenceRunner(new StubRepo(sequence));
    var ran = new List<string>();
    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
      commandDispatcher: (id, _) => {
        ran.Add(id);
        return Task.FromResult(id == "send-1"
          ? new CommandDispatchOutcome(true, null, StepStates: new Dictionary<int, string> { [12] = "executed" })
          : CommandDispatchOutcome.Executed);
      },
      ct: CancellationToken.None);
    return (res, ran);
  }

  [Fact]
  public async Task AStepConditionRunsTheStepWhenTrueAndSkipsItWhenFalse() {
    var (_, ran) = await RunAsync(CommandStep("send-1"), CommandStep("yes", Leaf()), CommandStep("no", Leaf("completed_timeout")));

    ran.Should().Contain("yes").And.NotContain("no");
  }

  [Fact]
  public async Task AllAnyAndNoneChildrenWork() {
    var all = new AllStepCondition { Children = new SequenceStepCondition[] { Leaf(), Leaf() } };
    var any = new AnyStepCondition { Children = new SequenceStepCondition[] { Leaf("image_detected"), Leaf() } };
    var none = new NoneStepCondition { Children = new SequenceStepCondition[] { Leaf("image_detected") } };
    var notAll = new AllStepCondition { Children = new SequenceStepCondition[] { Leaf(), Leaf("image_detected") } };

    var (_, ran) = await RunAsync(
      CommandStep("send-1"), CommandStep("all", all), CommandStep("any", any), CommandStep("none", none), CommandStep("notAll", notAll));

    ran.Should().Contain(new[] { "all", "any", "none" }).And.NotContain("notAll");
  }

  [Fact]
  public async Task AnIfStepChoosesTheBranch() {
    var thenStep = CommandStep("then");
    var elseStep = CommandStep("else");
    var trueIf = new SequenceStep {
      StepId = "if-true", StepType = SequenceStepType.If, If = new IfConfig { Condition = Leaf() },
      Body = new[] { thenStep }, ElseBody = new[] { elseStep }
    };
    var thenStep2 = CommandStep("then2");
    var elseStep2 = CommandStep("else2");
    var falseIf = new SequenceStep {
      StepId = "if-false", StepType = SequenceStepType.If, If = new IfConfig { Condition = Leaf("completed_timeout") },
      Body = new[] { thenStep2 }, ElseBody = new[] { elseStep2 }
    };

    var (_, ran) = await RunAsync(CommandStep("send-1"), trueIf, falseIf);

    ran.Should().Contain(new[] { "then", "else2" }).And.NotContain(new[] { "else", "then2" });
  }

  [Fact]
  public async Task AWhileLoopConditionEndsTheLoop() {
    var calls = 0;
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    var loop = new SequenceStep {
      StepId = "loop", StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = new CommandStepOutcomeStepCondition { StepRef = "send-1", CommandStepOrder = 12, ExpectedState = "executed" }, MaxIterations = 10 },
      Body = new[] { CommandStep("body") }
    };
    var steps = new[] { CommandStep("send-1"), loop };
    sequence.SetSteps(steps.Select((s, i) => { s.Order = i; return s; }).ToArray());
    sequence.InterStepDelayRangeMs = new DelayRangeMs { Min = 0, Max = 0 };
    var runner = new SequenceRunner(new StubRepo(sequence));

    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
      commandDispatcher: (id, _) => {
        if (id == "body") calls++;
        var state = id == "send-1" ? "executed" : "x";
        return Task.FromResult(id == "send-1"
          ? new CommandDispatchOutcome(true, null, StepStates: new Dictionary<int, string> { [12] = state })
          : CommandDispatchOutcome.Executed);
      },
      ct: CancellationToken.None);

    // The condition is true all the time, so the loop stops at the ceiling and the run reports it.
    res.Status.Should().Be("Failed");
    calls.Should().Be(10);
  }

  [Fact]
  public async Task ABreakConditionEndsTheLoop() {
    var loop = new SequenceStep {
      StepId = "loop", StepType = SequenceStepType.Loop, Loop = new CountLoopConfig { Count = 5 },
      Body = new[] {
        CommandStep("body"),
        new SequenceStep { StepId = "stop", StepType = SequenceStepType.Break, BreakCondition = Leaf() }
      }
    };

    var (res, ran) = await RunAsync(CommandStep("send-1"), loop);

    res.Status.Should().Be("Succeeded");
    ran.Count(id => id == "body").Should().Be(1);
  }
}

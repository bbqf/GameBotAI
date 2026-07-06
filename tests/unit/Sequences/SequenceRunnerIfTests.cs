using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Unit tests for if-step execution (feature 067): then/else branch selection, no-op branches,
/// condition-error parity with while loops, and behaviour inside loop bodies (re-evaluation,
/// break propagation, {{iteration}} substitution).
/// </summary>
public sealed class SequenceRunnerIfTests {
  // ──────────────────────────────────────────────────────────────────────────
  // Infrastructure
  // ──────────────────────────────────────────────────────────────────────────

  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _seq;
    public StubRepo(CommandSequence seq) { _seq = seq; }
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_seq);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() =>
        Task.FromResult<IReadOnlyList<CommandSequence>>(new[] { _seq }.ToList().AsReadOnly());
    public Task<CommandSequence> CreateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<CommandSequence> UpdateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static CommandSequence Sequence(string id, IEnumerable<SequenceStep> steps) {
    var seq = new CommandSequence { Id = id, Name = id };
    seq.SetSteps(steps.ToList());
    return seq;
  }

  private static SequenceStep ActionStep(int order, string stepId, string commandId)
      => new() {
        Order = order,
        StepId = stepId,
        CommandId = commandId,
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = "tap" }
      };

  private static SequenceStep IfStep(
      string stepId,
      IReadOnlyList<SequenceStep> thenBranch,
      IReadOnlyList<SequenceStep>? elseBranch = null,
      bool negate = false,
      int order = 0)
      => new() {
        Order = order,
        StepId = stepId,
        StepType = SequenceStepType.If,
        If = new IfConfig { Condition = new ImageVisibleStepCondition { ImageId = "img", Negate = negate } },
        Body = thenBranch,
        ElseBody = elseBranch
      };

  // ──────────────────────────────────────────────────────────────────────────
  // US1: then branch
  // ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task IfConditionTrueThenBranchRunsInOrder() {
    var executed = new List<string>();
    var ifStep = IfStep("if1", new List<SequenceStep> {
      ActionStep(0, "t1", "cmd-1"),
      ActionStep(1, "t2", "cmd-2")
    });

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-1", "cmd-2");

    var decision = result.Steps.First(s => s.CommandId == "if1");
    decision.ConditionResult.Should().Be("true");
    decision.ActionOutcome.Should().Be("then");
    decision.Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task IfConditionFalseWithoutElseSkipsBranchAndContinues() {
    var executed = new List<string>();
    var ifStep = IfStep("if1", new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") });
    var after = ActionStep(1, "after", "cmd-after");

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep, after })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-after");

    var decision = result.Steps.First(s => s.CommandId == "if1");
    decision.ConditionResult.Should().Be("false");
    decision.ActionOutcome.Should().Be("none");
  }

  [Fact]
  public async Task IfConditionEvaluatedExactlyOncePerEncounter() {
    var evalCount = 0;
    var ifStep = IfStep("if1", new List<SequenceStep> {
      ActionStep(0, "t1", "cmd-1"),
      ActionStep(1, "t2", "cmd-2")
    });

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s",
        _ => Task.CompletedTask,
        conditionEvaluator: (_, _) => { evalCount++; return Task.FromResult(true); });

    result.Status.Should().Be("Succeeded");
    evalCount.Should().Be(1);
  }

  [Fact]
  public async Task IfConditionThrowsStepAndSequenceFail() {
    var executed = new List<string>();
    var ifStep = IfStep("if1", new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") });

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => throw new InvalidOperationException("eval error"));

    result.Status.Should().Be("Failed");
    executed.Should().BeEmpty();

    var decision = result.Steps.First(s => s.CommandId == "if1");
    decision.Status.Should().Be("Failed");
    decision.ConditionResult.Should().Be("error");
  }

  [Fact]
  public async Task IfNegateInvertsConditionResult() {
    var executed = new List<string>();
    var ifStep = IfStep("if1", new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") }, negate: true);

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().BeEmpty();
    result.Steps.First(s => s.CommandId == "if1").ConditionResult.Should().Be("false");
  }

  [Fact]
  public async Task IfMissingConfigurationFailsSequence() {
    var ifStep = new SequenceStep {
      Order = 0,
      StepId = "if1",
      StepType = SequenceStepType.If,
      Body = new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s", _ => Task.CompletedTask);

    result.Status.Should().Be("Failed");
  }

  [Fact]
  public async Task LaterCommandOutcomeConditionSeesSkippedIfOutcome() {
    // The no-op if reports outcome "skipped" (mirroring a zero-iteration while loop), so a later
    // step conditioned on that outcome runs.
    var executed = new List<string>();
    var ifStep = IfStep("if1", new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") });
    var after = ActionStep(1, "after", "cmd-after");
    after.Condition = new CommandOutcomeStepCondition { StepRef = "if1", ExpectedState = "skipped" };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep, after })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-after");
  }

  // ──────────────────────────────────────────────────────────────────────────
  // US1: if blocks inside loop bodies
  // ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task IfInsideCountLoopReevaluatedPerIteration() {
    var executed = new List<string>();
    var evalCount = 0;
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 3 },
      Body = new List<SequenceStep> {
        IfStep("if1", new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") })
      }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    // Condition alternates true/false/true across the three iterations.
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(++evalCount % 2 == 1));

    result.Status.Should().Be("Succeeded");
    evalCount.Should().Be(3);
    executed.Should().Equal("cmd-then", "cmd-then");
  }

  [Fact]
  public async Task IfBranchInsideLoopSubstitutesIterationPlaceholder() {
    var executed = new List<string>();
    var branchStep = new SequenceStep {
      Order = 0,
      StepId = "t1",
      CommandId = "cmd-{{iteration}}",
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = "tap" }
    };
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new List<SequenceStep> {
        IfStep("if1", new List<SequenceStep> { branchStep })
      }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-1", "cmd-2");
  }

  [Fact]
  public async Task BreakInsideIfThenBranchExitsEnclosingLoop() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 5 },
      Body = new List<SequenceStep> {
        ActionStep(0, "work", "cmd-work"),
        IfStep("if1", new List<SequenceStep> {
          new() { Order = 0, StepId = "brk", StepType = SequenceStepType.Break }
        }, order: 1)
      }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-work");
  }

  // ──────────────────────────────────────────────────────────────────────────
  // US2: else branch
  // ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task IfConditionFalseElseBranchRuns() {
    var executed = new List<string>();
    var ifStep = IfStep("if1",
        new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") },
        new List<SequenceStep> { ActionStep(0, "e1", "cmd-else") });

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-else");

    var decision = result.Steps.First(s => s.CommandId == "if1");
    decision.ConditionResult.Should().Be("false");
    decision.ActionOutcome.Should().Be("else");
  }

  [Fact]
  public async Task IfConditionTrueElseBranchUntouched() {
    var executed = new List<string>();
    var ifStep = IfStep("if1",
        new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") },
        new List<SequenceStep> { ActionStep(0, "e1", "cmd-else") });

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-then");
  }

  [Fact]
  public async Task ElseOnlyIfWithTrueConditionIsNoop() {
    var executed = new List<string>();
    var ifStep = IfStep("if1",
        Array.Empty<SequenceStep>(),
        new List<SequenceStep> { ActionStep(0, "e1", "cmd-else") });

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().BeEmpty();
    result.Steps.First(s => s.CommandId == "if1").ActionOutcome.Should().Be("none");
  }

  [Fact]
  public async Task BothBranchesEmptyIsNoopAndSequenceContinues() {
    var executed = new List<string>();
    var ifStep = IfStep("if1", Array.Empty<SequenceStep>(), Array.Empty<SequenceStep>());
    var after = ActionStep(1, "after", "cmd-after");

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep, after })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-after");
  }

  [Fact]
  public async Task ElseBranchStepFailureFailsSequence() {
    var ifStep = IfStep("if1",
        new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") },
        new List<SequenceStep> { ActionStep(0, "e1", "cmd-else") });

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { ifStep })));
    var result = await runner.ExecuteAsync("s",
        id => id == "cmd-else" ? throw new InvalidOperationException("boom") : Task.CompletedTask,
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Failed");
  }

  [Fact]
  public async Task BreakInsideElseBranchExitsEnclosingLoop() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 5 },
      Body = new List<SequenceStep> {
        ActionStep(0, "work", "cmd-work"),
        IfStep("if1", new List<SequenceStep> { ActionStep(0, "t1", "cmd-then") },
          new List<SequenceStep> {
            new() { Order = 0, StepId = "brk", StepType = SequenceStepType.Break }
          }, order: 1)
      }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-work");
  }
}

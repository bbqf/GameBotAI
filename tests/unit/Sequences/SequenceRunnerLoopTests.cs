using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Unit tests for loop step execution: count (T009), while (T018), repeat-until (T020),
/// and break (T022) covering all story-acceptance criteria.
/// </summary>
public sealed class SequenceRunnerLoopTests {
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

  private static SequenceStep ActionBodyStep(int order, string stepId, string commandId = "inner-cmd")
      => new() {
        Order = order,
        StepId = stepId,
        CommandId = commandId,
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = "tap" }
      };

  // ──────────────────────────────────────────────────────────────────────────
  // T009: Count loop
  // ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task CountLoopExecutesBodyExactlyNTimes() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 5 },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s", id => { executed.Add(id); return Task.CompletedTask; });

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(5);
  }

  [Fact]
  public async Task CountLoopZeroCountSkipsBodyAndSucceeds() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 0 },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s", id => { executed.Add(id); return Task.CompletedTask; });

    result.Status.Should().Be("Succeeded");
    executed.Should().BeEmpty();
  }

  [Fact]
  public async Task CountLoopIterationPlaceholderSubstitutesCommandId() {
    // Body step uses {{iteration}} in CommandId so we can verify substitution
    var bodyStep = new SequenceStep {
      Order = 0,
      StepId = "inner",
      CommandId = "cmd-{{iteration}}",
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = "tap" }
    };
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 3 },
      Body = new List<SequenceStep> { bodyStep }
    };

    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s", id => { executed.Add(id); return Task.CompletedTask; });

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("cmd-1", "cmd-2", "cmd-3");
  }

  [Fact]
  public async Task CountLoopBodyAppliesAndRecordsInterStepDelayBetweenBodySteps() {
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 1 },
      Body = new List<SequenceStep>
        {
                ActionBodyStep(0, "inner-1", "cmd-1"),
                ActionBodyStep(1, "inner-2", "cmd-2")
            }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s", _ => Task.CompletedTask);

    result.Status.Should().Be("Succeeded");
    var firstBodyStep = result.Steps.First(step => step.CommandId == "cmd-1");
    firstBodyStep.InterStepDelayMs.Should().NotBeNull();
    firstBodyStep.InterStepDelayMs!.Value.Should().BeGreaterOrEqualTo(100).And.BeLessOrEqualTo(300);
  }

  // ──────────────────────────────────────────────────────────────────────────
  // T018: While loop
  // ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task WhileLoopConditionTrueTwiceThenFalseBodyRunsTwice() {
    var executed = new List<string>();
    var evalCount = 0;
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig {
        Condition = new ImageVisibleStepCondition { ImageId = "img" }
      },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    // Condition: true, true, false  (evaluated before iteration 1, 2, 3)
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (cond, _) => Task.FromResult(++evalCount <= 2));

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(2);
  }

  [Fact]
  public async Task WhileLoopConditionFalseOnEntryBodySkipped() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig {
        Condition = new ImageVisibleStepCondition { ImageId = "img" }
      },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Succeeded");
    executed.Should().BeEmpty();
    result.Steps.Should().ContainSingle();
    result.Steps[0].Status.Should().Be("Skipped");
  }

  [Fact]
  public async Task WhileLoopConditionNeverFalseFailsAtLimit() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig {
        Condition = new ImageVisibleStepCondition { ImageId = "img" },
        MaxIterations = 3
      },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Failed");
    executed.Should().HaveCount(3);
  }

  [Fact]
  public async Task WhileLoopConditionThrowsLoopFails() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig {
        Condition = new ImageVisibleStepCondition { ImageId = "img" }
      },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => throw new InvalidOperationException("eval error"));

    result.Status.Should().Be("Failed");
    executed.Should().BeEmpty();
  }

  // ──────────────────────────────────────────────────────────────────────────
  // T020: Repeat-until loop
  // ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task RepeatUntilLoopExitConditionTrueAfterIteration1BodyRunsOnce() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig {
        Condition = new ImageVisibleStepCondition { ImageId = "img" }
      },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    // Condition true on first check (after first iteration)
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(1);
  }

  [Fact]
  public async Task RepeatUntilLoopExitConditionTrueAfterIteration3BodyRuns3Times() {
    var executed = new List<string>();
    var evalCount = 0;
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig {
        Condition = new ImageVisibleStepCondition { ImageId = "img" }
      },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    // Condition: false, false, true
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(++evalCount >= 3));

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(3);
  }

  [Fact]
  public async Task RepeatUntilLoopConditionNeverTrueFailsAtLimit() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig {
        Condition = new ImageVisibleStepCondition { ImageId = "img" },
        MaxIterations = 3
      },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Failed");
    executed.Should().HaveCount(3);
  }

  [Fact]
  public async Task RepeatUntilLoopConditionThrowsAfterFirstIterationLoopFails() {
    var executed = new List<string>();
    var firstEval = true;
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig {
        Condition = new ImageVisibleStepCondition { ImageId = "img" }
      },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    // Execute once, then throw on condition eval
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => {
          if (firstEval) { firstEval = false; throw new InvalidOperationException("eval error"); }
          return Task.FromResult(false);
        });

    result.Status.Should().Be("Failed");
    executed.Should().HaveCount(1);
  }

  // ──────────────────────────────────────────────────────────────────────────
  // T022: Break steps
  // ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task CountLoopConditionalBreakOnIteration3LoopExitsAfter3Iterations() {
    var executed = new List<string>();
    var evalCount = 0;
    var breakStep = new SequenceStep {
      Order = 1,
      StepId = "break",
      StepType = SequenceStepType.Break,
      BreakCondition = new ImageVisibleStepCondition { ImageId = "img" }
    };
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 10 },
      Body = new List<SequenceStep> { ActionBodyStep(0, "inner"), breakStep }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    // Break condition returns true at evalCount == 3 (on 3rd iteration's break check)
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(++evalCount >= 3));

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(3);

    // Break was triggered — should be recorded in loop step result
    var loopStepResult = result.Steps.FirstOrDefault(s => s.LoopIterations is not null);
    loopStepResult.Should().NotBeNull();
    var lastIter = loopStepResult!.LoopIterations![^1];
    lastIter.BreakTriggered.Should().BeTrue();
  }

  [Fact]
  public async Task CountLoopConditionalBreakNeverTriggeredLoopRunsToCompletion() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 5 },
      Body = new List<SequenceStep>
        {
                ActionBodyStep(0, "inner"),
                new SequenceStep
                {
                    Order = 1,
                    StepId = "break",
                    StepType = SequenceStepType.Break,
                    BreakCondition = new ImageVisibleStepCondition { ImageId = "img" }
                }
            }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    // Break condition never true
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Succeeded"); // T015 (US2): the non-firing break never taints the run
    executed.Should().HaveCount(5);

    // T015 (US2): each non-firing break is a neutral no_break, never Skipped.
    var breaks = result.Steps.Where(s => s.CommandId == "break").ToList();
    breaks.Should().HaveCount(5);
    breaks.Should().OnlyContain(s => s.ActionOutcome == "no_break");
    breaks.Should().NotContain(s => s.Status == "Skipped");
  }

  [Fact]
  public async Task CountLoopUnconditionalBreakExitsAfterFirstIteration() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 10 },
      Body = new List<SequenceStep>
        {
                ActionBodyStep(0, "inner"),
                new SequenceStep
                {
                    Order = 1,
                    StepId = "break",
                    StepType = SequenceStepType.Break,
                    BreakCondition = null // unconditional
                }
            }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; });

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(1);
  }

  [Fact] // T014 (US2) — reverses the old fail-on-error behavior (feature 066, FR-002a).
  public async Task CountLoopBreakConditionErrorRecordedAsNoBreakLoopContinues() {
    var executed = new List<string>();
    var loopStep = CountLoopWithBreak(new ImageVisibleStepCondition { ImageId = "img" }, count: 4);

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => throw new InvalidOperationException("eval error"));

    // The break condition erroring is a neutral "no break": the loop runs to completion and the
    // run stays Succeeded (result.Fail is never called).
    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(4);

    var breaks = result.Steps.Where(s => s.CommandId == "break").ToList();
    breaks.Should().HaveCount(4);
    breaks.Should().OnlyContain(s => s.ActionOutcome == "no_break");
    breaks.Should().OnlyContain(s => s.ConditionResult == "error");
    breaks.Should().NotContain(s => s.Status == "Failed");
  }

  // ──────────────────────────────────────────────────────────────────────────
  // Feature 066: break outcome vocabulary (break / no_break)
  // ──────────────────────────────────────────────────────────────────────────

  private static SequenceStep BreakBodyStep(int order, SequenceStepCondition? condition, string stepId = "break")
      => new() {
        Order = order,
        StepId = stepId,
        StepType = SequenceStepType.Break,
        BreakCondition = condition
      };

  private static SequenceStep CountLoopWithBreak(SequenceStepCondition? breakCondition, int count) =>
      new() {
        Order = 0,
        StepId = "loop",
        StepType = SequenceStepType.Loop,
        Loop = new CountLoopConfig { Count = count },
        Body = new List<SequenceStep> { ActionBodyStep(0, "inner"), BreakBodyStep(1, breakCondition) }
      };

  [Fact] // T003 (US1)
  public async Task ConditionalBreakConditionTrueRecordsBreakSuccessAndEndsIteration() {
    var executed = new List<string>();
    var loopStep = CountLoopWithBreak(new ImageVisibleStepCondition { ImageId = "img" }, count: 5);

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(1); // break fired on first iteration → loop ends

    var brk = result.Steps.Single(s => s.CommandId == "break");
    brk.Status.Should().Be("Succeeded");
    brk.ActionOutcome.Should().Be("break");
    brk.ConditionResult.Should().Be("true");
  }

  [Fact] // T004 (US1)
  public async Task ConditionalBreakConditionFalseRecordsNoBreakAndLoopContinues() {
    var executed = new List<string>();
    var loopStep = CountLoopWithBreak(new ImageVisibleStepCondition { ImageId = "img" }, count: 3);

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false));

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(3); // never breaks → loop runs to completion

    var breaks = result.Steps.Where(s => s.CommandId == "break").ToList();
    breaks.Should().HaveCount(3);
    breaks.Should().OnlyContain(s => s.ActionOutcome == "no_break");
    breaks.Should().OnlyContain(s => s.ConditionResult == "false");
    breaks.Should().NotContain(s => s.Status == "Skipped");
    breaks.Should().NotContain(s => s.ActionOutcome == "continue");
  }

  [Fact] // T005 (US1)
  public async Task UnconditionalBreakRecordsBreakSuccessAndEndsIteration() {
    var executed = new List<string>();
    var loopStep = CountLoopWithBreak(breakCondition: null, count: 10);

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; });

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(1);

    var brk = result.Steps.Single(s => s.CommandId == "break");
    brk.Status.Should().Be("Succeeded");
    brk.ActionOutcome.Should().Be("break");
  }

  [Fact] // T026 (US1) — FR-009: a break in a while step-loop yields the same break/no_break outcomes
  public async Task BreakInWhileStepLoopYieldsSameBreakAndNoBreakOutcomes() {
    var executed = new List<string>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = new ImageVisibleStepCondition { ImageId = "loop" } },
      Body = new List<SequenceStep> {
        ActionBodyStep(0, "inner"),
        BreakBodyStep(1, new ImageVisibleStepCondition { ImageId = "brk" })
      }
    };

    var brkChecks = 0;
    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (cond, _) => {
          // While condition ("loop") stays true; break condition ("brk") fires on 2nd check.
          if (cond.TargetId == "brk") return Task.FromResult(++brkChecks >= 2);
          return Task.FromResult(true);
        });

    result.Status.Should().Be("Succeeded");

    var breaks = result.Steps.Where(s => s.CommandId == "break").ToList();
    breaks.Should().HaveCount(2);
    breaks[0].ActionOutcome.Should().Be("no_break");
    breaks[0].Status.Should().Be("Succeeded");
    breaks[1].ActionOutcome.Should().Be("break");
    breaks[1].Status.Should().Be("Succeeded");
  }

  [Fact] // T016 (US2) — a no_break on an inner break taints neither the inner loop, outer loop, nor run.
  public async Task NestedLoopInnerNoBreakDoesNotFailAnyAncestor() {
    var executed = new List<string>();
    var innerLoop = new SequenceStep {
      Order = 1,
      StepId = "inner-loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new List<SequenceStep> {
        ActionBodyStep(0, "inner"),
        BreakBodyStep(1, new ImageVisibleStepCondition { ImageId = "img" })
      }
    };
    var outerLoop = new SequenceStep {
      Order = 0,
      StepId = "outer-loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 3 },
      Body = new List<SequenceStep> { innerLoop }
    };

    var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { outerLoop })));
    var result = await runner.ExecuteAsync("s",
        id => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false)); // break never fires

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(6); // 3 outer × 2 inner iterations

    var breaks = result.Steps.Where(s => s.CommandId == "break").ToList();
    breaks.Should().OnlyContain(s => s.ActionOutcome == "no_break");
    result.Steps.Where(s => s.LoopIterations is not null).Should().OnlyContain(s => s.Status != "Failed");
  }
}

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
/// Composite guards driven through the real <see cref="SequenceRunner"/> (feature 088, issue #191):
/// a step guard, a loop break guard, and what the execution log says when a composite skips a step.
/// </summary>
public sealed class CompositeConditionRunnerTests {
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

  private static CommandSequence SequenceWithGuard(SequenceStepCondition guard) {
    var sequence = new CommandSequence { Id = "composite-runner", Name = "Composite Runner" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "dismiss",
        CommandId = "cmd-dismiss",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-dismiss" } },
        Condition = guard
      }
    });

    return sequence;
  }

  /// <summary>The B-011 guard: the shared Confirm button, and NOT the look-alike dialog's title.</summary>
  private static SequenceStepCondition DisconnectGuard() => new AllStepCondition {
    Children = new SequenceStepCondition[] {
      new ImageVisibleStepCondition { ImageId = "pns-disconnect-confirm", MinSimilarity = 0.85 },
      new ImageVisibleStepCondition { ImageId = "pns-gas-dialog-title", MinSimilarity = 0.85, Negate = true }
    }
  };

  private static Func<GameBot.Domain.Commands.Blocks.Condition, CancellationToken, Task<bool>> ScreenShowing(params string[] visible) {
    var set = new HashSet<string>(visible, StringComparer.Ordinal);
    return (condition, _) => Task.FromResult(set.Contains(condition.TargetId));
  }

  [Fact]
  public async Task TheGuardedStepDoesNotRunWhenTheLookAlikeDialogIsUp() {
    // Both the ambiguous button and the gas dialog's title are on screen. Before composites existed
    // the guard saw only the button and fired, tapping a paid-resource prompt.
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(SequenceWithGuard(DisconnectGuard())));

    var result = await runner.ExecuteAsync(
      "composite-runner",
      (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
      conditionEvaluator: ScreenShowing("pns-disconnect-confirm", "pns-gas-dialog-title"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().BeEmpty();
  }

  [Fact]
  public async Task TheGuardedStepRunsWhenOnlyTheIntendedDialogIsUp() {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(SequenceWithGuard(DisconnectGuard())));

    var result = await runner.ExecuteAsync(
      "composite-runner",
      (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
      conditionEvaluator: ScreenShowing("pns-disconnect-confirm"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-dismiss");
  }

  [Fact]
  public async Task TheGuardedStepDoesNotRunWhenNeitherImageIsUp() {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(SequenceWithGuard(DisconnectGuard())));

    await runner.ExecuteAsync(
      "composite-runner",
      (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
      conditionEvaluator: ScreenShowing(),
      ct: CancellationToken.None);

    executed.Should().BeEmpty();
  }

  [Fact]
  public async Task ASkipRecordsTheCompositeRuleAndNamesTheChildThatSettledIt() {
    // FR-016: "the guard was false" is not diagnosable; "the gas dialog was up" is.
    var runner = new SequenceRunner(new StubRepo(SequenceWithGuard(DisconnectGuard())));

    var result = await runner.ExecuteAsync(
      "composite-runner",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: ScreenShowing("pns-disconnect-confirm", "pns-gas-dialog-title"),
      ct: CancellationToken.None);

    var step = result.Steps.Should().ContainSingle().Subject;
    step.ConditionType.Should().Be("all");
    step.ConditionResult.Should().Be("false");
    step.Message.Should().Contain("$.children[1]").And.Contain("pns-gas-dialog-title");
  }

  [Fact]
  public async Task AnUnevaluableCompositeFailsTheSequenceRatherThanSkippingSilently() {
    var guard = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new CommandOutcomeStepCondition { StepRef = "never-ran", ExpectedState = "success" }
      }
    };

    var runner = new SequenceRunner(new StubRepo(SequenceWithGuard(guard)));

    var result = await runner.ExecuteAsync(
      "composite-runner",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: ScreenShowing(),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    result.Steps.Should().ContainSingle().Which.ConditionResult.Should().Be("error");
  }

  [Fact]
  public async Task ACompositeBreakConditionExitsTheLoopOnAnyListedImage() {
    var sequence = new CommandSequence { Id = "composite-runner", Name = "Composite Runner" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "drain",
        StepType = SequenceStepType.Loop,
        Loop = new CountLoopConfig { Count = 5 },
        Body = new[] {
          new SequenceStep {
            Order = 0,
            StepId = "brk",
            StepType = SequenceStepType.Break,
            BreakCondition = new AnyStepCondition {
              Children = new SequenceStepCondition[] {
                new ImageVisibleStepCondition { ImageId = "pns-city-hud" },
                new ImageVisibleStepCondition { ImageId = "pns-world-hud" }
              }
            }
          },
          new SequenceStep {
            Order = 1,
            StepId = "tap",
            CommandId = "cmd-tap",
            Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-tap" } }
          }
        }
      }
    });

    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "composite-runner",
      (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
      conditionEvaluator: ScreenShowing("pns-world-hud"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    // The break fires on the very first iteration because the second alternative is on screen.
    executed.Should().BeEmpty();
  }

  [Fact]
  public async Task ACompositeBreakReasonNamesTheRuleAndItsChildren() {
    var sequence = new CommandSequence { Id = "composite-runner", Name = "Composite Runner" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "drain",
        StepType = SequenceStepType.Loop,
        Loop = new CountLoopConfig { Count = 3 },
        Body = new[] {
          new SequenceStep {
            Order = 0,
            StepId = "brk",
            StepType = SequenceStepType.Break,
            BreakCondition = new AnyStepCondition {
              Children = new SequenceStepCondition[] {
                new ImageVisibleStepCondition { ImageId = "pns-city-hud" }
              }
            }
          }
        }
      }
    });

    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "composite-runner",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: ScreenShowing("pns-city-hud"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    var breakStep = result.Steps.FirstOrDefault(s => s.ConditionType == "any");
    breakStep.Should().NotBeNull();
  }

  [Fact]
  public async Task ACompositeWhileConditionDrivesTheLoop() {
    var sequence = new CommandSequence { Id = "composite-runner", Name = "Composite Runner" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "wait",
        StepType = SequenceStepType.Loop,
        Loop = new WhileLoopConfig {
          MaxIterations = 3,
          ExitOnMaxIterations = true,
          Condition = new AllStepCondition {
            Children = new SequenceStepCondition[] {
              new ImageVisibleStepCondition { ImageId = "spinner" },
              new ImageVisibleStepCondition { ImageId = "error-banner", Negate = true }
            }
          }
        },
        Body = new[] {
          new SequenceStep {
            Order = 0,
            StepId = "tap",
            CommandId = "cmd-tap",
            Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-tap" } }
          }
        }
      }
    });

    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    // The spinner is up but so is the error banner, so the composite is false on entry and the body
    // never runs — the loop guard and the step guard agree because they share one evaluator.
    var result = await runner.ExecuteAsync(
      "composite-runner",
      (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
      conditionEvaluator: ScreenShowing("spinner", "error-banner"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().BeEmpty();
  }

  // ---------- lastRun through the ambient queue context (feature 105) ----------

  private static LastRunStepCondition LastRunSelf() => new() { Sequence = "self", Status = "success", Since = "11:00" };

  private const string LastRunDescription = "lastRun(sequence=self, status=success, since=11:00)";

  private static SequenceStep Tap(int order, string stepId) => new() {
    Order = order,
    StepId = stepId,
    CommandId = "cmd-" + stepId,
    Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-" + stepId } }
  };

  private static CommandSequence Sequence(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "composite-runner", Name = "Composite Runner" };
    sequence.SetSteps(steps);
    return sequence;
  }

  /// <summary>Runs the sequence with a pushed queue context whose delegate answers <paramref name="lastRunValue"/>.</summary>
  private static async Task<(SequenceExecutionResult Result, List<string> Executed, int Asked)> RunWithContextAsync(
      CommandSequence sequence, bool? lastRunValue) {
    var executed = new List<string>();
    var asked = 0;
    var runner = new SequenceRunner(new StubRepo(sequence));
    IDisposable? scope = lastRunValue is { } value
      ? SequenceRunContext.Push(new SequenceRunContext("q1", "composite-runner", (_, _) => { asked++; return Task.FromResult(value); }))
      : null;
    SequenceExecutionResult result;
    using (scope) {
      result = await runner.ExecuteAsync(
        "composite-runner",
        (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
        conditionEvaluator: ScreenShowing(),
        ct: CancellationToken.None).ConfigureAwait(false);
    }

    SequenceRunContext.Current.Should().BeNull("the context must not leak past the run");
    return (result, executed, asked);
  }

  [Theory]
  [InlineData(true, true)]
  [InlineData(false, false)]
  public async Task AStepConditionReadsTheContextDelegate(bool lastRunValue, bool runs) {
    var guarded = Tap(0, "work");
    guarded.Condition = LastRunSelf();

    var (result, executed, asked) = await RunWithContextAsync(Sequence(guarded), lastRunValue);

    asked.Should().Be(1);
    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(runs ? 1 : 0);
    result.Steps.Should().ContainSingle().Which.ConditionType.Should().Be("lastRun");
  }

  [Fact]
  public async Task WithNoContextALastRunGuardIsFalseAndTheRunDoesNotFail() {
    var guarded = Tap(0, "work");
    guarded.Condition = LastRunSelf();

    var (result, executed, _) = await RunWithContextAsync(Sequence(guarded), lastRunValue: null);

    result.Status.Should().Be("Succeeded");
    executed.Should().BeEmpty();
    var step = result.Steps.Should().ContainSingle().Subject;
    step.ConditionType.Should().Be("lastRun");
    step.ConditionResult.Should().Be("false");
  }

  [Theory]
  [InlineData(true, "cmd-then")]
  [InlineData(false, "cmd-else")]
  public async Task AnIfConditionReadsTheContextDelegate(bool lastRunValue, string expected) {
    var branch = new SequenceStep {
      Order = 0,
      StepId = "branch",
      StepType = SequenceStepType.If,
      If = new IfConfig { Condition = LastRunSelf() },
      Body = new[] { Tap(0, "then") },
      ElseBody = new[] { Tap(0, "else") }
    };

    var (result, executed, _) = await RunWithContextAsync(Sequence(branch), lastRunValue);

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal(expected);
    result.Steps[0].ConditionType.Should().Be("lastRun");
    result.Steps[0].Message.Should().Contain(LastRunDescription);
  }

  [Theory]
  [InlineData(true, 3)]
  [InlineData(false, 0)]
  public async Task AWhileLoopConditionReadsTheContextDelegate(bool lastRunValue, int expectedTaps) {
    var loop = new SequenceStep {
      Order = 0,
      StepId = "wait",
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { MaxIterations = 3, ExitOnMaxIterations = true, Condition = LastRunSelf() },
      Body = new[] { Tap(0, "tap") }
    };

    var (result, executed, asked) = await RunWithContextAsync(Sequence(loop), lastRunValue);

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(expectedTaps);
    asked.Should().BeGreaterThan(0);
  }

  [Theory]
  [InlineData(true, 1)]
  [InlineData(false, 3)]
  public async Task ARepeatUntilLoopConditionReadsTheContextDelegate(bool lastRunValue, int expectedTaps) {
    // Analyze finding G2: while and repeatUntil share EvaluateLoopConditionAsync; this pins it.
    var loop = new SequenceStep {
      Order = 0,
      StepId = "until",
      StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig { MaxIterations = 3, ExitOnMaxIterations = true, Condition = LastRunSelf() },
      Body = new[] { Tap(0, "tap") }
    };

    var (result, executed, asked) = await RunWithContextAsync(Sequence(loop), lastRunValue);

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(expectedTaps);
    asked.Should().BeGreaterThan(0);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ALastRunBreakInALoopBodyStopsOrContinuesTheLoop(bool lastRunValue) {
    var loop = new SequenceStep {
      Order = 0,
      StepId = "once",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new[] {
        new SequenceStep { Order = 0, StepId = "guard", StepType = SequenceStepType.Break, BreakCondition = LastRunSelf() },
        Tap(1, "work")
      }
    };

    var (result, executed, _) = await RunWithContextAsync(Sequence(loop), lastRunValue);

    result.Status.Should().Be("Succeeded");
    var breakSteps = result.Steps.Where(s => s.CommandId == "guard").ToList();
    breakSteps.Should().NotBeEmpty();
    breakSteps.Should().OnlyContain(s => s.ConditionType == "lastRun");
    if (lastRunValue) {
      executed.Should().BeEmpty();
      breakSteps.Should().ContainSingle().Which.ActionOutcome.Should().Be(BreakOutcomes.Break);
      breakSteps[0].Message.Should().Contain(LastRunDescription);
    }
    else {
      executed.Should().Equal("cmd-work", "cmd-work");
      breakSteps.Should().HaveCount(2).And.OnlyContain(s => s.ActionOutcome == BreakOutcomes.NoBreak);
    }
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ALastRunBreakInAnIfBranchInALoopBodyStopsOrContinuesTheLoop(bool lastRunValue) {
    var loop = new SequenceStep {
      Order = 0,
      StepId = "once",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 1 },
      Body = new[] {
        new SequenceStep {
          Order = 0,
          StepId = "branch",
          StepType = SequenceStepType.If,
          If = new IfConfig { Condition = new ImageVisibleStepCondition { ImageId = "never-visible", Negate = true } },
          Body = new[] {
            new SequenceStep { Order = 0, StepId = "guard", StepType = SequenceStepType.Break, BreakCondition = LastRunSelf() }
          }
        },
        Tap(1, "after-branch")
      }
    };

    var (result, executed, _) = await RunWithContextAsync(Sequence(loop), lastRunValue);

    result.Status.Should().Be("Succeeded");
    var guard = result.Steps.Should().ContainSingle(s => s.CommandId == "guard").Subject;
    guard.ConditionType.Should().Be("lastRun");
    if (lastRunValue) {
      guard.ActionOutcome.Should().Be(BreakOutcomes.Break);
      guard.Message.Should().Contain(LastRunDescription);
      executed.Should().BeEmpty();
    }
    else {
      guard.ActionOutcome.Should().Be(BreakOutcomes.NoBreak);
      executed.Should().Equal("cmd-after-branch");
    }
  }
}

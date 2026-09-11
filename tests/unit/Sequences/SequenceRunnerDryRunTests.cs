using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 082 (FR-002): with <c>dryRun: true</c>, <see cref="SequenceRunner"/> skips every step
/// that would dispatch to the emulator, start/use a session, or read live capture state, reporting
/// <see cref="DryRunOutcomes.SkippedDryRun"/> for each, while Loop/If/Break
/// control-flow evaluation stays real.
/// </summary>
public sealed class SequenceRunnerDryRunTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceRunner Runner(CommandSequence sequence) => new(new StubRepo(sequence));

  private static CommandSequence Sequence(string id, IEnumerable<SequenceStep> steps) {
    var sequence = new CommandSequence { Id = id, Name = id };
    sequence.SetSteps(new List<SequenceStep>(steps));
    return sequence;
  }

  [Fact]
  public async Task DryRunSkipsPrimitiveTapWithoutInvokingActionDispatcher() {
    var sequence = Sequence("tap-seq", new[] {
      new SequenceStep {
        Order = 0,
        StepId = "tap-1",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = "tap", Parameters = { ["x"] = 1, ["y"] = 1 } }
      }
    });
    var dispatched = new List<SequenceActionPayload>();

    var result = await Runner(sequence).ExecuteAsync(
      "tap-seq",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (action, _) => {
        dispatched.Add(action);
        return Task.FromResult(new ActionDispatchResult("executed", "should not have been called"));
      },
      dryRun: true,
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    dispatched.Should().BeEmpty();
    result.Steps.Should().ContainSingle(s => s.CommandId == "tap-1"
      && s.Status == "Succeeded"
      && s.ActionOutcome == DryRunOutcomes.SkippedDryRun);
  }

  [Fact]
  public async Task DryRunSkipsResolvableCommandStepEvenWithRequireDispatchAndNeverFailsTheSequence() {
    var sequence = Sequence("cmd-seq", new[] {
      new SequenceStep {
        Order = 0,
        StepId = "cmd-1",
        StepType = SequenceStepType.Command,
        CommandId = "real-command",
        RequireDispatch = true
      }
    });
    var commandDispatcherCalls = 0;

    var result = await Runner(sequence).ExecuteAsync(
      "cmd-seq",
      (_, _) => Task.CompletedTask,
      commandDispatcher: (_, _) => {
        commandDispatcherCalls++;
        // Simulates SequenceExecutionService.DispatchCommandAsync's dry-run existence-check
        // carve-out: the commandId resolved, so it reports SkippedDryRun rather than a real miss.
        return Task.FromResult(new CommandDispatchOutcome(false, null, SkippedDryRun: true));
      },
      dryRun: true,
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    commandDispatcherCalls.Should().Be(1);
    // The command-dispatch path records StepResult.CommandId as the raw CommandId, not the StepId
    // (existing convention — see the "executed"/"not_executed" AddStep calls just above this one).
    result.Steps.Should().ContainSingle(s => s.CommandId == "real-command"
      && s.Status == "Succeeded"
      && s.ActionOutcome == DryRunOutcomes.SkippedDryRun);
  }

  [Fact]
  public async Task DryRunSkipsRescheduleSelfWithoutInvokingActionDispatcher() {
    var sequence = Sequence("reschedule-seq", new[] {
      new SequenceStep {
        Order = 0,
        StepId = "reschedule",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf }
      }
    });
    var dispatched = new List<SequenceActionPayload>();

    var result = await Runner(sequence).ExecuteAsync(
      "reschedule-seq",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (action, _) => {
        dispatched.Add(action);
        return Task.FromResult(new ActionDispatchResult("scheduled", "should not have been called"));
      },
      dryRun: true,
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    dispatched.Should().BeEmpty();
    result.Steps.Should().ContainSingle(s => s.CommandId == "reschedule"
      && s.ActionOutcome == DryRunOutcomes.SkippedDryRun);
  }

  [Fact]
  public async Task DryRunSkipsWaitForImageWithoutInvokingConditionEvaluator() {
    var sequence = Sequence("wait-seq", new[] {
      new SequenceStep {
        Order = 0,
        StepId = "wait-1",
        Action = new SequenceActionPayload { Type = "WaitForImage" },
        WaitForImage = new WaitForImageConfig { TimeoutMs = 50, DetectionTarget = new DetectionTarget("img", 0.9) }
      }
    });
    var evaluatorCalls = 0;

    var result = await Runner(sequence).ExecuteAsync(
      "wait-seq",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: (_, _) => { evaluatorCalls++; return Task.FromResult(true); },
      dryRun: true,
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    evaluatorCalls.Should().Be(0);
    result.Steps.Should().ContainSingle(s => s.CommandId == "wait-1"
      && s.ActionOutcome == DryRunOutcomes.SkippedDryRun);
  }

  [Fact]
  public async Task DryRunLoopBreakFiringStillReportsCorrectExitReason() {
    // The break condition references a prior sibling's recorded outcome (commandOutcome), resolved
    // directly against the in-memory stepOutcomes map — never touching conditionEvaluator — so this
    // exercises exactly the "not live device state" control-flow path dry-run must keep real.
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 5, MaxIterations = 5 },
      Body = new List<SequenceStep> {
        new() {
          Order = 0,
          StepId = "gate",
          StepType = SequenceStepType.Break,
          BreakCondition = null // unconditional: fires on the first iteration
        }
      }
    };
    var sequence = Sequence("loop-seq", new[] { loopStep });

    var result = await Runner(sequence).ExecuteAsync(
      "loop-seq",
      (_, _) => Task.CompletedTask,
      dryRun: true,
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    var loopResult = result.Steps.Should().ContainSingle(s => s.CommandId == "loop").Subject;
    loopResult.ExitReason.Should().NotBeNull();
    loopResult.ExitReason!.BrokeVia.Should().Be("gate");
    loopResult.ExitReason.ExhaustedMaxIterations.Should().BeFalse();
  }

  [Fact]
  public async Task DryRunLoopExhaustingMaxIterationsStillReportsExhausted() {
    // "primer" runs (and is itself dry-run-skipped) before the loop, so its recorded outcome is
    // already available. The while condition negates an ExpectedState it can never match
    // ("success"), so it is always true and the loop can only end by exhausting MaxIterations.
    var primerStep = new SequenceStep {
      Order = 0,
      StepId = "primer",
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = "tap", Parameters = { ["x"] = 0, ["y"] = 0 } }
    };
    var loopStep = new SequenceStep {
      Order = 1,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig {
        Condition = new CommandOutcomeStepCondition { StepRef = "primer", ExpectedState = "success", Negate = true },
        MaxIterations = 3,
        ExitOnMaxIterations = true
      },
      Body = new List<SequenceStep> {
        new() {
          Order = 0,
          StepId = "tap-in-body",
          StepType = SequenceStepType.Action,
          Action = new SequenceActionPayload { Type = "tap", Parameters = { ["x"] = 1, ["y"] = 1 } }
        }
      }
    };
    var sequence = Sequence("loop-seq-2", new[] { primerStep, loopStep });

    var result = await Runner(sequence).ExecuteAsync(
      "loop-seq-2",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (_, _) => Task.FromResult(new ActionDispatchResult("executed", "should not have been called")),
      dryRun: true,
      ct: CancellationToken.None);

    var loopResult = result.Steps.Should().ContainSingle(s => s.CommandId == "loop").Subject;
    loopResult.ExitReason.Should().NotBeNull();
    loopResult.ExitReason!.BrokeVia.Should().BeNull();
    loopResult.ExitReason.ExhaustedMaxIterations.Should().BeTrue();
    // The loop's own body steps were still skipped under dry-run, not dispatched.
    result.Steps.Should().Contain(s => s.CommandId == "tap-in-body" && s.ActionOutcome == DryRunOutcomes.SkippedDryRun);
  }

  [Fact]
  public async Task CommandOutcomeReferenceToADryRunSkippedStepNeverMatchesAnyExpectedState() {
    var sequence = Sequence("ref-seq", new[] {
      new SequenceStep {
        Order = 0,
        StepId = "tap-1",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = "tap", Parameters = { ["x"] = 1, ["y"] = 1 } }
      },
      new SequenceStep {
        Order = 1,
        StepId = "gate",
        CommandId = "gate",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = "tap", Parameters = { ["x"] = 2, ["y"] = 2 } },
        Condition = new CommandOutcomeStepCondition { StepRef = "tap-1", ExpectedState = "success" }
      }
    });

    var result = await Runner(sequence).ExecuteAsync(
      "ref-seq",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (_, _) => Task.FromResult(new ActionDispatchResult("executed", "should not have been called")),
      dryRun: true,
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    result.Steps.Should().Contain(s => s.CommandId == "tap-1" && s.ActionOutcome == DryRunOutcomes.SkippedDryRun);
    // "gate"'s commandOutcome condition expects "success", but "tap-1" resolved to "skipped_dry_run" —
    // never a match — so "gate" is skipped, exactly like any other non-matching reference.
    result.Steps.Should().Contain(s => s.CommandId == "gate" && s.Status == "Skipped" && s.ActionOutcome == "skipped");
  }
}

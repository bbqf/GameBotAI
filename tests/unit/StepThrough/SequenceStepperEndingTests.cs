using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepperTestKit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

/// <summary>
/// Steps that end a real run (break with no loop, a failed step) and steps with an outside effect
/// (reschedule-self). A step-through shows the effect and goes on to the next step (spec Clarifications).
/// </summary>
public sealed class SequenceStepperEndingTests {
  private static SequenceStep Reschedule(int order, string id) {
    var step = Cmd(order, id);
    step.Action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    step.CommandId = string.Empty;
    return step;
  }

  [Fact]
  public async Task BreakOutsideALoopSaysNoLoopIsActiveAndGoesToTheNextStep() {
    var sequence = Sequence(Break(0, "brk"), Cmd(1, "after"));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(new List<string>()));

    var entry = produced.Should().ContainSingle().Subject;
    entry.Status.Should().Be("Succeeded");
    entry.Outcome.Should().Be(BreakOutcomes.Break);
    entry.Notes.Should().Contain(SequenceStepper.NoLoopNote);
    state.Cursor.Should().Be("1");
  }

  [Fact]
  public async Task FailedStepSaysTheSequenceWouldEndHereAndGoesToTheNextStep() {
    var sequence = Sequence(Cmd(0, "bad"), Cmd(1, "after"));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = Deps(new List<string>(), commandDispatcher: (_, _) => throw new InvalidOperationException("no good"));

    var produced = await stepper.RunNextAsync(sequence, state, deps);

    var entry = produced.Should().ContainSingle().Subject;
    entry.Status.Should().Be("Failed");
    entry.Notes.Should().Contain(SequenceStepper.SequenceEndNote);
    state.Cursor.Should().Be("1");
  }

  [Fact]
  public async Task RescheduleSelfShowsItsIntendedEffectAndNeverReachesTheRealDispatcher() {
    var sequence = Sequence(Reschedule(0, "resched"), Cmd(1, "after"));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var realCalls = 0;
    var deps = new StepperDependencies {
      ExecuteCommandAsync = (_, _) => Task.CompletedTask,
      ActionDispatcher = (_, _) => {
        realCalls++;
        return Task.FromResult(new ActionDispatchResult("scheduled", "real"));
      },
      PreviewServiceAction = (_, _) => Task.FromResult(new ActionDispatchResult(SequenceStepper.PreviewOutcome, "would reschedule at 14:30"))
    };

    var produced = await stepper.RunNextAsync(sequence, state, deps);

    var entry = produced.Should().ContainSingle().Subject;
    realCalls.Should().Be(0);
    entry.Status.Should().Be("Succeeded");
    entry.Effects.Should().Equal("would reschedule at 14:30");
    entry.Notes.Should().NotContain(SequenceStepper.SequenceEndNote);
    state.Cursor.Should().Be("1");
  }

  [Fact]
  public async Task NotifyIsPreviewedWithAShortTextWhenNoPreviewCallbackExists() {
    var step = Cmd(0, "n");
    step.Action = new SequenceActionPayload { Type = ActionTypes.Notify };
    step.CommandId = string.Empty;
    var sequence = Sequence(step);
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = new StepperDependencies {
      ExecuteCommandAsync = (_, _) => Task.CompletedTask,
      ActionDispatcher = (_, _) => throw new InvalidOperationException("must not run")
    };

    var produced = await stepper.RunNextAsync(sequence, state, deps);

    produced.Single().Effects.Should().ContainSingle().Which.Should().Contain(ActionTypes.Notify);
  }

  [Fact]
  public async Task EffectsPreviewedInsideACommandStepAppearOnTheHistoryEntry() {
    var sequence = Sequence(Cmd(0, "cmd"));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = Deps(
      new List<string>(),
      commandDispatcher: (_, _) => Task.FromResult(new CommandDispatchOutcome(true, null, PreviewedEffects: new[] { "would notify" })));

    var produced = await stepper.RunNextAsync(sequence, state, deps);

    produced.Single().Effects.Should().Equal("would notify");
  }
}

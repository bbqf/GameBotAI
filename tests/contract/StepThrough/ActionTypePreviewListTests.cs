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

#pragma warning disable CA2007, CA1861, CA1859, CA1849, CA1054, CA1056, CA1307, CA1308

namespace GameBot.ContractTests.StepThrough;

/// <summary>
/// A guard for new sequence action types (feature 127, research R4). A step-through must never apply an
/// action with an outside effect. Every action type in <see cref="SequenceActionTypes.All"/> is listed here
/// as <c>runs</c> or <c>previews</c>. A new type that is not listed fails this test. The test then
/// runs each type through the stepper and checks that the list matches the behavior.
/// </summary>
public sealed class ActionTypePreviewListTests {
  private enum Mode { Runs, Previews }

  private static readonly Dictionary<string, Mode> Listed = new(StringComparer.OrdinalIgnoreCase) {
    [ActionTypes.Tap] = Mode.Runs,
    [ActionTypes.Swipe] = Mode.Runs,
    [ActionTypes.Key] = Mode.Runs,
    [ActionTypes.Command] = Mode.Runs,
    [ActionTypes.ConnectToGame] = Mode.Runs,
    [ActionTypes.WaitForImage] = Mode.Runs,
    [ActionTypes.EnsureGameRunning] = Mode.Runs,
    [ActionTypes.GoToHomeScreen] = Mode.Runs,
    [ActionTypes.EnsureEmulatorRunning] = Mode.Runs,
    [ActionTypes.RescheduleSelf] = Mode.Previews,
    [ActionTypes.Notify] = Mode.Previews
  };

  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;

    public StubRepo(CommandSequence sequence) { _sequence = sequence; }

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);

    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new[] { _sequence });

    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  public static IEnumerable<object[]> AllTypes() => SequenceActionTypes.All.Select(type => new object[] { type });

  // Feature 129: an ensure-game-running step with forceRestart true stops the game, so the stepper
  // previews it. A plain step still reaches the real dispatcher.
  [Theory]
  [InlineData(true, 0)]
  [InlineData(false, 1)]
  public async Task AnEnsureGameRunningStepIsPreviewedOnlyWithForceRestart(bool forceRestart, int expectedRealDispatches) {
    var action = new SequenceActionPayload { Type = ActionTypes.EnsureGameRunning };
    action.Parameters["forceRestart"] = forceRestart;
    var step = new SequenceStep {
      Order = 0,
      StepId = "s",
      CommandId = "cmd",
      StepType = SequenceStepType.Action,
      Action = action
    };
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(new[] { step });
    var realDispatches = 0;
    var dependencies = new StepperDependencies {
      ExecuteCommandAsync = (_, _) => Task.CompletedTask,
      ActionDispatcher = (_, _) => {
        realDispatches++;
        return Task.FromResult(new ActionDispatchResult("executed", null));
      }
    };
    var state = new StepperState();
    state.Restart(sequence);

    var produced = await new SequenceStepper(new SequenceRunner(new StubRepo(sequence)))
      .RunNextAsync(sequence, state, dependencies);

    realDispatches.Should().Be(expectedRealDispatches);
    var entry = produced.Single();
    if (forceRestart) {
      entry.Outcome.Should().Be(SequenceStepper.PreviewOutcome);
    }
    else {
      entry.Outcome.Should().NotBe(SequenceStepper.PreviewOutcome);
    }
  }

  [Fact]
  public void EverySequenceActionTypeIsListedAsRunsOrPreviews() {
    foreach (var type in SequenceActionTypes.All) {
      Listed.Should().ContainKey(type, $"the action type '{type}' must be listed as runs or previews for the step-through (feature 127)");
    }

    Listed.Keys.Should().OnlyContain(key => SequenceActionTypes.All.Contains(key, StringComparer.OrdinalIgnoreCase));
  }

  [Theory]
  [MemberData(nameof(AllTypes))]
  public async Task TheStepperDoesWhatTheListSays(string type) {
    var step = new SequenceStep {
      Order = 0,
      StepId = "s",
      CommandId = "cmd",
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = type }
    };
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(new[] { step });
    var realDispatches = 0;
    var dependencies = new StepperDependencies {
      ExecuteCommandAsync = (_, _) => Task.CompletedTask,
      ActionDispatcher = (_, _) => {
        realDispatches++;
        return Task.FromResult(new ActionDispatchResult("executed", null));
      }
    };
    var state = new StepperState();
    state.Restart(sequence);

    var produced = await new SequenceStepper(new SequenceRunner(new StubRepo(sequence)))
      .RunNextAsync(sequence, state, dependencies);

    var entry = produced.Single();
    if (Listed[type] == Mode.Previews) {
      realDispatches.Should().Be(0, $"'{type}' has an outside effect and must not reach the real dispatcher");
      entry.Outcome.Should().Be(SequenceStepper.PreviewOutcome);
      entry.Effects.Should().NotBeEmpty();
    }
    else {
      entry.Outcome.Should().NotBe(SequenceStepper.PreviewOutcome, $"'{type}' runs in a step-through");
      entry.Effects.Should().BeEmpty();
    }
  }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

/// <summary>Builders and a driver for the step-through tests.</summary>
internal static class StepperTestKit {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;

    public StubRepo(CommandSequence sequence) { _sequence = sequence; }

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);

    public Task<IReadOnlyList<CommandSequence>> ListAsync()
      => Task.FromResult<IReadOnlyList<CommandSequence>>(new[] { _sequence });

    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  public static CommandSequence Sequence(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(steps);
    return sequence;
  }

  public static SequenceRunner Runner(CommandSequence sequence) => new(new StubRepo(sequence));

  /// <summary>A step that runs a command. The command id is the visit marker in the tests.</summary>
  public static SequenceStep Cmd(int order, string commandId, SequenceStepCondition? guard = null)
    => new() {
      Order = order,
      StepId = commandId,
      CommandId = commandId,
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = "tap" },
      Condition = guard
    };

  public static SequenceStep CountLoop(int order, string id, int count, params SequenceStep[] body)
    => new() {
      Order = order,
      StepId = id,
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = count },
      Body = body
    };

  public static SequenceStep WhileLoop(int order, string id, int? max, bool exitOnMax, params SequenceStep[] body)
    => new() {
      Order = order,
      StepId = id,
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = Image("w-" + id), MaxIterations = max, ExitOnMaxIterations = exitOnMax },
      Body = body
    };

  public static SequenceStep RepeatUntil(int order, string id, params SequenceStep[] body)
    => new() {
      Order = order,
      StepId = id,
      StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig { Condition = Image("u-" + id) },
      Body = body
    };

  public static SequenceStep If(int order, string id, IReadOnlyList<SequenceStep> thenSteps, IReadOnlyList<SequenceStep>? elseSteps = null)
    => new() {
      Order = order,
      StepId = id,
      StepType = SequenceStepType.If,
      If = new IfConfig { Condition = Image("i-" + id) },
      Body = thenSteps,
      ElseBody = elseSteps
    };

  public static SequenceStep Break(int order, string id, SequenceStepCondition? condition = null)
    => new() { Order = order, StepId = id, StepType = SequenceStepType.Break, BreakCondition = condition };

  public static ImageVisibleStepCondition Image(string imageId) => new() { ImageId = imageId };

  /// <summary>
  /// Dependencies that record each command id and answer image conditions from <paramref name="answers"/>:
  /// a queue of results for each image id. An image id with an empty queue answers false.
  /// </summary>
  public static StepperDependencies Deps(
      List<string> executed,
      Dictionary<string, Queue<bool>>? answers = null,
      Func<string, ParameterScope, Task<CommandDispatchOutcome>>? commandDispatcher = null,
      Func<SequenceActionPayload, CancellationToken, Task<ActionDispatchResult>>? preview = null,
      Func<SequenceStep, CancellationToken, Task<bool>>? gate = null)
    => new() {
      ExecuteCommandAsync = (id, _) => { executed.Add(id); return Task.CompletedTask; },
      CommandDispatcher = commandDispatcher,
      GateEvaluator = gate,
      ConditionEvaluator = (condition, _) => {
        var id = condition.TargetId ?? string.Empty;
        return Task.FromResult(answers is not null && answers.TryGetValue(id, out var queue) && queue.Count > 0 && queue.Dequeue());
      },
      PreviewServiceAction = preview
    };

  public static Dictionary<string, Queue<bool>> Answers(params (string ImageId, bool[] Values)[] items)
    => items.ToDictionary(i => i.ImageId, i => new Queue<bool>(i.Values));

  /// <summary>Starts a state at the first step.</summary>
  public static StepperState Start(CommandSequence sequence) {
    var state = new StepperState();
    state.Restart(sequence);
    return state;
  }

  /// <summary>Runs the next step until the sequence is complete. Returns the leaf entries in order.</summary>
  public static async Task<List<HistoryEntry>> RunToEndAsync(
      SequenceStepper stepper,
      CommandSequence sequence,
      StepperState state,
      StepperDependencies dependencies,
      int maxCalls = 200) {
    var steps = new List<HistoryEntry>();
    for (var i = 0; i < maxCalls && state.Cursor is not null; i++) {
      var produced = await stepper.RunNextAsync(sequence, state, dependencies);
      steps.AddRange(produced.Where(e => e.Kind == HistoryKind.Step));
    }

    return steps;
  }
}

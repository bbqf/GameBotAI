using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Parameters;

namespace GameBot.Domain.Services.StepThrough;

/// <summary>
/// The callbacks that the stepper needs to run one step. They have the same shapes as the arguments of
/// <see cref="SequenceRunner.ExecuteAsync"/>, so the service builds both from the same wiring.
/// </summary>
public sealed class StepperDependencies {
  /// <summary>Runs a command step when no <see cref="CommandDispatcher"/> exists.</summary>
  public required Func<string, ParameterScope, Task> ExecuteCommandAsync { get; init; }

  /// <summary>Runs a command step and reports what it dispatched. May report previewed effects.</summary>
  public Func<string, ParameterScope, Task<CommandDispatchOutcome>>? CommandDispatcher { get; init; }

  /// <summary>Evaluates the gate of a step.</summary>
  public Func<SequenceStep, CancellationToken, Task<bool>>? GateEvaluator { get; init; }

  /// <summary>Evaluates image and text conditions.</summary>
  public Func<Condition, CancellationToken, Task<bool>>? ConditionEvaluator { get; init; }

  /// <summary>Dispatches device actions (tap, swipe, key, and so on).</summary>
  public Func<SequenceActionPayload, CancellationToken, Task<ActionDispatchResult>>? ActionDispatcher { get; init; }

  /// <summary>
  /// Describes the effect of an action with an outside effect (<c>reschedule-self</c> and <c>notify</c>)
  /// without it being applied (FR-015). The result message is the intended effect. When this callback
  /// is null, the stepper uses a short fixed text.
  /// </summary>
  public Func<SequenceActionPayload, CancellationToken, Task<ActionDispatchResult>>? PreviewServiceAction { get; init; }
}

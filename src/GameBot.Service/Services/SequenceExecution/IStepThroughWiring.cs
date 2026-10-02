using System.Threading;
using GameBot.Domain.Services.StepThrough;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>What the wiring of one step-through run needs (feature 127).</summary>
/// <param name="SequenceId">The id of the saved sequence.</param>
/// <param name="SequenceName">The name of the sequence, for the execution log.</param>
/// <param name="SessionId">The game session that receives the input.</param>
/// <param name="RootExecutionId">The id of the execution log entry that the commands of this run link to.</param>
/// <param name="Token">The token that cancels the run. The command callbacks use it.</param>
internal sealed record StepWiringRequest(
  string SequenceId,
  string SequenceName,
  string? SessionId,
  string RootExecutionId,
  CancellationToken Token);

/// <summary>
/// Builds the callbacks that the stepper needs (feature 127). It uses the same code as a real run. A step in
/// a step-through therefore reaches the device in the same way as a step in a real run. The command
/// callback runs in preview mode. The actions with an outside effect only show their effect.
/// </summary>
internal interface IStepThroughWiring {
  /// <summary>Creates the callbacks for one run of one step.</summary>
  StepperDependencies CreateStepWiring(StepWiringRequest request);
}

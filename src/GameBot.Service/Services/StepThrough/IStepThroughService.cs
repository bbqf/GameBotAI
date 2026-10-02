using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Service.Services.StepThrough;

/// <summary>
/// Owns the step-through runs (feature 127). A step-through is in memory only. It has one cursor, one
/// history, and one lease. A read of the state renews the lease. A step runs in the background, so the
/// calls that start a step return at once.
/// </summary>
internal interface IStepThroughService {
  /// <summary>Starts a step-through. It does not run a step.</summary>
  Task<StepThroughResult<StepThroughStateDto>> StartAsync(StartStepThroughRequest request, CancellationToken ct = default);

  /// <summary>Reads the state and renews the lease. With <paramref name="afterSeq"/> the history holds only newer entries.</summary>
  StepThroughResult<StepThroughStateDto> Get(string id, int? afterSeq = null);

  /// <summary>Starts the step at the cursor in the background. The state shows <c>running</c> at once.</summary>
  Task<StepThroughResult<StepThroughStateDto>> RunNextAsync(string id, CancellationToken ct = default);

  /// <summary>Sets the next step by hand.</summary>
  Task<StepThroughResult<StepThroughStateDto>> SelectAsync(string id, string? path, CancellationToken ct = default);

  /// <summary>Cancels the running step. It does nothing when no step runs.</summary>
  StepThroughResult<CancelOutcome> Cancel(string id);

  /// <summary>Clears the history, the frames, and the outcomes. Keeps the parameter values and the queue pause.</summary>
  Task<StepThroughResult<StepThroughStateDto>> RestartAsync(string id, CancellationToken ct = default);

  /// <summary>Changes the parameter values and the step outcomes. History and cursor stay.</summary>
  StepThroughResult<StepThroughStateDto> SetValues(string id, SetValuesRequest request);

  /// <summary>Pauses the queue that owns the device of the session.</summary>
  StepThroughResult<StepThroughStateDto> PauseQueue(string id);

  /// <summary>Ends the step-through: cancels a running step and resumes a queue that it paused. An unknown id is ignored.</summary>
  Task EndAsync(string id);

  /// <summary>Ends every step-through whose lease ended. Returns the number of step-throughs that it ended.</summary>
  Task<int> SweepExpiredAsync();

  /// <summary>The task of the step that runs now, or null. Tests use it to wait for the end of a step.</summary>
  Task? RunningTask(string id);
}

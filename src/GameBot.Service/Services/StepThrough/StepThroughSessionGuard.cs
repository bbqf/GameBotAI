using System;
using GameBot.Service.Services.QueueExecution;

namespace GameBot.Service.Services.StepThrough;

/// <summary>What a queue is doing on the device of a step-through session.</summary>
internal enum SessionQueueState {
  /// <summary>No queue run owns the device. The step-through can run.</summary>
  NoQueue,

  /// <summary>A queue run owns the device, runs, and has no firing now. The next firing can start at any time.</summary>
  Running,

  /// <summary>A queue run owns the device and a firing runs now. The step-through must wait for it to end.</summary>
  FiringActive,

  /// <summary>A queue run owns the device and is paused. No firing starts. The step-through can run.</summary>
  Paused
}

/// <summary>A snapshot of the queue that owns the device of a session.</summary>
/// <param name="State">What the queue does now.</param>
/// <param name="QueueId">The id of the owning queue. Null when <paramref name="State"/> is <see cref="SessionQueueState.NoQueue"/>.</param>
/// <param name="QueueName">The name of the owning queue.</param>
/// <param name="PausedByStepThrough">True when the pause of the queue has the reason of a step-through.</param>
internal sealed record SessionQueueStatus(
  SessionQueueState State,
  string? QueueId = null,
  string? QueueName = null,
  bool PausedByStepThrough = false);

/// <summary>The result of <see cref="IStepThroughSessionGuard.TryPause"/>.</summary>
internal enum QueuePauseOutcome {
  /// <summary>The guard paused the queue. The step-through must resume it at the end.</summary>
  Paused,

  /// <summary>The queue was paused before. The guard did not change it. The step-through must not resume it (FR-012b).</summary>
  AlreadyPaused,

  /// <summary>No queue run owns the device.</summary>
  NoQueue,

  /// <summary>A firing runs now. The queue was not paused (FR-012c).</summary>
  FiringActive
}

/// <summary>
/// Finds the queue that owns the device of a step-through session (feature 127, R6). It pauses and
/// resumes that queue with the policy-pause gate of the queue run. The guard pauses a queue only when
/// the author asks. It resumes only a pause that it made.
/// </summary>
internal interface IStepThroughSessionGuard {
  /// <summary>Reads what the owning queue of <paramref name="deviceSerial"/> does now.</summary>
  SessionQueueStatus Inspect(string? deviceSerial);

  /// <summary>Pauses the owning queue of <paramref name="deviceSerial"/>, when no firing runs.</summary>
  QueuePauseOutcome TryPause(string? deviceSerial, out SessionQueueStatus status);

  /// <summary>
  /// Resumes the queue <paramref name="queueId"/> when its pause has the step-through reason. Returns true
  /// when it resumed the queue. A pause from another cause stays.
  /// </summary>
  bool Resume(string queueId);
}

/// <inheritdoc cref="IStepThroughSessionGuard"/>
internal sealed class StepThroughSessionGuard : IStepThroughSessionGuard {
  /// <summary>The reason that the guard gives to the pause of a queue.</summary>
  public const string PauseReason = "step-through";

  private readonly IQueueRunRegistry _runs;
  private readonly IDeviceClaimRegistry _claims;
  private readonly TimeProvider _time;

  public StepThroughSessionGuard(IQueueRunRegistry runs, IDeviceClaimRegistry claims, TimeProvider time) {
    _runs = runs;
    _claims = claims;
    _time = time;
  }

  public SessionQueueStatus Inspect(string? deviceSerial) {
    if (!TryFindRun(deviceSerial, out var claim, out var handle)) {
      return new SessionQueueStatus(SessionQueueState.NoQueue);
    }

    var pausedByUs = handle.IsPolicyPaused && string.Equals(handle.PauseReason, PauseReason, StringComparison.Ordinal);
    var state = handle.CurrentSequenceId is not null
      ? SessionQueueState.FiringActive
      : handle.IsPolicyPaused ? SessionQueueState.Paused : SessionQueueState.Running;
    return new SessionQueueStatus(state, claim.QueueId, claim.QueueName, pausedByUs);
  }

  public QueuePauseOutcome TryPause(string? deviceSerial, out SessionQueueStatus status) {
    status = Inspect(deviceSerial);
    switch (status.State) {
      case SessionQueueState.NoQueue:
        return QueuePauseOutcome.NoQueue;
      case SessionQueueState.FiringActive:
        return QueuePauseOutcome.FiringActive;
      case SessionQueueState.Paused:
        return QueuePauseOutcome.AlreadyPaused;
    }

    if (!_runs.TryGet(status.QueueId!, out var handle)) {
      status = new SessionQueueStatus(SessionQueueState.NoQueue);
      return QueuePauseOutcome.NoQueue;
    }

    handle.EnterPolicyPause(PauseReason, _time.GetUtcNow());
    status = Inspect(deviceSerial);
    return QueuePauseOutcome.Paused;
  }

  public bool Resume(string queueId) {
    if (!_runs.TryGet(queueId, out var handle)) return false;
    if (!string.Equals(handle.PauseReason, PauseReason, StringComparison.Ordinal)) return false;
    return handle.ResumeFromPolicyPause();
  }

  private bool TryFindRun(string? deviceSerial, out DeviceClaim claim, out QueueRunHandle handle) {
    handle = null!;
    if (!_claims.TryGetHolder(deviceSerial, out claim)) return false;
    return _runs.TryGet(claim.QueueId, out handle);
  }
}

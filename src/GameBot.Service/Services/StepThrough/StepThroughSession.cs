using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Services.StepThrough;

namespace GameBot.Service.Services.StepThrough;

/// <summary>
/// One step-through in memory (data-model.md). <see cref="Gate"/> guards all fields. The stepper does not
/// run under the lock. It runs on a copy of <see cref="State"/>. The service adopts the copy under the
/// lock when the step ends.
/// </summary>
internal sealed class StepThroughSession {
  public required string Id { get; init; }

  public required string GameSessionId { get; init; }

  public string? DeviceSerial { get; init; }

  public required string SequenceId { get; init; }

  public required string SequenceName { get; set; }

  /// <summary>The stored sequence at the time of the start or the last restart.</summary>
  public required CommandSequence Sequence { get; set; }

  /// <summary>The hash of the stored sequence at the time of the start or the last restart.</summary>
  public required string Version { get; set; }

  /// <summary>The rows of the step list. They are stable for one version.</summary>
  public required IReadOnlyList<StepNode> Nodes { get; set; }

  public StepperState State { get; } = new();

  public object Gate { get; } = new();

  public bool Running { get; set; }

  public string? RunningPath { get; set; }

  public DateTimeOffset RunningStartedAt { get; set; }

  public CancellationTokenSource? RunCts { get; set; }

  public Task? RunTask { get; set; }

  /// <summary>The id of the queue that this step-through paused. Null when it paused no queue (FR-012b).</summary>
  public string? PausedQueueId { get; set; }

  public DateTimeOffset LeaseExpiresAt { get; set; }

  public bool Ended { get; set; }
}

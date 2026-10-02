using System;
using System.Collections.Generic;

namespace GameBot.Service.Services.StepThrough;

/// <summary>The error codes of the step-through API (contracts/step-through-api.md).</summary>
internal static class StepThroughErrorCodes {
  public const string SequenceNotFound = "sequence_not_found";
  public const string StepThroughNotFound = "step_through_not_found";
  public const string UnsupportedSequenceKind = "unsupported_sequence_kind";
  public const string SequenceEmpty = "sequence_empty";
  public const string UnknownStep = "unknown_step";
  public const string NotSelectable = "not_selectable";
  public const string UnknownParameter = "unknown_parameter";
  public const string SessionUnavailable = "session_unavailable";
  public const string SessionInUse = "session_in_use";
  public const string StepRunning = "step_running";
  public const string SequenceComplete = "sequence_complete";
  public const string SequenceChanged = "sequence_changed";
  public const string QueueRunning = "queue_running";
  public const string NoOwningQueue = "no_owning_queue";
  public const string QueueRunActive = "queue_run_active";
}

/// <summary>An error of a step-through call. The message names the cause and the next action.</summary>
/// <param name="Code">One of <see cref="StepThroughErrorCodes"/>.</param>
/// <param name="Message">The cause and the next action, in plain words.</param>
/// <param name="Details">Extra data for the caller, or null.</param>
internal sealed record StepThroughError(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null);

/// <summary>The result of a step-through call: a value or an error.</summary>
internal sealed class StepThroughResult<T> where T : class {
  private StepThroughResult(T? value, StepThroughError? error) {
    Value = value;
    Error = error;
  }

  public T? Value { get; }

  public StepThroughError? Error { get; }

  public bool IsSuccess => Error is null;

  public static StepThroughResult<T> Ok(T value) => new(value, null);

  public static StepThroughResult<T> Fail(string code, string message, IReadOnlyDictionary<string, object?>? details = null)
    => new(null, new StepThroughError(code, message, details));
}

/// <summary>The result of a cancel call: the state, and whether a step ran when the call came.</summary>
/// <param name="State">The state after the call.</param>
/// <param name="WasRunning">True when a step ran at the time of the call. The API answers 202 then, and 200 otherwise.</param>
internal sealed record CancelOutcome(StepThroughStateDto State, bool WasRunning);

/// <summary>The request to start a step-through.</summary>
internal sealed class StartStepThroughRequest {
  public string? SequenceId { get; init; }

  public string? GameSessionId { get; init; }

  public string? StartPath { get; init; }

  public Dictionary<string, string>? ParameterValues { get; init; }
}

/// <summary>The request to select the next step.</summary>
internal sealed class SelectStepRequest {
  public string? Path { get; init; }
}

/// <summary>The request to change the values of a step-through.</summary>
internal sealed class SetValuesRequest {
  public Dictionary<string, string>? ParameterValues { get; init; }

  public Dictionary<string, string?>? Outcomes { get; init; }
}

internal sealed class StepNodeDto {
  public required string Path { get; init; }

  public int Depth { get; init; }

  public string? StepId { get; init; }

  public required string Type { get; init; }

  public required string Label { get; init; }

  public bool Container { get; init; }

  public bool Selectable { get; init; }

  public string? Branch { get; init; }
}

internal sealed class RunningStepDto {
  public required string Path { get; init; }

  public DateTimeOffset StartedAt { get; init; }
}

internal sealed class HistoryEntryDto {
  public int Seq { get; init; }

  public required string Path { get; init; }

  public string? StepId { get; init; }

  public required string Kind { get; init; }

  public int? Iteration { get; init; }

  public required string Status { get; init; }

  public string? Outcome { get; init; }

  public string? Message { get; init; }

  public required IReadOnlyList<string> Effects { get; init; }

  public required IReadOnlyList<string> Notes { get; init; }

  public DateTimeOffset StartedAt { get; init; }

  public int DurationMs { get; init; }

  public string? ExecutionLogId { get; init; }
}

internal sealed class StepThroughParameterDto {
  public required string Name { get; init; }

  public string? Value { get; init; }

  public bool IsSet { get; init; }
}

internal sealed class StepThroughQueueDto {
  public required string QueueId { get; init; }

  public required string QueueName { get; init; }

  /// <summary>True while the queue runs and is not paused.</summary>
  public bool Running { get; init; }

  /// <summary>True while a firing of the queue runs now.</summary>
  public bool FiringActive { get; init; }

  public bool PausedByStepThrough { get; init; }

  public bool AlreadyPaused { get; init; }
}

internal sealed class StepThroughStateDto {
  public required string Id { get; init; }

  public required string SequenceId { get; init; }

  public required string SequenceName { get; init; }

  public required string GameSessionId { get; init; }

  /// <summary><c>idle</c>, <c>running</c>, or <c>complete</c>.</summary>
  public required string State { get; init; }

  public string? Cursor { get; init; }

  public required IReadOnlyList<StepNodeDto> Nodes { get; init; }

  public RunningStepDto? Running { get; init; }

  public required IReadOnlyList<HistoryEntryDto> History { get; init; }

  public required IReadOnlyList<StepThroughParameterDto> Parameters { get; init; }

  public required IReadOnlyDictionary<string, string> Outcomes { get; init; }

  public StepThroughQueueDto? Queue { get; init; }

  public DateTimeOffset LeaseExpiresAt { get; init; }
}

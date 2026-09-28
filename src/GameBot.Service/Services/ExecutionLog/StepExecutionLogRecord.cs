namespace GameBot.Service.Services.ExecutionLog;

/// <summary>
/// Feature 112 (issue #222): the data of one <c>POST /api/steps/execute</c> call for the execution log.
/// The command executor makes one record for each call that passes the session check.
/// </summary>
/// <param name="SessionId">The resolved session id.</param>
/// <param name="StepType">The name of the step type, for example <c>PrimitiveTap</c>.</param>
/// <param name="Outcome">The final step outcome. For a timeout, a cancellation or an error, the executor makes this outcome.</param>
/// <param name="Accepted">The count of inputs that the session accepted.</param>
/// <param name="StartedAtUtc">The time before the step started.</param>
/// <param name="DurationMs">The time that the step took, in milliseconds.</param>
internal sealed record StepExecutionLogRecord(
  string SessionId,
  string StepType,
  PrimitiveTapStepOutcome Outcome,
  int Accepted,
  DateTimeOffset StartedAtUtc,
  long DurationMs);

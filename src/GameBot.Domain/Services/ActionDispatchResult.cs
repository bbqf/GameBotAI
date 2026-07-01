namespace GameBot.Domain.Services;

/// <summary>
/// Result of dispatching a non-command sequence action (e.g. <c>reschedule-self</c>) through the
/// optional action dispatcher supplied to <see cref="SequenceRunner.ExecuteAsync"/> (feature 065).
/// The runner records this as the action step's result and continues — the action never, by
/// itself, terminates the sequence (FR-012).
/// </summary>
/// <param name="Outcome">Action outcome recorded on the step (e.g. <c>scheduled</c>, <c>noop</c>).</param>
/// <param name="Message">Optional human-readable detail for the execution log.</param>
public sealed record ActionDispatchResult(string Outcome, string? Message);

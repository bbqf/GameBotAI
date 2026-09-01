namespace GameBot.Domain.Services;

/// <summary>
/// Result of invoking a command from a sequence step through the optional command dispatcher
/// supplied to <see cref="SequenceRunner.ExecuteAsync"/>.
/// <para>
/// The runner previously had no way to see what a command actually did: the invoking callback
/// returned a bare <c>Task</c>, so a command that completed without throwing always recorded
/// <c>executed</c> on its step — even when the only thing it contained was an image-anchored tap
/// that never found its template and so sent nothing to the device. That made a failed recovery
/// guard indistinguishable in the log from a successful one.
/// </para>
/// </summary>
/// <param name="Dispatched">
/// <c>false</c> when the command completed but no input reached the device — every input-bearing
/// step in it was skipped (template not detected, unresolved parameter, invalid configuration).
/// Observational steps (waiting for an image) and lifecycle steps (ensuring the emulator or game
/// is running) are not input-bearing and never, on their own, make this false.
/// </param>
/// <param name="Reason">
/// Reason code from the first step that failed to dispatch (e.g. <c>detection_failed_after_3_retries</c>),
/// recorded on the sequence step so the miss is legible without opening the command's own subtree.
/// </param>
public sealed record CommandDispatchOutcome(bool Dispatched, string? Reason) {
  /// <summary>The ordinary case: the command ran and its input reached the device.</summary>
  public static readonly CommandDispatchOutcome Executed = new(true, null);
}

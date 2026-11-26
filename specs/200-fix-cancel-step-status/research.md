# Research: Give a reschedule-self Cancel step the status success when the cancel runs (B-046)

## R-001: Cause of the status `failure`

- **Decision**: The cause is `ExecutionLogService.MapStepStatus`. It has no case for the outcome `cancelled`, so `cancelled` goes to the default `_ => "failure"`.
- **Rationale**: `SequenceRunner.RecordServiceLevelActionAsync` records each `reschedule-self` step as `Succeeded` with `actionOutcome` = the dispatch outcome. `SequenceExecutionService.DispatchSelfReschedule` gives `cancelled` (bookings removed) or `noop` (nothing removed). `BuildSequenceStepOutcomes` stores `actionOutcome` as `ExecutionStepOutcome.Outcome`, and `BuildStepNode` calls `MapStepStatus(step.Outcome)`. `MapStepStatus` maps `scheduled` to `success` and `noop` to `skipped`, but has no `cancelled` case.
- **Alternatives considered**: A cause in the runner or in the sequence status. Rejected: the runner records `Succeeded`, and the sequence status is `success` in the issue evidence.

## R-002: Fix at read time, not at write time

- **Decision**: Change the status calculation in `ExecutionLogService` (read time), only for `reschedule-self` step nodes. Do not change `MapStepStatus`, and do not change the stored outcome.
- **Scope note (analyze C1)**: `MapStepStatus` is shared by all step types. `CommandExecutor` writes the outcome `cancelled` for a primitive or WaitForImage step that a stop cuts off (`CommandExecutor.cs:272`, `:639`), and `ExecutionLogService.cs:143` keeps that status as the outcome of a `waitForImage` step. A global `cancelled` → `success` row would show such a step as `success`. Thus the new rule checks `step.StepType` = `reschedule-self` first.
- **Rationale**: The status of a step node is not stored. It is calculated in `BuildStepNode` each time the subtree is read. A read-time fix gives the correct status to the 165 stored `Cancel` steps also (FR-009). The message text starts with the outcome ("... cancelled: ..."), so a new outcome name would change the message (FR-006).
- **Alternatives considered**: A new outcome name for Cancel (for example `nothing_to_cancel`). Rejected: it changes the message, `actionOutcome`, and `reasonCode`, and old logs keep the wrong status.

## R-003: How to know that a `noop` node is a `Cancel` step

- **Decision**: Add a private helper `ReadRescheduleOption(ExecutionLogEntry entry, int stepOrder)` in `ExecutionLogService`. It finds the detail item of the step. That item has `Kind` `step`, `stepType` `reschedule-self`, and the same `stepOrder`. The helper gives the `option` attribute of the item as a string. Add a private helper `ResolveStepStatus(ExecutionLogEntry entry, ExecutionStepOutcome step)`, which `BuildStepNode` calls in place of `MapStepStatus`. For a `reschedule-self` step it gives `success` for the outcome `cancelled`. It also gives `success` for the outcome `noop` when `ReadRescheduleOption` gives `Cancel` (ignore case). In all other cases the status stays `MapStepStatus(step.Outcome)`.
- **Rationale**: The stored `ExecutionStepOutcome` has no option field, but the stored detail item of the step has `option` since feature 065 (`SequenceExecutionService`, `rescheduleDetail["option"]`). `ReadStepParameters` already reads the detail items by `stepOrder`, so the pattern is known. The attributes are plain objects in memory and `JsonElement` values after a read from disk, so the helper reads them through `JsonSerializer.SerializeToElement`, as `ReadStepParameters` does.
- **Alternatives considered**: (a) A new `RescheduleOption` property on `ExecutionStepOutcome`. Rejected: the stored logs do not have it, so old `noop` Cancel nodes stay `skipped`; it also changes the stored format. (b) Use the `removed` attribute (only Cancel has it). Rejected: `option` is the direct fact, and `removed` is also not on `ExecutionStepOutcome`.

## R-004: An error during the cancel

- **Decision**: In `DispatchSelfReschedule`, put the call `_selfRescheduleCoordinator.CancelSelf(...)` in `try`/`catch (Exception ex)`. On an error, return `new ActionDispatchResult("failed", $"cancel not performed: {ex.Message}", false)`. Write a warning to the service log with the sequence id, the queue id, and the exception. Use a `[LoggerMessage]` method in the static class `SequenceExecutionLog` at the end of `SequenceExecutionService.cs` (next to `AbandonedFinalizeFailed`, EventId 1130), and call it as `SequenceExecutionLog.<Name>(_logger, ...)`. Use an EventId that no other `LoggerMessage` in `src` uses (search for `EventId = <n>` first). The logger is optional (`_logger` can be null). Do not catch `OperationCanceledException` separately: `CancelSelf` is synchronous and takes no token.
- **Rationale**: Today an error propagates out of the dispatcher, `ExecuteAsync` calls `FinalizeAbandonedAsync` (status `failure`) and throws again, and the run has no step node for the step. The spec (FR-004, FR-005, clarification 2) needs a step node with the status `failure` and a run that continues. `RecordServiceLevelActionAsync` stops the run only for a `WaitForStill` `failed` outcome, so a `reschedule-self` `failed` outcome is recorded as a step and the run continues. `MapStepStatus("failed")` already gives `failure`. `stepOutcomes` keeps `success` for a `reschedule-self` step (FR-010), with no runner change.
- **Alternatives considered**: Catch the error in the runner. Rejected: the runner catch would apply to all service-level actions, which is out of scope.

## R-005: Other places that show a step status

- **Decision**: No other change.
- **Rationale**: The web UI shows `node.status` from the subtree endpoint. The detail projection (`BuildDetailProjection`) gives the raw `Outcome`, not a status. The child-entry branch of `BuildTreeNode` (line 645) is only for command steps with a child entry; a `reschedule-self` step has no child entry. The step-through preview does not call `CancelSelf`.
- **Alternatives considered**: None.

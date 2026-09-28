# Data Model: Truthful tap outcome and an execution log for a single step

## PrimitiveTapStepOutcome (existing record, new values)

No new fields. New values for `Status` and `Reason`:

| Status | Reason | `accepted` | Points | Description |
|--------|--------|-----------|--------|---------|
| `executed` | `executed_then_error` | count from the session | resolved point; executed point and confidence when known | The session accepted the input. An error occurred after that. |
| `executed` | `executed_then_cancelled` | count from the session | resolved point; executed point and confidence when known | The session accepted the input. A cancellation occurred after that. |
| `dispatch_unknown` | `dispatch_error` | 0 | resolved point | The session threw an error during the dispatch. The device can have the input. |
| `dispatch_unknown` | `dispatch_cancelled` | 0 | resolved point | The dispatch was cancelled. The device can have the input. |

The current values stay. `skipped_detection_failed`, `skipped_invalid_config` and `cancelled` now occur only when the service did not start a dispatch.

## TapDispatchState (new, internal to `CommandExecutor`)

| Field | Type | Rule |
|-------|------|------|
| `Started` | bool | Set to true immediately before the call to `SendInputsAsync`. |
| `Completed` | bool | Set to true when `SendInputsAsync` returned. |
| `Accepted` | int | The count that `SendInputsAsync` returned. |
| `ResolvedPoint` | `PrimitiveTapResolvedPoint?` | The calculated point before the jitter. |
| `ExecutedPoint` | `PrimitiveTapResolvedPoint?` | The point after the jitter, when the read-back succeeded. |
| `DetectionConfidence` | double? | Read before the dispatch. |
| `HoldMs` | int? | The same value as in the `executed` outcome of today. |

State transitions: not started → started → completed. The `catch` blocks of `ExecuteOneStepAsync` read the state.

## StepExecutionLogRecord (new, `GameBot.Service.Services.ExecutionLog`)

| Field | Type | Source |
|-------|------|--------|
| `SessionId` | string | The resolved session id. |
| `StepType` | string | `CommandStepType` name, for example `PrimitiveTap`. |
| `Outcome` | `PrimitiveTapStepOutcome` | The final outcome, or a made outcome for `timeout`, `cancelled` and `failed`. |
| `Accepted` | int | The `accepted` count of the call. 0 for `timeout`, `cancelled` and `failed`. |
| `StartedAtUtc` | DateTimeOffset | The time before the step started. |
| `DurationMs` | long | The time that the step took. |

## ExecutionLogEntry for a single step call (existing model, new values)

| Field | Value |
|-------|-------|
| `ExecutionType` | `step` |
| `ObjectRef` | `ObjectType = "step"`, `ObjectId = <SessionId>`, `DisplayNameSnapshot = "<StepType> step"` |
| `FinalStatus` | `success` when the status is `executed`, else `failure` |
| `Summary` | `Step '<StepType>' on session '<SessionId>' ended with <status>.` plus ` Reason: <reason>.` when a reason exists |
| `StepOutcomes` | One `ExecutionStepOutcome`: order, step type (camel case, for example `primitiveTap`), outcome (`executed`, `dispatch_unknown`, `timeout` or `not_executed`), reason code = status, reason text = reason |
| `Details` | One item, kind `step`, with the attributes `sessionId`, `stepType`, `status`, `reason`, `resolvedX`, `resolvedY`, `executedX`, `executedY`, `detectionConfidence`, `accepted`, `startedAtUtc`, `durationMs` |
| `Hierarchy` | Root (no parent, depth 0) |
| `TimestampUtc` | The time of the write |

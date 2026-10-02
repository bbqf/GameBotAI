# Contract: Step-Through API

Base route: `/api/step-through`. All bodies are JSON. Property names use camelCase.
Errors use the error envelope of the service: `{ "error": { "code": "...", "message": "...", "hint": null, "details": { } } }`.
Each message names the cause and a next action (Constitution III).

## POST /api/step-through

Starts a step-through run. Does not run a step.

Request:

```json
{ "sequenceId": "string", "gameSessionId": "string", "startPath": "2",
  "parameterValues": { "name": "value" } }
```

`startPath` and `parameterValues` are optional (FR-013a, FR-017).

Response `201`: a `StepThroughState` (see below).

Errors: `404 sequence_not_found`, `400 unsupported_sequence_kind`, `400 sequence_empty`,
`400 unknown_step`, `400 not_selectable` (a loop or an if step cannot be the first step),
`400 unknown_parameter` (a name that the sequence does not declare), `409 session_unavailable`,
`409 session_in_use`.
A running queue does not block the start. It blocks `run-next` (FR-012).

## GET /api/step-through/{id}

Returns the `StepThroughState`. Renews the lease (90 seconds).
Errors: `404 step_through_not_found` (the session ended or the lease expired).

## POST /api/step-through/{id}/run-next

Runs the step at `cursor`. Returns `202` at once. The step runs in the background.
Request body: none.

Errors:
- `409 step_running` — another step runs.
- `409 sequence_complete` — `cursor` is null. The author must select a step first (FR-010).
- `409 sequence_changed` — the stored sequence changed (FR-013). The author must restart.
- `409 queue_running` — a queue run is active on the device. `details` has `queueId`, `queueName`,
  `canPause` (FR-012, FR-012c).
- `409 session_unavailable`.

## POST /api/step-through/{id}/select

Sets the next step manually (FR-007).

Request: `{ "path": "1/body/0" }`. Response `200`: `StepThroughState`.
Errors: `400 unknown_step`, `400 not_selectable` (a loop or an if header row), `409 step_running`,
`409 sequence_changed`.

## POST /api/step-through/{id}/cancel

Cancels the running step (FR-011). Response `202` with the state when a step ran at the time of the call.
A no-op with `200` when no step ran. The history gets an entry with status `Cancelled` when the step stops.
The cursor stays on the cancelled step.

## POST /api/step-through/{id}/restart

Clears history, frames, and outcomes. Sets `cursor` to the first step (FR-009).
Keeps `parameterValues` and the queue pause. Errors: `409 step_running`.

## PUT /api/step-through/{id}/values

Replaces the author values (FR-017). Keeps history and cursor.

Request: `{ "parameterValues": { }, "outcomes": { "stepId": "success" } }`.
`parameterValues` replaces all author values. Each item of `outcomes` sets the outcome of that step. An empty or
null value makes the outcome "not set". An outcome that is not in the request does not change.
Errors: `409 step_running`, `400 unknown_parameter`, `400 unknown_step` (an outcome for a step id that the
sequence does not have).

## POST /api/step-through/{id}/pause-queue

Pauses the queue that owns the device (FR-012a). Response `200`: `StepThroughState`
with `queue.pausedByStepThrough = true`.
Errors: `409 no_owning_queue`, `409 queue_run_active` (a firing runs now, FR-012c).

## DELETE /api/step-through/{id}

Ends the step-through run. Cancels a running step. Resumes the queue when `pausedByStepThrough` is true (FR-012b).
Response `204`. Idempotent: an unknown id also gives `204`.

## StepThroughState

```json
{
  "id": "string",
  "sequenceId": "string",
  "sequenceName": "string",
  "gameSessionId": "string",
  "state": "idle | running | complete",
  "cursor": "1/body/0",
  "nodes": [
    { "path": "0", "depth": 0, "stepId": "s1", "type": "action", "label": "tap 100,200",
      "container": false, "selectable": true, "branch": null }
  ],
  "running": { "path": "1/body/0", "startedAt": "2026-10-02T10:00:00Z" },
  "history": [
    { "seq": 1, "path": "0", "stepId": "s1", "kind": "step", "iteration": null,
      "status": "Succeeded", "outcome": "executed", "message": null,
      "effects": [], "notes": [], "startedAt": "2026-10-02T10:00:00Z", "durationMs": 120,
      "executionLogId": "string" }
  ],
  "parameters": [ { "name": "n", "value": "1", "isSet": true } ],
  "outcomes": { "s1": "success" },
  "queue": { "queueId": "string", "queueName": "string", "running": true, "firingActive": false,
             "pausedByStepThrough": false, "alreadyPaused": false },
  "leaseExpiresAt": "2026-10-02T10:01:30Z"
}
```

`nodes` is stable for one version of the sequence. The view can cache it. A node also has `branch`: `body`,
`else`, or null at the top level. `cursor` can be a loop or an if step (a header row). The step-through then
evaluates it when the next step runs. `history[].kind` is `step`, `enter` (the decision of a loop or an if
step), or `exit` (a loop ended). `queue` is null when no queue owns the device. `queue.running` is true while
the queue runs and is not paused. `queue.firingActive` is true while a firing of the queue runs now.
`history` is ordered by `seq` ascending. A request with `?afterSeq=N` returns only newer entries.

## Execution log change

Entries written by a step-through have `origin: "step-through"` on the log DTO (FR-016).
The list endpoint accepts `?origin=step-through` as a filter. Entries without the field stay valid.

## Command executor change (internal)

`ICommandExecutor.ForceExecuteDetailedAsync` gets an optional argument `ExecutionOptions? options`.
`options.PreviewEffects = true` makes the executor skip `reschedule-self` and `notify` actions.
`CommandForceExecutionResult` gets `PreviewedEffects: IReadOnlyList<string>`.
The default (null) keeps the old behavior for all current callers.

## Contract tests

1. Every route above returns the documented status for each documented error code.
2. Every sequence action type is listed as `runs` or `previews` (guards a new type, see memory note on action-type sites).
3. A step-through of reschedule-self changes no queue schedule (SC-006).
4. A change of the stored sequence after the start gives `409 sequence_changed` (SC-005 partner test).

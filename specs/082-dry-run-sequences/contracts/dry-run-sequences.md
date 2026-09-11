# Contract: `dryRun` on Sequence Create and Execute

This feature adds **no new HTTP endpoint**. It adds one optional request field
to two existing endpoints and one new outcome value to an existing response
field. Both changes are additive: every request/response shape that is valid
today remains valid and unchanged in meaning when `dryRun` is omitted or
`false`.

## `POST /api/sequences` — `dryRun` on create (per-step request shape only)

**Request** gains an optional top-level field:

```json
{
  "name": "candidate-sequence",
  "steps": [ /* unchanged shape */ ],
  "dryRun": true
}
```

| Field | Type | Default | Meaning |
|---|---|---|---|
| `dryRun` | `boolean` | `false` | When `true`, run the same enrichment (`commandId` → `commandName` resolution) and validation (structural constraints, `commandId`/image reference resolution) as a real create, but do not persist a sequence. |

Only recognized on the **per-step** request shape (a body with a top-level
`steps` array of step objects, as validated by `SequenceStepValidationService`).
The two legacy create shapes (`{name, steps: string[]}` and the raw domain
fallback) are unaffected — `dryRun` has no effect there because neither runs
structural validation today.

**Success response** (`200 OK`, replacing the usual `201 Created` + persisted
sequence body — nothing is created):

```json
{ "valid": true, "dryRun": true, "errors": [] }
```

**Failure response** — byte-for-byte the same shape and status a non-dry-run
create returns for the same invalid body:

```json
{ "message": "Invalid sequence payload", "errors": ["..."] }
```

(`400 Bad Request`.) A caller cannot distinguish a dry-run validation failure
from a real one by shape — only by the fact that a dry-run request never
produces a `Location` header or a persisted `id`, dry-run or not, since a
dry-run success never returns one either.

## `POST /api/sequences/{sequenceId}/execute` — `dryRun` on execute

**Request** gains an optional field on the existing body:

```json
{
  "sessionId": null,
  "parameters": [ /* unchanged */ ],
  "dryRun": true
}
```

| Field | Type | Default | Meaning |
|---|---|---|---|
| `dryRun` | `boolean` | `false` | When `true`, execute the sequence's real step tree, but skip every step that would dispatch input to the emulator, start/use a session, or read live screen-capture state. |

`sessionId` remains optional exactly as today; when `dryRun` is `true` it is
never required to resolve to a running session, because dispatch-capable steps
never reach session resolution.

**Response** — the same `SequenceExecutionResult` shape (`200 OK`) a real
execution returns: top-level `status`, `steps[]`, `blocks[]`,
`conditionTraces[]`. No new top-level field. Only the per-step `actionOutcome`
value changes for steps the dry-run skipped:

```json
{
  "status": "Succeeded",
  "steps": [
    {
      "stepId": "tap-start",
      "status": "Succeeded",
      "actionOutcome": "skipped_dry_run",
      "message": "dry run: 'tap' was not dispatched"
    },
    {
      "stepId": "collect-loop",
      "status": "Succeeded",
      "loopIterations": [ "..." ],
      "exitReason": { "brokeVia": "cluster-not-found-break", "exhaustedMaxIterations": false }
    }
  ]
}
```

A `Loop`/`If`/`Break` step's own control-flow fields (`loopIterations`,
`exitReason`, branch selection, `break`/`no_break`) are computed exactly as in
a real run — dry-run does not touch control-flow evaluation, only the leaf
dispatch beneath it.

### New `actionOutcome` value: `skipped_dry_run`

Reported for **every** step a dry-run execution intercepts before dispatch:
primitive tap/swipe/key, connect-to-game, ensure-game-running,
go-to-home-screen, ensure-emulator-running, wait-for-image, reschedule-self,
and any command-referencing step. `status` for such a step is `"Succeeded"` —
an intentional, successful no-op, never a failure — even when the step is
authored with `requireDispatch: true` (dry-run skipping a step is not "nothing
dispatched," so the `requireDispatch` miss-check never applies to it).

### Condition evaluation under `dryRun`

- A `commandOutcome` condition (per-step gate, `If` branch selector, or `Loop`
  `breakOn`) evaluates for real against the in-memory outcome of steps that
  have already run this dry-run pass — including a `skipped_dry_run` outcome,
  which never equals `success`/`failed`/`skipped`/`break`/`no_break`, so any
  reference to a dry-run-skipped step's outcome resolves to `false`.
- An `imageVisible`-sourced condition (per-step gate, `If` condition, or
  `Loop` `breakOn`) never reads live capture state under `dryRun` — it
  resolves to `false` (the same outcome as "condition evaluated false" today),
  regardless of whether a session is supplied. This applies even when the
  referenced image no longer exists: unlike a stale `commandId` below, a
  stale image reference is **not** specially detected under `dryRun` — it
  resolves the same neutral `false` any other image condition does.

### `commandId` reference-resolution failures are still real errors

A step referencing a `commandId` that cannot be resolved to an existing
command (e.g. the referenced command was deleted after the sequence was
created) still fails the step and the run with the existing error, unchanged
by `dryRun` — checked directly against the command repository, without
dispatching to the command:

```json
{ "status": "Failed", "message": "Command 'stale-id' was not found; the sequence step references a missing command." }
```

A `commandId` that **does** resolve is skipped exactly like any other
dispatch-capable step (`actionOutcome: "skipped_dry_run"`), even when the step
is authored with `requireDispatch: true` — a resolvable-but-dry-run-skipped
command is never treated as a dispatch miss.

## Invariants (MUST hold — asserted by tests)

1. Every create/execute request that omits `dryRun` (or sets it `false`)
   behaves byte-for-byte as it does today.
2. `dryRun: true` on create never calls `ISequenceRepository.CreateAsync` (or
   any other persistence write) and never returns a persisted `id`.
3. `dryRun: true` on execute never calls `ISessionManager.SendInputsAsync`,
   `ISessionService.StartSession`, or `ICommandExecutor.ForceExecuteDetailedAsync`.
4. `dryRun: true` on execute succeeds (`status: "Succeeded"` when no other step
   fails) even when zero emulator sessions exist.
5. A `Loop` step's `exitReason` and a `Break` step's `break`/`no_break`
   outcome, computed under `dryRun: true`, match what a real run with
   equivalent structural inputs (parameters, referenced-step outcomes) would
   report.
6. `dryRun: true` never causes a `requireDispatch: true` step to fail.
7. Queue-scheduled execution (`QueueExecutionService`) always runs with
   `dryRun` effectively `false` — the option is unreachable from a queue run.

## Out of scope (explicitly unchanged)

- Persisted sequence-definition JSON schema — no field added or removed.
- `POST /api/sequences/{sequenceId}/validate` (the existing flow-graph
  validate endpoint) — untouched; it already re-validates a persisted
  sequence and is a different code path from create-time per-step validation.
- Queue execution and its request/response shapes.
- `CommandExecutor`'s own `ForceExecuteDetailedAsync` contract — unreachable,
  not modified.

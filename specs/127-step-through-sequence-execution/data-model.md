# Data Model: Step-Through Sequence Execution

All state is in memory. The service stores nothing on disk, except the execution log entries (FR-016).

## StepThroughSession

| Field | Type | Notes |
|-------|------|-------|
| `id` | string (GUID) | Session id. |
| `sequenceId` | string | The saved sequence. |
| `sequenceVersion` | string | SHA-256 of the stored sequence JSON at start. |
| `gameSessionId` | string | The game session that receives the input. |
| `deviceSerial` | string? | Used to find the owning queue. |
| `cursor` | string? | Path of the next step. Null when complete (FR-010). |
| `frames` | Frame[] | Open loops and open if branches. See below. |
| `parameterValues` | map string→string | Author values for sequence parameters (FR-017). |
| `outcomes` | map string→string | Step outcomes that conditions read (FR-014). |
| `history` | HistoryEntry[] | Capped at 1,000. Oldest entry drops first. |
| `state` | enum | `idle`, `running`, `complete`. |
| `pausedQueueId` | string? | Set only when this session paused a queue (FR-012b). |
| `leaseExpiresAt` | timestamp | Renewed on each state read. 90 seconds. |
| `runningStepCts` | CancellationTokenSource? | Internal. Not in the API. |

**Rules**:
- One active session for each `gameSessionId`.
- `state = running` means no `select`, `run-next`, or `values` call is accepted (`409 step_running`).
- `Restart` clears `history`, `frames`, `outcomes`, and sets `cursor` to the first step.
  It keeps `parameterValues` and the queue pause.

## Frame

| Field | Type | Notes |
|-------|------|-------|
| `ownerPath` | string | Path of the loop or if step. |
| `kind` | enum | `loop-count`, `loop-while`, `loop-repeat-until`, `if-then`, `if-else`. |
| `iteration` | int | 1-based. Loops only. |
| `limit` | int? | Count for `loop-count`. Null for other kinds. |

Transitions:
- Run the last body step of a loop frame: evaluate the loop rule. Either increment `iteration`
  and set `cursor` to the first body step, or pop the frame and move to the next sibling of the owner.
- Run the last step of an if branch: pop the frame and move to the next sibling of the owner.
- A break that fires pops the nearest loop frame.
- A manual `select` into a body with no frame opens frames for all parents at iteration 1.

## StepNode (read model)

| Field | Type | Notes |
|-------|------|-------|
| `path` | string | Example: `1/body/0`, `3/else/1`. |
| `depth` | int | 0 for top level. |
| `stepId` | string? | From the sequence. |
| `type` | string | `action`, `command`, `loop`, `if`, `break`, `wait-for-image`. |
| `label` | string | Human text for the list. |
| `container` | bool | True for a loop or an if step. A container is a header row. |
| `selectable` | bool | False for `loop` and `if` header rows. Their first body action is the first step. |

A loop or an if step is not a step that runs as one unit (FR-004a).
The stepper still evaluates its guard or condition when the cursor enters it.
That evaluation writes its own history entry (kind `enter`).

## HistoryEntry

| Field | Type | Notes |
|-------|------|-------|
| `seq` | int | Increases by 1 for each entry. |
| `path` | string | Step path. |
| `stepId` | string? | |
| `kind` | enum | `step`, `enter` (loop or if entry decision), `exit` (loop end). |
| `iteration` | int? | Iteration number for a step inside a loop (FR-004a). |
| `status` | string | `Succeeded`, `Failed`, `Skipped`, `Cancelled`. Same words as the execution log. |
| `outcome` | string? | For example `executed`, `not_executed`, `break`, `no_break`. |
| `message` | string? | Result text. |
| `effects` | string[] | Previewed effects, for example `would reschedule at 14:30`. |
| `notes` | string[] | For example `lastRun is not evaluated`, `sequence would end here`. |
| `startedAt` | timestamp | |
| `durationMs` | int | |
| `executionLogId` | string? | Link to the execution log entry. |

## Relations

- A `StepThroughSession` has one stored `CommandSequence` (read only).
- A `StepThroughSession` has zero or one owning queue (`pausedQueueId`).
- A `HistoryEntry` has zero or one execution log entry.

## Validation rules

| Rule | Source | Error |
|------|--------|-------|
| The sequence exists | FR-002 | `404 sequence_not_found` |
| The sequence is a step-list sequence | R11 | `400 unsupported_sequence_kind` |
| The sequence has at least one step | Edge case | `400 sequence_empty` |
| The game session exists and is connected | Edge case | `409 session_unavailable` |
| No other step-through uses the session | R14 | `409 session_in_use` |
| No queue run is active on the device | FR-012 | `409 queue_running`, with `queueId` and `queueName` |
| The stored version equals `sequenceVersion` | FR-013 | `409 sequence_changed` |
| `path` exists in the node list | FR-007 | `400 unknown_step` |

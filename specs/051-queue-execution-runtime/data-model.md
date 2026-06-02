# Phase 1 Data Model: Queue Execution Runtime

This feature adds **runtime** orchestration state and a new execution-log entry kind. It does not add new persisted configuration (queue config, template, and queue→template link already persist; runtime status and entries remain non-persistent by design).

## New / changed types

### QueueStopReason (new domain enum — `GameBot.Domain/Queues/QueueStopReason.cs`)

| Value | Meaning | Maps to execution-log `FinalStatus` |
|-------|---------|--------------------------------------|
| `CompletedFullRun` | Ran all sequences once (cycle off), or empty template | `success` |
| `StoppedManually` | Operator stopped via UI/API (incl. while cycling) | `success` (summary states "stopped manually") |
| `Failure` | Run-level failure: no resolvable template, emulator unreachable at start, or connection lost mid-run | `failure` |

> Per-sequence failures do **not** map to a stop reason; they are recorded on the sequence's own child entry and the run continues (FR-008).

### QueueRunHandle (new, internal to `QueueExecutionService`)

In-memory, one per currently-running queue. Not persisted.

| Field | Type | Notes |
|-------|------|-------|
| `QueueId` | `string` | Key in the run registry. |
| `Cts` | `CancellationTokenSource` | Cancelled by `StopAsync`; linked to host shutdown. |
| `RunTask` | `Task` | The background orchestration task. |
| `RootExecutionId` | `string` | The queue-run execution-log root id (for correlation). |
| `SessionId` | `string?` | The emulator session opened for this run (null until connected). |
| `StartedAtUtc` | `DateTimeOffset` | For diagnostics/summary. |

### QueueRunResult (new, internal)

Outcome of a run, used to build the finalize log entry.

| Field | Type | Notes |
|-------|------|-------|
| `StopReason` | `QueueStopReason` | See table above. |
| `SequencesExecuted` | `int` | Total sequence executions across all cycles. |
| `SequencesFailed` | `int` | Count of non-fatal per-sequence failures. |
| `Cycles` | `int` | Completed full passes (≥1 when cycle off and not failed at start). |
| `FailureReason` | `string?` | User-facing text when `StopReason == Failure`. |

### Loaded template snapshot (in-memory, per run)

An ordered `IReadOnlyList<string sequenceId>` resolved from the linked template at run start (FR-002/FR-015). Reused for every cycle; never re-resolved mid-run.

## Changed: ExecutionLogService

New methods (mirroring the existing sequence start/finalize pattern):

- `Task<string> LogQueueStartAsync(string queueId, string queueName, CancellationToken ct)` — creates a `running` root with `ExecutionType = "queue"`, `ObjectRef = ("queue", queueId, queueName)`, hierarchy root (parent null, depth 0). Returns the root execution id.
- `Task LogQueueFinalizeAsync(string rootId, string queueId, string queueName, string finalStatus, string summary, QueueStopReason stopReason, IReadOnlyList<ExecutionDetailItem>? details, CancellationToken ct)` — upserts the terminal queue root (preserving root hierarchy and original timestamp), summary carries the stop reason.

New/extended **nestable sequence** entry points so a sequence can be a child of the queue run:

- `LogSequenceStartAsync(string sequenceId, string sequenceName, ExecutionLogContext parentContext, CancellationToken ct)` — like the existing overload but sets hierarchy `ParentExecutionId`/`RootExecutionId`/`SequenceIndex` from `parentContext` instead of forcing a root.
- `LogSequenceFinalizeAsync(...)` — preserve the entry's existing parent hierarchy on upsert (today it hardcodes parent = null; change to read the stored entry's hierarchy or accept it via `parentContext`).

> No change to `ExecutionLogEntry` shape, the repository, retention, or the subtree/grid builders. `BuildTreeNode` already nests `directChildren` for a parent without `StepOutcomes` (the queue root), so sequence children render correctly.

## Entity relationships (runtime)

```
ExecutionQueue (persisted: id, name, emulatorSerial, cycleExecution, linkedTemplateId)
   │ 1
   │ linkedTemplateId (0..1, persisted)
   ▼
QueueTemplate (persisted: ordered sequence entries)
   │ resolved once at run start → snapshot (in-memory)
   ▼
QueueRunHandle (in-memory, 1 per running queue)  ──opens──►  EmulatorSession (ISessionManager, bound serial)
   │
   ▼ produces
ExecutionLogEntry [type=queue]  (root)
   └── ExecutionLogEntry [type=sequence] (child, per executed sequence, ordered by SequenceIndex)
          └── ExecutionLogEntry [type=command] / step nodes (existing nesting)
```

## State transitions (queue runtime status)

`QueueExecutionStatus` is unchanged (`Stopped` / `Running`).

```
Stopped ──StartAsync (handle created)──► Running
Running ──run ends (completed | stopped | failure)──► Stopped   (handle removed, session disconnected, finalize logged)
Running ──StartAsync again──► (rejected: 409 already_running; stays Running)
Stopped ──StopAsync──► (no-op; stays Stopped)
service restart ──► Stopped (runtime not persisted)
```

## Validation rules

- Start requires the queue to exist (else 404) and not already be running (else 409 `already_running`).
- A run with no resolvable linked template ends immediately with `Failure` + a "no template to run" log entry (FR-002) — it does **not** 400 the start call; the start launches and the run records the failure.
- Emulator unreachable at connect → `Failure` + "emulator could not be reached" (FR-004); zero sequences executed.
- Every run writes exactly one terminating queue-run entry (FR-009, SC-005) and disconnects its session (FR-023, SC-006).

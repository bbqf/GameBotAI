# Phase 1 Data Model: Queue Cycle Observability

**Feature**: 086-queue-cycle-observability | **Date**: 2026-09-14

Nothing here is persisted. Every type below is in-memory, run-scoped, and discarded with the
`QueueRunHandle` when the run ends. No repository, no file format, no migration.

## Runtime types (`src/GameBot.Service/Services/QueueExecution/QueueCycleLedger.cs`)

### `QueueCycleLedger`

The single owner of a run's cycle observability state. One instance per `QueueRunHandle`, created with
it. All state is guarded by one private lock.

| State | Type | Meaning |
|---|---|---|
| `_open` | `OpenCycle?` | The cycle currently accumulating, or null between cycles |
| `_completed` | `Queue<QueueCycleRecord>` | Completed cycles, oldest first, trimmed to `MaxRetainedCycles` |
| `_cyclesCompleted` | `int` | Monotonic count for the run; keeps counting past the ring bound |
| `_consecutiveFailedCycles` | `int` | Failed cycles at the tail; reset to 0 by a success |
| `_currentEntryIndex` | `int?` | Roster position being executed, or null outside the roster pass |

`MaxRetainedCycles = 50`.

**Mutators** (all `void`, all called only by the run loop, none throwing an error the caller must act
on — this is what makes the ledger a pure observer per FR-020):

| Method | Called at | Behaviour |
|---|---|---|
| `EnsureOpen(now)` | Top of each run-loop iteration | Opens a cycle if none is open; otherwise a no-op |
| `RecordEntry(sequenceId, succeeded)` | After each `RunOneSequenceAsync` result | Appends to the open cycle; no-op if none is open |
| `SetCurrentEntryIndex(index)` / `ClearCurrentEntryIndex()` | Around each roster-pass entry | Sets/clears the roster position |
| `CompleteOpen(now)` | At the existing `cycles++` | Seals the open cycle into a record, updates counters, trims the ring; no-op if none is open |
| `RecordEmptyCycle(now)` | The empty-template branch | Records one completed cycle with no entries |

**Readers** (called only by the projection, each returning a copy taken under the lock):

| Method | Returns |
|---|---|
| `SnapshotHealth()` | `QueueCycleHealth` |
| `SnapshotCycles(limit)` | `IReadOnlyList<QueueCycleRecord>`, newest first, at most `limit` |

### `OpenCycle` (private)

`StartedAt` plus a mutable `List<QueueCycleEntryOutcome>`. Never escapes the ledger and is never
published — an open cycle that never completes (a stop mid-cycle, or a non-cycling run's trailing poll
iterations) is simply dropped.

### `QueueCycleRecord`

One completed cycle.

| Field | Type | Notes |
|---|---|---|
| `Ordinal` | `int` | 1-based within the run; equals `_cyclesCompleted` after increment |
| `StartedAt` | `DateTimeOffset` | Local clock, from `_timeProvider`, matching the rest of the run loop |
| `CompletedAt` | `DateTimeOffset` | |
| `Succeeded` | `bool` | **Derived**: false iff any entry failed. A cycle with no entries succeeds |
| `Entries` | `IReadOnlyList<QueueCycleEntryOutcome>` | In execution order |

### `QueueCycleEntryOutcome`

`SequenceId` + `Succeeded`. The sequence's display name is resolved at projection time, not stored, so
the ledger never holds a stale name and never needs a repository.

### `QueueCycleHealth`

A value snapshot, not live state — safe to hold after the lock is released.

`CyclesCompleted`, `LastCycleStartedAt?`, `LastCycleCompletedAt?`, `LastCycleSucceeded?`,
`ConsecutiveFailedCycles`, `CurrentEntryIndex?`.

The three nullable last-cycle fields are all null together, exactly when no cycle has completed yet.

## Response contracts (`src/GameBot.Service/Contracts/Queues/`)

### `QueueHealthResponse` (new)

Attached as `QueueDetailResponse.Health`, **nullable, null when the queue is not running** (FR-008) —
a stopped queue has no health block at all rather than a zeroed one.

Populated only when the liveness gate holds: `IQueueRuntimeStore.GetStatus(id) == Running` **and**
`IQueueRunRegistry.TryGet` yields a handle (FR-008a, research R8). The same gate decides
`QueueCyclesResponse.Running`, so the two endpoints can never disagree.

| Field | Type | Source |
|---|---|---|
| `cyclesCompleted` | `int` | `QueueCycleHealth` |
| `lastCycleStartedAt` | `DateTimeOffset?` | |
| `lastCycleCompletedAt` | `DateTimeOffset?` | |
| `lastCycleStatus` | `string?` | `"success"` / `"failure"`; null before the first completed cycle |
| `consecutiveFailedCycles` | `int` | |
| `currentEntryIndex` | `int?` | Null outside the roster pass |
| `currentSequenceId` | `string?` | From the handle's existing `CurrentSequenceId` |
| `runStartedAt` | `DateTimeOffset?` | From the handle's existing `RunStartedAt` |

`lastCycleStatus` uses the `"success"`/`"failure"` spelling the execution log already uses for run
status, rather than a new boolean vocabulary (Constitution III).

### `QueueCyclesResponse` (new)

Body of `GET /api/queues/{id}/cycles`.

| Field | Type | Notes |
|---|---|---|
| `queueId` | `string` | |
| `running` | `bool` | The liveness gate above; false for a known queue with no active run |
| `cycles` | `Collection<QueueCycleResponse>` | Newest first; empty when not running |

### `QueueCycleResponse` / `QueueCycleEntryResponse` (new)

`QueueCycleResponse`: `ordinal`, `startedAt`, `completedAt`, `status` (`"success"`/`"failure"`),
`entries`.

`QueueCycleEntryResponse`: `sequenceId`, `sequenceName` (resolved at projection time; null when the
sequence no longer exists), `status`.

## Unchanged

- `QueueExecutionStatus` — still `Stopped | Running` (FR-017).
- `QueueResponse` — the list projection gains nothing (Clarifications).
- `ExecutionQueue`, `QueueEntry`, `QueueTemplate` and every persisted shape.
- Every execution-log record type and its contents (FR-018).

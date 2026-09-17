# Data Model: A cycling queue with nothing due waits instead of spinning empty cycles

No persisted data changes. The only model touched is the in-memory, run-scoped cycle ledger.

## Loop iteration (run-local, not persisted)

| Field | Meaning |
|-------|---------|
| `index` at iteration start | Snapshot of the run's sequence-firing counter |
| ran work | `index` advanced during the iteration (any sequence firing, including every-step passes) |

State rule for a **cycling** run:

- **ran work** → counts one completed cycle (as today) → next iteration starts at once.
- **no work** → no cycle counted, open ledger cycle discarded → wait (idle-pause hold if enabled and the next due firing is beyond the threshold; otherwise one 250 ms poll) → next iteration.

A **non-cycling** run is unchanged.

## QueueCycleLedger (existing, in-memory)

| Member | Change |
|--------|--------|
| `EnsureOpen(now)` | unchanged |
| `CompleteOpen(now)` | unchanged; now called only for iterations that ran work in a cycling run |
| `DiscardOpenIfEmpty()` | **new**. Drops the open cycle only if it has no recorded entries. It never changes `CyclesCompleted`, `ConsecutiveFailedCycles` or completed records. |

Invariant kept: the ledger stays a pure observer. No scheduling decision reads it.

## Queue health (`GET /api/queues/{id}` → `health`), unchanged shape

- `cyclesCompleted`: counts only iterations that ran at least one sequence (for cycling runs).
- `lastCycleStartedAt` / `lastCycleCompletedAt`: bracket a cycle that ran work. Idle waiting time is excluded.

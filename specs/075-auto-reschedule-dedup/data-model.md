# Data Model: Deduplicate Self-Rescheduled Sequence Firings

**Feature**: 075-auto-reschedule-dedup | **Date**: 2026-07-29

This feature introduces **no new types** and **no persisted data**. It changes one invariant on an existing in-memory, run-scoped register. The entities below are the existing ones the change touches.

## Entity: Pending Timer self-reschedule firing (`SelfRescheduleEntry`)

Existing record (`QueueRunHandle.cs`), unchanged in shape:

| Field | Type | Meaning |
|-------|------|---------|
| `Id` | `string` | Fresh GUID per request; identifies the individual firing (not used for dedup). |
| `SequenceId` | `string` | The sequence that will run again. **Dedup key** for Timer firings. |
| `Option` | `SelfRescheduleOption` | `Timer` for entries in this register. |
| `FireAt` | `DateTimeOffset?` | Absolute instant the firing becomes due. The retained firing keeps the most-recently-requested value. |

## Register: `QueueRunHandle._pendingTimerFirings`

An in-memory `List<SelfRescheduleEntry>` guarded by `_timerLock`, owned by one `QueueRunHandle` (one queue run). Never persisted; discarded when the run ends.

### Invariant (new)

> At most one pending Timer firing exists per `SequenceId` within a run.

Established by making inserts most-recent-wins per sequence.

### State transitions

| Operation | Before | After |
|-----------|--------|-------|
| `AddTimerFiring(e)` where no pending firing has `SequenceId == e.SequenceId` | *(no entry for S)* | one entry for S at `e.FireAt` (unchanged from today, FR-007) |
| `AddTimerFiring(e)` where a pending firing for `e.SequenceId` exists | one entry for S at old `FireAt` | old entry removed; one entry for S at new `e.FireAt` (FR-002, FR-006) |
| `AddTimerFiring(e)` for a different sequence D | entry for S present | entry for S untouched; entry for D added (FR-003) |
| `DrainDueTimerFirings(now)` | ≤ one entry per sequence | due entries removed & returned (unchanged) |

### Unaffected registers (explicitly out of scope, FR-004)

| Register | Semantics | Change |
|----------|-----------|--------|
| `PendingOncePerRun` | accumulates | none |
| `PendingNextCycleStart` | accumulates | none |
| `EveryStepInjections` | already idempotent per sequence id | none |
| `PendingLiveSchedules` | already most-recent-wins per sequence id | none |
| Template-defined timers (`QueueRunSchedule`) | fire on their own schedule | none |

## Requirements traceability

| Requirement | Model element |
|-------------|---------------|
| FR-001 (≤1 pending per sequence) | Register invariant |
| FR-002 (replace pre-existing, most-recent-wins) | `AddTimerFiring` second row |
| FR-003 (per-sequence scope) | `AddTimerFiring` third row |
| FR-004 (other mechanisms unchanged) | Unaffected registers table |
| FR-005 (replace ≠ execution) | Remove+add is register-only; draining is separate |
| FR-006 (retained targets newest time) | `FireAt` = incoming entry's |
| FR-007 (first request unchanged) | `AddTimerFiring` first row |
| FR-008 (per-run scope) | Register owned by `QueueRunHandle`, discarded at run end |

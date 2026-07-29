# Research: Deduplicate Self-Rescheduled Sequence Firings

**Feature**: 075-auto-reschedule-dedup | **Date**: 2026-07-29

No open `NEEDS CLARIFICATION` items remained after `/speckit-clarify`. This file records the design decisions and the codebase facts they rest on.

## Decision 1 — "Auto-rescheduled" == the Timer self-reschedule register only

**Decision**: The deduplication applies solely to the run handle's pending **Timer** self-reschedule firings (`QueueRunHandle._pendingTimerFirings`, added via `AddTimerFiring`). The other self-reschedule options are out of scope.

**Rationale**: The user's phrasing — "there's another auto-rescheduled entry in the queue already" awaiting replacement — describes an entry parked for a *future* fire time. In the code that is exactly the Timer register: `SelfRescheduleCoordinator.ScheduleSelf` routes `SelfRescheduleOption.Timer` to `handle.AddTimerFiring(entry)` with a resolved `FireAt`, and the run loop later drains it via `DrainDueTimerFirings`. The other options are immediate or cycle-bound and are the "other types" the request excludes:
- `OncePerRun` → appended to the current cycle's drain queue (intentionally accumulates; existing test `TwoOncePerRunReschedulesAccumulate` asserts this).
- `AtQueueStart` → next-cycle-start queue (or once-per-run fallback).
- `EveryStep` → already idempotent per sequence id (`EveryStepInjections` dictionary keyed by sequence id; existing test `EveryStepIsIdempotentPerSequence`).

**Alternatives considered**:
- *Dedup every self-reschedule register*: rejected — contradicts "does not affect other types" and would break `TwoOncePerRunReschedulesAccumulate`.
- *Dedup at drain time* (collapse duplicates when firing): rejected — leaves the stacked state observable to the monitor and to `HasPendingTimerFirings`/`ComputeNextDue`, and is harder to reason about than preventing the duplicate at insertion.

## Decision 2 — Dedup key is the sequence id, scoped to the current run

**Decision**: Two pending Timer firings are "the same" iff they share a `SequenceId`. Matching is per queue run (the handle owns the register).

**Rationale**: A sequence re-arming itself is identified by its sequence id; `SelfRescheduleEntry.SequenceId` is already carried on every firing. This mirrors the established pattern for live schedules: `PendingLiveSchedules` is a `ConcurrentDictionary<string, DateTimeOffset>` keyed by sequence id with "most-recent-wins per sequence (FR-011)" semantics. Run scoping is automatic because `_pendingTimerFirings` lives on the per-run `QueueRunHandle`, which is discarded when the run ends (FR-008/FR-010 of feature 065).

**Alternatives considered**:
- *Key by the self-reschedule entry `Id` (GUID)*: rejected — every request gets a fresh GUID, so it would never match and never dedup.
- *Key by (sequence id + FireAt)*: rejected — different target times are precisely the case that must collapse to the newest.

## Decision 3 — Replace-then-add inside the existing lock; most-recent-wins

**Decision**: In `AddTimerFiring`, under the existing `_timerLock`, remove every entry whose `SequenceId` equals the incoming entry's, then add the incoming entry. The last write wins and targets the most-recently-requested `FireAt` (FR-002, FR-006).

**Rationale**: `_pendingTimerFirings` is written by the run-loop thread and read by the monitor thread; all access already goes through `_timerLock` (see `AddTimerFiring`, `DrainDueTimerFirings`, `SnapshotPendingTimerFirings`, `HasPendingTimerFirings`). Doing the remove+add in one locked section keeps the "at most one per sequence" invariant atomic and never triggers an execution — draining is a separate, later call (FR-005). The list stays small (one entry per self-rescheduling sequence), so an O(n) `RemoveAll` is trivially cheap.

**Alternatives considered**:
- *Switch `_pendingTimerFirings` to a `Dictionary<string, SelfRescheduleEntry>` keyed by sequence id*: viable and slightly more self-documenting, but a larger change touching `DrainDueTimerFirings`/`SnapshotPendingTimerFirings` iteration and ordering; rejected in favor of the minimal, lower-risk list `RemoveAll` that leaves the three readers unchanged.

## Codebase facts relied upon

- `SelfRescheduleCoordinator.ScheduleSelf` (`SelfRescheduleCoordinator.cs:62`) is the single producer of Timer firings; it already computes `FireAt` and calls `handle.AddTimerFiring(entry)`.
- `QueueRunHandle.AddTimerFiring` (`QueueRunHandle.cs:144`) currently does an unconditional `_pendingTimerFirings.Add(entry)` under `_timerLock`.
- Readers that must keep working unchanged: `DrainDueTimerFirings` (`QueueRunHandle.cs:151`), `SnapshotPendingTimerFirings` (`:139`), `HasPendingTimerFirings` (`:169`), and `QueueRunSchedule.ComputeNextDue` (`QueueRunSchedule.cs:175`) which iterates the snapshot.
- Precedent for per-sequence most-recent-wins: `PendingLiveSchedules` (`QueueRunHandle.cs:51`).

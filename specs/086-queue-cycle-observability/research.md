# Phase 0 Research: Queue Cycle Observability

**Feature**: 086-queue-cycle-observability | **Date**: 2026-09-14

Questions the plan had to answer against the existing code before a design was possible.

## R1 — Does the engine already have a cycle boundary, or must one be invented?

**Finding**: It already has one, and it is unambiguous.

`QueueExecutionService.ExecuteRunAsync` keeps a local `var cycles = 0;` and increments it at exactly
one place — immediately after `schedule.MarkOncePerRunPassDone()`, inside the block guarded by
`if (queue.CycleExecution || !schedule.OncePerRunPassDone)`. That count reaches
`new QueueRunResult(reason, executed, failed, cycles, failureReason)` and surfaces in the terminating
summary as "across N cycles".

**Consequence**: The feature publishes an existing counter rather than defining a competing notion of
"cycle". Any other boundary would produce a health count that disagrees with the run's own summary.

## R2 — Where does live run state already live, and is it safe to read concurrently?

**Finding**: `QueueRunHandle` is the established home for exactly this kind of state, and it already
solves the concurrency problem three separate times:

- `_currentSequenceId` — `volatile` for lock-free reads, set/cleared per firing (feature 072).
- `_idlePausedUntil` — guarded by `_idleLock`, read by the monitor on another thread (feature 073).
- `_pendingTimerFirings` — guarded by `_timerLock`, with a `SnapshotPendingTimerFirings()` that copies
  under the lock specifically so the monitor can read without mutating (feature 065/075).

**Consequence**: The ledger needs no new concurrency strategy — it copies the
`SnapshotPendingTimerFirings` pattern. It also confirms the placement: run-scoped observability state
belongs on the handle, not in the domain layer or a repository.

## R3 — Execution log per cycle, or live state plus a dedicated read?

The issue accepts either. Three findings decided it:

1. **The log cannot satisfy FR-013.** The run detail (`/subtree`) is populated after a run ends; that
   is precisely the limitation the issue reports as gap #3. Writing per-cycle records into the same
   store would not by itself make them readable mid-run.
2. **The log is file-backed and already load-bearing.** Feature 084 added retention and rotation to
   it, and a single malformed file has previously taken out the whole log listing. Multiplying its
   write volume by the cycle count of every production queue is a real operational risk to take on in
   a change whose purpose is to *reduce* operational blindness.
3. **FR-018 demands the existing records be unchanged.** Keeping the feature entirely out of the log
   makes that guarantee trivially true rather than something to verify.

**Decision**: Live, run-scoped ledger + a dedicated read. Recorded in the spec's Clarifications.

**Accepted cost**: Cycle history does not survive a service restart. The spec's Assumptions accept
this explicitly — run state is already in-memory, and the failure being addressed is a *running* queue
doing nothing, which is observable precisely when the state exists.

## R4 — What contract should a "read a running queue" endpoint use?

**Finding**: `GET /api/queues/{id}/monitor` (feature 072) already established it, and its route
registration carries the reasoning in a comment: 200 with `running:false` rather than 404/409 when the
queue exists but is not running, "so the client can render the … state", and "safe to poll".

**Consequence**: `{id}/cycles` copies that contract exactly. A caller polling both endpoints sees one
consistent set of rules. FR-016's split — 404 for an unknown queue, empty list for a queue that has
not run — is the same split `{id}/monitor` makes.

## R5 — How large is the run loop, and what does that constrain?

**Finding**: `ExecuteRunAsync` spans several hundred lines with deep nesting (a `using` chain, a
`try`, a `do/while`, and per-block `foreach`es). The repository has a recorded constraint that the
build-time Roslyn taint analyzers scale super-linearly with method body size — the sibling
`ImageDetectionsEndpoints.cs` carries a comment at its head explaining that it uses named static
handlers for this reason, and feature 085's plan treats it as a hard design input.

**Consequence**: The three run-loop edits must each be a single call with no new locals, no branching
and no lambdas. All logic goes in `QueueCycleLedger`. This is a build-time-cost constraint, not a
style preference.

## R6 — Which firings should count toward a cycle?

**Finding**: A single loop iteration executes up to five kinds of firing before the roster pass
(at-queue-start self-reschedules, live schedules, timer firings, self-reschedule timers, and
after-every-step passes), all of which call `RunOneSequenceAsync` and increment the same `executed`
counter the roster pass does. The engine already treats them as work done within the run's iteration.

**Consequence**: An idempotent `EnsureOpen` at the top of the iteration captures them into the cycle
that iteration completes, with no need to classify firings by kind. It also handles the non-cycling
tail correctly by construction: those trailing poll iterations never complete a cycle, so their
firings are never published as one — consistent with the engine's own `cycles` staying at 1.

## R7 — What bound keeps memory constant (SC-005)?

**Finding**: No existing run-scoped collection on the handle is bounded by count; they are bounded by
lifetime (drained when fired, discarded with the handle). A continuously cycling queue has no such
natural bound, so one must be chosen.

**Decision (R7)**: 50 completed cycles, oldest discarded first; the read's `limit` clamps to 1–50 with a
default of 20. A production queue cycling a five-entry roster stores at most 50 records of ~5 entry
outcomes each — a few kilobytes, constant regardless of run duration. 50 is enough to read a failure
pattern; a larger window would be history, which is the execution log's job, not a live monitor's.

## R8 — Can `status` and the health block contradict each other? (raised by analyze)

**Finding**: Yes, in two windows, because `status` and the run handle are maintained by two different
stores that are not updated together:

- **At start**, `QueueStartAsync` calls `_registry.TryAdd(queueId, handle)` well before
  `_runtime.SetStatus(queueId, Running)` — there is device claiming, template resolution and
  pre-session emulator work between them. A read landing in that window would find a handle while the
  queue still reports `Stopped`.
- **At end**, the run's `finally` calls `_runtime.SetStatus(queue.Id, Stopped)` and only then
  `_registry.Remove(queue.Id, out _)`. A read landing between them would find a handle while the queue
  already reports `Stopped`.

Keying the health block off the registry alone would therefore emit a populated `health` on a response
whose `status` says `Stopped` — exactly the confusion FR-008 exists to prevent, and worse than no
health block because it is self-contradictory.

**Decision**: Both read paths gate on the **reported status** as well as the handle: health is emitted,
and `running` is true, only when `IQueueRuntimeStore.GetStatus(id) == Running` *and* a handle is
registered. The status flip is the narrower condition at both ends, so this collapses both windows
without adding any synchronisation to the engine. Recorded as FR-008a.

**Note**: the reverse contradiction (status `Running` with no handle) cannot occur under the current
ordering, since the status is set to `Stopped` before the handle is removed; the conjunction handles it
regardless if that ordering ever changes.

## R9 — Does the spec's service-restart edge case actually hold?

**Finding**: Yes, verified. `QueueRuntimeStore` is an in-memory `ConcurrentDictionary` registered as a
singleton, and its own class comment states that "all entries and statuses are lost on restart by
design". `IQueueRunRegistry` is likewise in-memory.

**Consequence**: After a restart a queue reports `Stopped` and, under the FR-008a gate, no health block
— which is the behaviour the spec's Edge Cases assert. No work is needed to make the claim true, and no
stale health can survive a restart.

# Implementation Plan: Queue Cycle Observability

**Branch**: `086-queue-cycle-observability` | **Date**: 2026-09-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/086-queue-cycle-observability/spec.md`
**Issue**: [#180](https://github.com/bbqf/GameBotAI/issues/180) (FR-003)

## Summary

A cycling queue run counts its own cycles today — `QueueExecutionService` keeps a local `cycles`
counter and reports "across N cycles" in the terminating execution-log summary — but that count is
write-only until the run ends. Nothing observes it while the run is alive, so a run that has done
nothing useful for 44 hours is indistinguishable from a healthy one.

The fix does not add a new counting mechanism; it *publishes the one already there*:

1. `QueueRunHandle` gains a small, bounded cycle ledger: an open cycle that accumulates per-entry
   outcomes, a completed-cycle ring bounded at 50, and derived health counters. It sits alongside the
   existing run-scoped, lock-guarded, in-memory state on that handle (current sequence, idle pause,
   pending timer firings) and follows exactly the same concurrency pattern.
2. The run loop calls into that ledger at three points it already has: the top of each loop iteration,
   each sequence outcome, and the `cycles++` at the end of the once-per-run pass.
3. Two read paths project the ledger: a `health` block on `GET /api/queues/{id}`, and a new
   `GET /api/queues/{id}/cycles?limit=n`. Both read the live handle from `IQueueRunRegistry`, so both
   work *while the queue is running* — which is what the existing `/subtree` run detail cannot do.

**The load-bearing constraint** (spec FR-020): the ledger must be a pure observer. Every call the run
loop makes into it is a void, non-throwing state update; no scheduling decision reads it. If the
ledger were removed the run would behave identically. This is what keeps the change safe to land on an
engine that drives production farms.

**The second constraint** (Code Quality, and the `gamebot-build-time-analyzers` lesson): the run loop
is one very large method that the Roslyn taint analyzers already scale badly against. Every addition
to it is a single call to a method on the handle — no inline blocks, no new locals beyond none.

## Technical Context

**Language/Version**: C# / .NET 9 (`net9.0`)
**Primary Dependencies**: ASP.NET Core Minimal APIs, Swashbuckle; no new packages
**Storage**: N/A — the ledger is in-memory and run-scoped, discarded with the handle (spec Assumptions)
**Testing**: xUnit + FluentAssertions; `tests/contract` (via `WebApplicationFactory<Program>`) and `tests/unit`
**Target Platform**: Windows service host; CI is `windows-latest`
**Project Type**: Web service (single ASP.NET Core host); the React `web-ui` is untouched (spec Out of Scope)
**Performance Goals**: No measurable change to run-loop timing. Per completed cycle the added work is
one list append, one bounded-ring trim, and three counter updates, all under a lock held for O(1)
work. Per sequence, one append to the open cycle's entry list. Reads are O(returned cycles) over a
snapshot copy and never block the run loop for longer than the copy.
**Constraints**: Additive to the queue representation only (FR-017); existing run-level execution
records byte-for-byte unchanged (FR-018); no change to scheduling (FR-020)
**Scale/Scope**: One new domain-ish record set on the handle, three call sites in the run loop, two
DTO files, one new endpoint, one projection helper, plus tests and `docs/architecture.md`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment | Status |
|-----------|-----------|--------|
| **I. Code Quality Discipline** | The run loop (`ExecuteRunAsync`) is already far past the ~50 LOC guidance and is a known hazard for the build-time taint analyzers. This change therefore adds **no logic** to it — only three one-line calls to methods on `QueueRunHandle`. All ledger logic lives in a new, small, single-purpose type (`QueueCycleLedger`) with XML docs on every public member; the projection to DTOs lives in its own static helper rather than inline in the endpoint lambda, matching how `ProjectMonitor` is already factored. No new dependencies. | PASS |
| **II. Testing Standards** | This is an enhancement, not a bug fix, so the "failing test first" clause does not bind — but the *condition* the issue describes is testable and gets an explicit regression test: a run whose every cycle fails must report a climbing `consecutiveFailedCycles` while still `Running`. Coverage: unit tests over `QueueCycleLedger` (ordinal, outcome derivation, reset-on-success, ring bound, open-cycle discard) and contract tests over both read paths including the not-found, never-run, and limit-clamp cases. | PASS |
| **III. UX Consistency** | The new endpoint follows the sibling `{id}/monitor` convention exactly: 404 for an unknown queue, 200 with `running:false` and an empty list for a known queue that is not running (never 409), safe to poll. Field names are taken verbatim from the issue's proposed shape so the requesting project's code matches. Swagger gets the new route and the `health` block. | PASS |
| **IV. Performance** | Budget declared in Performance Goals. The perf note for the PR: the run loop's added cost is O(1) per sequence and O(1) amortized per cycle under an uncontended lock; the ring is capped at 50 entries and each entry holds a bounded list of per-entry outcomes, so a week-long run's memory is constant (SC-005). No hot path is touched. | PASS |
| **V. Living Documentation (NON-NEGOTIABLE)** | This changes the API surface, so `docs/architecture.md` MUST gain the `health` block and the `{id}/cycles` route and have its "Last reviewed" refreshed. `spec.md` needs its `**Status**:` line moved off Draft and `specs/STATUS.md` a row for 086. Feature 072 (live monitor) is complemented, not superseded, so no earlier spec's Status changes. All explicit tasks. | PASS |

**Gate result: PASS** — no violations, so the Complexity Tracking table is omitted.

## Project Structure

### Documentation (this feature)

```text
specs/086-queue-cycle-observability/
├── spec.md              # Feature specification (with Clarifications session)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── queue-cycles.md  # Phase 1 output — health block + cycles endpoint contract
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Services/QueueExecution/
│   ├── QueueCycleLedger.cs              # NEW — the bounded ledger + its records
│   ├── QueueRunHandle.cs                # MODIFY — owns a QueueCycleLedger
│   └── QueueExecutionService.cs         # MODIFY — three one-line calls at existing points
├── Contracts/Queues/
│   ├── QueueDetailResponse.cs           # MODIFY — gains Health
│   ├── QueueHealthResponse.cs           # NEW — the health block
│   └── QueueCyclesResponse.cs           # NEW — cycles list + per-entry outcomes
├── Endpoints/
│   └── QueuesEndpoints.cs               # MODIFY — health on GetQueue, new GetQueueCycles route
└── Swagger/
    └── SwaggerConfig.cs                 # MODIFY — document the new route and block

tests/
├── unit/                                 # GameBot.UnitTests.csproj
│   └── QueueCycleLedgerTests.cs         # NEW — ledger semantics
└── contract/Queues/                      # GameBot.ContractTests.csproj
    └── QueueCyclesApiContractTests.cs   # NEW — both read paths, error cases, clamping

docs/
└── architecture.md                      # MODIFY — API surface + Last reviewed

specs/
└── STATUS.md                            # MODIFY — add the 086 row
```

**Structure Decision**: No new projects or layers. The ledger is service-layer run state, not a domain
concept — it is never persisted, never repository-backed, and dies with the run — so it belongs beside
`QueueRunHandle` in `Services/QueueExecution`, not in `GameBot.Domain.Queues`. That placement mirrors
`QueueRunSchedule` and `QueueMonitorSnapshot`, which are the same kind of thing.

## Design

### Where a cycle begins and ends

The run loop's `do { … } while (true)` already has exactly the boundary this feature needs. Per
iteration it runs, in order: self-reschedule and timer firings (blocks a0–a4), then — guarded by
`if (queue.CycleExecution || !schedule.OncePerRunPassDone)` — the once-per-run roster pass, ending in
`schedule.MarkOncePerRunPassDone(); cycles++;`.

The ledger binds to that:

| Point in the loop | Call |
|---|---|
| Top of each iteration | `handle.Cycles.EnsureOpen(now)` |
| After every `RunOneSequenceAsync` result | `handle.Cycles.RecordEntry(sequenceId, ok)` |
| At the existing `cycles++` | `handle.Cycles.CompleteOpen(now)` |

`EnsureOpen` is idempotent: it opens a cycle only when none is open. This is what makes the two queue
kinds behave correctly with one rule:

- **Cycling run** — every iteration reaches the roster pass, so every iteration opens and completes
  exactly one cycle. Firings from blocks a0–a4 land in the cycle the iteration goes on to complete,
  which is what the spec's Assumptions say they should.
- **Non-cycling run** — the first iteration opens and completes cycle 1. Later iterations exist only
  to wait for pending relative/live timers; they never reach the roster pass, so the cycle they open
  is never completed and is discarded with the handle. `cyclesCompleted` stays 1, matching FR-019 and
  the engine's own `cycles` counter.

A cycle interrupted by a stop or a connection loss is simply an open cycle that never completes — it
is never published, satisfying the first Edge Case with no extra code.

The one branch that bypasses the loop is the empty-template case, which sets `cycles = 1` outright; it
gets a matching `handle.Cycles.RecordEmptyCycle(now)` so an idle-but-alive queue still reports a
completed cycle (Edge Cases).

### The ledger

`QueueCycleLedger` holds, under a single private lock:

- the open cycle, if any — start instant plus a growing list of `(sequenceId, succeeded)`;
- a `Queue<QueueCycleRecord>` of completed cycles, trimmed to the newest **50** on each completion;
- `CyclesCompleted` (monotonic for the run — *not* the ring length, which is capped);
- `ConsecutiveFailedCycles`, incremented when a completed cycle failed and reset to 0 when it
  succeeded;
- the current roster entry index, set and cleared by the roster pass.

A cycle's outcome is derived at completion: **failed if any recorded entry failed, succeeded
otherwise** — so a cycle with no entries at all succeeds. The ordinal assigned to a record is
`CyclesCompleted` after increment, so ordinals are stable and keep counting past the ring bound.

Every mutating method returns `void` and cannot throw for reasons the caller must handle; reads return
snapshot copies so a reader never holds the lock while the run loop wants it. This is the same
discipline `QueueRunHandle` already applies to `_currentSequenceId`, `_idlePausedUntil` and
`_pendingTimerFirings`.

### Read paths

Both resolve the live handle through `IQueueRunRegistry.TryGet` — but neither keys off it alone.

**The liveness gate (FR-008a).** `status` and the run handle live in two stores that are not updated
together: the handle is added *before* `SetStatus(Running)` at start, and removed *after*
`SetStatus(Stopped)` at end (research R8). Keying the health block off the handle alone would let a
response say `status: "Stopped"` while carrying a populated `health` — self-contradictory, and exactly
what FR-008 exists to prevent. So both read paths use the conjunction:

> a run is live iff `IQueueRuntimeStore.GetStatus(id) == Running` **and** `IQueueRunRegistry.TryGet`
> yields a handle.

The status flip is the narrower condition at both ends, so this closes both windows without adding any
synchronisation to the engine, and it guarantees the two endpoints agree with each other.

**`GET /api/queues/{id}`** gains a nullable `health` object on `QueueDetailResponse`, populated only
when the gate holds — `null` otherwise, never a zeroed object, so a stopped queue can never be misread
as a live one (FR-008). The queue *list* response is deliberately untouched (Clarifications).

**`GET /api/queues/{id}/cycles?limit=n`** is new and mirrors `{id}/monitor`'s contract decisions:

| Case | Response |
|---|---|
| Unknown queue id | `404 not_found` (FR-016) |
| Known queue, not live per the gate above | `200 { running: false, cycles: [] }` (FR-016, FR-008a) |
| Running | `200 { running: true, cycles: [ … ] }`, newest first |
| `limit` omitted | 20 |
| `limit` out of 1–50 | clamped, never rejected (FR-014) |

Returning 200-with-empty rather than 404 for a stopped queue is the same choice `{id}/monitor` already
made, for the same reason: the caller is polling and wants to render a state, not handle an error.

### What is deliberately not changed

- `QueueRunSchedule` and every scheduling decision — the ledger is write-only from the loop's side.
- The root and terminating execution-log records, their summary text, and their status (FR-018).
  Nothing is written to the execution log by this feature at all.
- `/subtree` and when it becomes populated (spec Out of Scope).
- `QueueExecutionStatus`, which stays `Stopped | Running`. The issue observes that this enum is being
  asked to carry meaning it cannot; the answer is to put that meaning in the health block, not to
  overload the enum further and break every existing consumer of it (FR-017).
- The web UI.

## Complexity Tracking

Not applicable — Constitution Check passed with no violations.

# Implementation Plan: Deduplicate Self-Rescheduled Sequence Firings

**Branch**: `075-auto-reschedule-dedup` | **Date**: 2026-07-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/075-auto-reschedule-dedup/spec.md`

## Summary

A queue sequence can reschedule itself to run again at a future time via the **Timer** self-reschedule option (feature 065). Today each such request appends an independent firing to the run handle's `_pendingTimerFirings` list, so a sequence that self-reschedules more than once before its earlier firing comes due stacks duplicate future firings and later executes redundantly. The fix makes the Timer self-reschedule register **most-recent-wins per sequence id**, exactly mirroring how `PendingLiveSchedules` already dedups per sequence: when a Timer firing for sequence `S` is added, any existing pending Timer firing for `S` in the same run is removed first. All other schedule sources — template timers, once-per-run, after-every-step, next-cycle-start, live schedules — are untouched.

Technical approach: change `QueueRunHandle.AddTimerFiring` to drop existing same-`SequenceId` entries under the existing `_timerLock` before appending, and update its documentation comment. No new types, no API/schema/config change, no persistence change.

## Technical Context

**Language/Version**: C# / .NET 8 (backend service); no frontend change
**Primary Dependencies**: none new — edit is within `GameBot.Service`
**Storage**: N/A — self-reschedule firings are in-memory, run-scoped, never persisted
**Testing**: xUnit + FluentAssertions (`tests/unit/Queues`), plus existing integration tests under `tests/integration/Queues`
**Target Platform**: Windows service host running the GameBot queue engine
**Project Type**: Single project (backend .NET service + separate web-ui; only the service changes)
**Performance Goals**: No measurable impact — the pending-firings list per run is tiny (one entry per self-rescheduling sequence); a linear same-sequence removal on add is O(n) over a handful of entries
**Constraints**: Change must be thread-safe (run-loop thread writes; monitor thread reads) — reuse the existing `_timerLock`; must not alter any other schedule source's behavior
**Scale/Scope**: One method body + its doc comment in `QueueRunHandle.cs`; new unit tests; targeted doc update

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality Discipline**: PASS — single small, cohesive method change; no dead code; the public-ish accessor keeps its XML doc, updated to reflect most-recent-wins. CamelCase only.
- **II. Testing Standards**: PASS — bug-fix-with-failing-test first: new unit tests prove a second Timer firing for the same sequence replaces the first, and that a different sequence is untouched; existing coordinator/monitor/integration tests must stay green (they assert single-firing cases and other registers, which are unaffected).
- **III. UX Consistency**: PASS — no user-facing interface change; the self-reschedule action author sees strictly less surprising behavior (no duplicate runs).
- **IV. Performance**: PASS — negligible; declared above. No hot-path regression.
- **V. Living Documentation (NON-NEGOTIABLE)**: `docs/architecture.md` describes the self-reschedule Timer register; if it states firings "accumulate", it MUST be updated to note Timer firings are most-recent-wins per sequence, with a refreshed "Last reviewed" date. Feature 065's spec `Status` line is updated to "Implemented (iterated by 075)" and `specs/STATUS.md` kept consistent. This spec carries a `Status` line.

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/075-auto-reschedule-dedup/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (internal behavior contract)
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/QueueExecution/
├── QueueRunHandle.cs          # CHANGE: AddTimerFiring dedups by SequenceId under _timerLock; update doc comment
├── SelfRescheduleCoordinator.cs  # (unchanged) already routes Timer → handle.AddTimerFiring
├── QueueExecutionService.cs      # (unchanged) drains due firings via DrainDueTimerFirings
└── QueueMonitorService.cs        # (unchanged) reads SnapshotPendingTimerFirings

tests/
├── unit/Queues/
│   ├── SelfRescheduleCoordinatorTests.cs  # ADD: Timer most-recent-wins per sequence; distinct sequences independent
│   └── QueueRunHandleTimerFiringTests.cs   # ADD (new file): direct AddTimerFiring dedup unit tests
└── integration/Queues/
    └── SelfRescheduleRunIntegrationTests.cs # (verify still green; optionally add a duplicate-collapse run test)

docs/
└── architecture.md            # CHANGE (if it describes accumulation): note Timer firings are most-recent-wins per sequence
```

**Structure Decision**: Single-project backend change confined to `GameBot.Service/Services/QueueExecution`, backed by unit tests in `tests/unit/Queues`. No web-ui or contract-schema change because self-reschedule firings are internal in-memory state with no external API surface.

## Complexity Tracking

No constitution violations; section intentionally empty.

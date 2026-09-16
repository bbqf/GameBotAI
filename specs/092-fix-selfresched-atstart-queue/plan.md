# Implementation Plan: Self-reschedule bookings keep an AtQueueStart-only queue running

**Branch**: `092-fix-selfresched-atstart-queue` | **Date**: 2026-09-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/092-fix-selfresched-atstart-queue/spec.md` (GitHub issue #198)

## Summary

`QueueExecutionService.RunAsync` runs the at-queue-start pre-pass. It then enters its scheduling `do { … } while (true)` loop **only if** the template has once-per-run, every-step or timer entries (`QueueExecutionService.cs:386`). Otherwise it takes the "empty template" `else` branch: `cycles = 1`, record an empty cycle, "completed full run".

When a template holds only at-queue-start entries, that gate is false. Any self-reschedule booking made during the pre-pass (a Timer firing on the handle, a `PendingNextCycleStart` or a `PendingOncePerRun` entry) is therefore never drained, and the run ends immediately. Mixed templates enter the loop, and inside the loop the existing `HasPendingRelativeOrLive()` keep-alive (which already includes `handle.HasPendingTimerFirings`) keeps a non-cycling run alive until the booking fires. That is why the control case works.

**Fix**: widen the loop-entry gate so it also enters when the run handle has pending self-reschedule work after the pre-pass. That work is a pending Timer firing, a next-cycle-start booking, a once-per-run booking or a pending live schedule. Every-step injections are excluded (see research R-002). Nothing else changes. Inside the loop, the existing drain points (a0, a4, once-per-run drain), idle-pause, poll, stop and cycle ledger logic apply unchanged.

The gate predicate is extracted into a small named helper on `QueueRunHandle`, so the gate reads as intent and can be unit-tested. The fix gets failing-first unit tests on the run engine (constitution II).

## Technical Context

**Language/Version**: C# / .NET 9 (GameBot.Service)  
**Primary Dependencies**: none new; `Microsoft.Extensions.Time.Testing.FakeTimeProvider` already used in tests  
**Storage**: N/A (bookings are ephemeral, run-scoped)  
**Testing**: xUnit unit tests in `tests/unit/Queues/QueueExecutionServiceTests.cs` (existing `Harness` + `FakeTimeProvider`)  
**Target Platform**: Windows service (GameBot.Service)  
**Project Type**: web-service (backend engine fix only; no web-ui change)  
**Performance Goals**: no hot-path cost beyond one extra O(1) predicate evaluated once per run. The non-cycling wait uses the existing poll/idle-pause (no busy loop).  
**Constraints**: no change to completion semantics for templates without bookings (FR-006/FR-007); no API/contract change  
**Scale/Scope**: one engine method gate, one handle helper, ~5 unit tests, one architecture-doc sentence

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment | Status |
|-----------|------------|--------|
| I. Code quality | A one-condition gate change plus a small named helper. No new analyzer warnings. CamelCase method names (no underscores). | PASS |
| II. Testing | Bug fix → failing test first (T-tests written and run red before the gate change). Unit tests use the deterministic `FakeTimeProvider`. | PASS |
| III. UX consistency | No API/response shape change. The run summary text is unchanged. | PASS |
| IV. Performance | Declared above. The only runtime change is that a booking-bearing run now waits on the existing poll/idle-pause. Cycling spin behaviour is inherited from existing timer-only cycling (#200, out of scope). | PASS |
| V. Living documentation | `docs/architecture.md` self-reschedule bullet gains the keep-alive guarantee plus a refreshed "Last reviewed" date. Spec 092 `Status` → Implemented and `specs/STATUS.md` row added at completion. No earlier spec is superseded, because this is a defect fix within feature 065's stated intent. | PASS |

Post-design re-check: PASS (design adds no violations).

## Project Structure

### Documentation (this feature)

```text
specs/092-fix-selfresched-atstart-queue/
├── spec.md
├── plan.md              # this file
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks output
```

No `contracts/` directory: the fix changes no REST endpoint, DTO or schema.

### Source Code (repository root)

```text
src/GameBot.Service/Services/QueueExecution/
├── QueueExecutionService.cs   # widen loop-entry gate (line ~386)
└── QueueRunHandle.cs          # new HasPendingSelfRescheduleWork helper

tests/unit/Queues/
└── QueueExecutionServiceTests.cs   # new AtQueueStart-only + reschedule-self tests

docs/architecture.md                # keep-alive sentence + Last reviewed
specs/STATUS.md                     # row 092
```

**Structure Decision**: this is a backend-only change inside the existing queue execution service and its unit test suite.

## Complexity Tracking

No violations.

# Implementation Plan: A cycling queue with nothing due waits instead of spinning empty cycles

**Branch**: `093-fix-empty-cycle-spin` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/093-fix-empty-cycle-spin/spec.md` (GitHub issue #200)

## Summary

The scheduling loop in `QueueExecutionService.RunAsync` (`do { … } while (true)`) works like this in a cycling run:

1. It evaluates the due firings: next-cycle-start bookings, time-of-day timers, daily retries, relative timers, live schedules and self-reschedule timers.
2. Because `queue.CycleExecution` is true, it always runs the once-per-run block. That block increments `cycles`, seals the cycle in the ledger (`handle.Cycles.CompleteOpen`) and calls the failure policy.
3. It then runs `if (queue.CycleExecution) continue;` with no wait.

A template with no once-per-run or every-step entries runs nothing in steps 1–2 on most iterations, so each iteration is an empty cycle that completes in about a microsecond. That is the 450,000 cycles a second in the issue. The idle-pause and poll logic sit below the `continue`, so a cycling run never reaches them.

**Fix** (one method):

- Snapshot the sequence-firing counter `index` at the top of each iteration. Every `RunOneSequenceAsync` call increments it, including every-step passes and self-reschedule drains. After the firing evaluation, `index` unchanged means no sequence ran.
- In a cycling run, when the once-per-run block would run no sequence (no once-per-run entries, no every-step entries, no queued once-per-run bookings) and nothing fired earlier in the iteration, skip the cycle bookkeeping. That means no `cycles++`, no `CompleteOpen` and no failure-policy call. Discard the entry-less open ledger cycle, so a later real cycle gets a fresh start time (FR-007).
- Replace `if (queue.CycleExecution) continue;` with: continue at once only if the iteration ran something. Otherwise fall through to the existing wait (idle-pause hold or one poll interval). For a cycling run the existing `if (!HasPendingRelativeOrLive()) break;` does not apply, because a cycling run never ends by itself (FR-004).

Non-cycling runs keep their exact path: they still run the once-per-run block once, count their one cycle, and break or wait as today (FR-009).

The ledger gains one observer-only mutator, `DiscardOpenIfEmpty()`. It drops an open cycle that has recorded no entries, and never touches counters or completed records. The ledger's "pure observer" guarantee holds.

## Technical Context

**Language/Version**: C# / .NET 9 (GameBot.Service)  
**Primary Dependencies**: none new; `Microsoft.Extensions.Time.Testing.FakeTimeProvider` already used in tests  
**Storage**: N/A (the cycle ledger is in-memory and run-scoped)  
**Testing**: xUnit + FluentAssertions. Unit tests in `tests/unit/Queues/QueueExecutionServiceTests.cs` (existing `Harness`, `FakeTimeProvider`, `WaitForAsync`) and ledger unit tests in `tests/unit/QueueCycleLedgerTests.cs`  
**Target Platform**: Windows service (GameBot.Service)  
**Project Type**: web-service (backend engine fix only; no web-ui change)  
**Performance Goals**: a waiting cycling run wakes at most once per `RelativeTimerPollInterval` (250 ms), or holds in idle pause. That replaces a continuous hot loop (~450k iterations/s). Cycling rosters with real work pay one extra integer comparison per iteration.  
**Constraints**: no change for non-cycling runs, empty templates or cycling rosters with once-per-run or every-step entries. No API or contract change.  
**Scale/Scope**: one loop-tail change in one method, one ledger helper, about 6 unit tests, one architecture-doc sentence

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment | Status |
|-----------|------------|--------|
| I. Code quality | A localized change to the loop tail plus a small named ledger helper. No new analyzer warnings. CamelCase test names (no underscores). | PASS |
| II. Testing | Bug fix → failing tests first: the spin and idle-pause tests are written and run red before the loop change. Tests use the deterministic `FakeTimeProvider` and bounded waits. | PASS |
| III. UX consistency | No API or response shape change. `cyclesCompleted` now means "cycles that ran work", which is what #180 documented it to mean. The run summary format is unchanged. | PASS |
| IV. Performance | This is the point of the fix: it removes a CPU hot loop. Declared above. | PASS |
| V. Living documentation | The `docs/architecture.md` cycle-observability bullet gains the "empty iterations are not cycles; a cycling run waits" guarantee, and "Last reviewed" is refreshed. At completion, spec 093 `Status` → Implemented and a `specs/STATUS.md` row is added. | PASS |

Post-design re-check: PASS (design adds no violations).

## Project Structure

### Documentation (this feature)

```text
specs/093-fix-empty-cycle-spin/
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
├── QueueExecutionService.cs   # loop tail: skip empty-cycle bookkeeping, wait instead of `continue`
└── QueueCycleLedger.cs        # new DiscardOpenIfEmpty() observer-only helper

tests/unit/
├── Queues/QueueExecutionServiceTests.cs   # new cycling scheduled-only spin / idle-pause / regression tests
└── QueueCycleLedgerTests.cs               # DiscardOpenIfEmpty tests (existing file)

docs/architecture.md                # cycle-observability sentence + Last reviewed
specs/STATUS.md                     # row 093
```

**Structure Decision**: this is a backend-only change inside the existing queue execution service and its unit test suite.

## Complexity Tracking

No violations.

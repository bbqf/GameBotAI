# Tasks: Before-Each-Run Schedule Type

**Input**: Design documents from `specs/095-before-each-run-schedule/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-changes.md, quickstart.md

**Tests**: Required (constitution II). Test tasks precede implementation within each story.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Confirm a green baseline: `dotnet build C:\src\GameBot\GameBot.sln -c Debug` and `dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~Queue"` pass before changes

## Phase 2: Foundational (blocks all stories)

- [X] T002 Add `BeforeEachRun = 4` with an XML doc (runs immediately before each timed, live-scheduled or self-rescheduled firing, at most once per scheduler wake-up, template order; not counted toward executed; failures non-fatal; displayed "Before Each Run") to `src/GameBot.Domain/QueueTemplates/ScheduleType.cs`
- [X] T003 Add `BeforeEachRun` to `ScheduleKind` next to `EveryStep` (serialized by name, so position is cosmetic) with an XML doc in `src/GameBot.Service/Services/QueueExecution/QueueMonitorSnapshot.cs`

**Checkpoint**: solution builds.

## Phase 3: User Story 1 — Establish a known state before each scheduled task (P1) 🎯 MVP

**Goal**: BeforeEachRun entries run, in template order, immediately before every triggering firing.
**Independent Test**: unit tests in `tests/unit/Queues/QueueExecutionServiceTests.cs` observe the recorded sequence execution order.

- [X] T004 [US1] Add failing unit tests to a new partial of the same test class, `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs`, following the file's existing fake-time/recording-executor style: (a) BeforeEachRun B + time-of-day Timer T → order B, T; (b) B + relative Timer → B before it; (c) B + live schedule via `ScheduleRelative` → B before it; (d) B + self-reschedule Timer firing → B before it; (e) B1, B2 template order → B1, B2, T; (f) failing B → T still runs, `failed` incremented, `executed` excludes B; (g) disabled B → never runs; (h) daily retry of a failed time-of-day firing → B runs before the retry; (i) self-reschedule OncePerRun and AtQueueStart (next-cycle) firings → B runs before each; (j) a queue with `PauseWhenIdle` whose next timer is beyond the idle threshold → after the hold ends, B runs before the timer (US1-AS5); (k) each B execution is logged as a sequence execution under the run root and recorded in the cycle ledger (FR-015; template timer firings themselves are not ledger entries, unchanged)
- [X] T005 [US1] Implement the pass in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`: partition `beforeEachRunEntries` beside `everyStepEntries`; add local function `RunBeforeEachRunPassAsync()` beside `RunEveryStepPassAsync` (per entry: `ct.ThrowIfCancellationRequested()`, session check → `QueueConnectionLostException`, `RunOneSequenceAsync(..., EntryScope(entry), ct)`, `failed++` on failure, `handle.Cycles.RecordEntry`; no `executed++`; never calls `RunEveryStepPassAsync`); guard with `beforeEachRunRanThisIteration` reset at the top of each loop iteration; await it immediately before `RunOneSequenceAsync` at sites (a0), (a), (a1b), (a2), (a3), (a4) and the self-reschedule OncePerRun drain in (b). Do not change the loop-entry condition, `cycleHasWork`, `HasPendingRelativeOrLive`, or the AtQueueStart pre-pass
- [X] T006 [US1] Update the class-level schedule-types summary comment in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` to describe BeforeEachRun
- [X] T007 [US1] Run `dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~QueueExecutionService"`; all tests (new + existing) pass

## Phase 4: User Story 2 — Once per wake-up (P1)

**Goal**: Several triggering firings due at one iteration share one pass; iterations with no triggering firing run no pass.
**Independent Test**: unit tests observe exactly one B execution before two co-due timers.

- [X] T008 [US2] Add unit tests to `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs`: (a) two time-of-day Timers due together + B + an EveryStep entry E → order B, T1, E, T2, E (B exactly once); (b) template with B + OncePerRun steps only (no triggering firings), non-cycling → B never runs and run completes as before; (c) template with only B entries → run behaves as an empty template (no B execution, completes); (d) cycling queue with B + OncePerRun step → B never runs across cycles; (e) a B sequence whose own firing does not trigger an EveryStep pass (EveryStep E runs only after the timed firing, not after B); (f) the run is stopped while B is executing → the triggering timer does not run and the stop reason is StoppedManually; (g) the session disappears before B → the run ends with the connection-lost failure; (h) a B sequence that books a self-reschedule Timer firing → the booking fires later with its normal semantics and B is not re-entered within the same iteration
- [X] T009 [US2] Adjust `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` if any T008 test fails; re-run the queue unit tests until green

## Phase 5: User Story 3 — Configure and see the type (P2)

**Goal**: API accepts/returns the type; monitor lists it; editor exposes it; docs describe it.
**Independent Test**: contract/integration tests round-trip the type; monitor unit test lists it; Jest tests cover the area.

### Tests

- [X] T010 [P] [US3] Contract tests in `tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs`: save with `BeforeEachRun` (and lower-case `beforeeachrun`) → 2xx and detail returns `BeforeEachRun`; invalid type → 400 whose message contains `BeforeEachRun`
- [X] T011 [P] [US3] Integration test in `tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs`: a BeforeEachRun entry persists and reloads with null timer fields
- [X] T012 [P] [US3] Unit tests in `tests/unit/Queues/QueueMonitorServiceTests.cs`: a running queue with an enabled BeforeEachRun entry lists it once with kind `BeforeEachRun`, reason "Before Each Run", null expectedAt, repeats false, ordered after timed items and before EveryStep items; a current BeforeEachRun sequence maps to kind `BeforeEachRun`
- [X] T013 [P] [US3] Jest tests in `src/web-ui/src/components/queues/__tests__/schedulingAreas.test.ts`: `areaForScheduleType('BeforeEachRun') === 'beforeEachRun'` and back; grouping places a BeforeEachRun entry in the new area; `applyDragMove` into/out of `beforeEachRun` changes scheduleType and flattens in canonical order
- [X] T014 [P] [US3] Jest test in `src/web-ui/src/components/queues/__tests__/QueueSchedulingAreas.test.tsx`: the "Before each run" area renders and shows a card whose schedule is BeforeEachRun

### Implementation

- [X] T015 [US3] Update the accepted-values error message in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs` to `OncePerRun, EveryStep, Timer, AtQueueStart, BeforeEachRun`
- [X] T016 [P] [US3] Update XML docs listing schedule type values in `src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs` and `src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs` ("BeforeEachRun" displayed as "Before Each Run")
- [X] T017 [P] [US3] Update the `ScheduleKind` value list XML doc in `src/GameBot.Service/Contracts/Queues/QueueMonitorResponse.cs`
- [X] T018 [US3] In `src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs`: `KindFor` maps `ScheduleType.BeforeEachRun`; `ReasonFor` returns "Before Each Run"; `BuildUpcoming` builds `beforeEachRunItems` (once per entry, expectedAt null, relativeLabel null, repeats false) and concatenates spine → timed → beforeEachRun → everyStep; `Repeats` unchanged (false for the new kind)
- [X] T019 [P] [US3] Add `'BeforeEachRun'` to the `ScheduleType` union in `src/web-ui/src/services/queueTemplates.ts` and to the `ScheduleKind` union in `src/web-ui/src/services/queues.ts`
- [X] T020 [US3] In `src/web-ui/src/components/queues/schedulingAreas.ts`: add area id `beforeEachRun` (label "Before each run"), both mapping tables, `CANONICAL_AREA_ORDER` = startOfExecution, beforeEachRun, oncePerRun, scheduled, afterEveryStep; both empty-bucket objects; update the "four areas" comments
- [X] T021 [US3] Render a `SchedulingArea areaId="beforeEachRun"` at the top of the right column (above "afterEveryStep") in `src/web-ui/src/components/queues/QueueSchedulingAreas.tsx`
- [X] T022 [P] [US3] Add `BeforeEachRun: 'Before Each Run'` label and a "Before Each Run" badge in `src/web-ui/src/components/queues/SchedulingSequenceCard.tsx` and `src/web-ui/src/components/queues/QueueEntryList.tsx` (mirroring the After Every Step badge)
- [X] T023 [US3] Run the contract, integration, monitor unit and web-ui tests (`npm --prefix C:\src\GameBot\src\web-ui test`, `npm --prefix C:\src\GameBot\src\web-ui run build`); all pass

## Phase 6: Polish & Cross-Cutting

- [X] T024 [P] Update `docs/architecture.md`: add *Before each run* to the sequence-schedule list with its semantics (triggering firings, once per wake-up, accounting); refresh "Last reviewed" to 2026-09-17 (feature 095)
- [X] T025 [P] Set `**Status**: Implemented` in `specs/095-before-each-run-schedule/spec.md` and add the 095 row to `specs/STATUS.md` in its existing format
- [X] T026 [P] Add a user-visible entry for the Before Each Run schedule type (issue #202) to `CHANGELOG.md` in its existing format, including a one-line perf note (no cost for templates without BeforeEachRun entries; one pass per wake-up otherwise)
- [X] T027 Full verification: `dotnet build C:\src\GameBot\GameBot.sln -c Debug` (no new warnings), `dotnet test` for unit, integration and contract test projects, web-ui `vite build` + `jest`

## Dependencies & Execution Order

- Phase 1 → Phase 2 → US1 (T004–T007) → US2 (T008–T009, same file as US1) → US3 → Polish.
- US3 depends only on Phase 2 and can run in parallel with US1/US2 (different files), except T018 which needs T003.
- Within US3, T010–T014 are parallel; T016, T017, T019, T022 are parallel.

## Parallel Example: User Story 3

```text
T010 contract tests | T011 integration test | T012 monitor unit tests | T013 schedulingAreas jest | T014 QueueSchedulingAreas jest
```

## Implementation Strategy

MVP = Phase 1–3 (engine pass). US2 hardens the once-per-wake-up rule; US3 makes the type configurable through the UI and visible in the monitor. Commit after the design artifacts and again after implementation.

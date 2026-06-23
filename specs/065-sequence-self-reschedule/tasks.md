---
description: "Task list for Sequence Self-Rescheduling into the Originating Queue Run"
---

# Tasks: Sequence Self-Rescheduling into the Originating Queue Run

**Input**: Design documents from `/specs/065-sequence-self-reschedule/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED — the project constitution (Principle II, Testing Standards) makes tests
mandatory for executable logic, and plan.md commits to them. Write each test before its
implementation and confirm it fails first.

**Organization**: Tasks are grouped by user story. US1 is the MVP (the authored, IF-gated action that
fires the sequence again via the Once-Per-Run option). US2 adds option parity, US3 the no-op-success
path, US4 the execution-log observability.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 / US4 (Setup, Foundational, Polish carry no story label)

## Path Conventions

Backend: `src/GameBot.Domain/`, `src/GameBot.Service/`, tests under `tests/{unit,contract,integration}/`.
Web UI: `src/web-ui/src/` with colocated `__tests__/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm a green baseline and create the new namespace location.

- [X] T001 Verify baseline is green: `dotnet build` + `dotnet test`, and `src/web-ui` `vite build` + `jest` (constitution NON-NEGOTIABLE gate — do not start implementation on a red baseline)
- [X] T002 [P] Create the `src/GameBot.Domain/Commands/SelfReschedule/` folder for the new action types

**Checkpoint**: Baseline confirmed green; namespace folder ready.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting plumbing every user story needs — the action type/payload, origin
propagation, the run registry (DI-cycle break), the runner dispatch callback, and the coordinator
skeleton. No option behavior or logging detail yet.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Add `RescheduleSelf = "reschedule-self"` to `src/GameBot.Domain/Actions/ActionTypes.cs`
- [X] T004 [P] Create `SelfRescheduleOption` enum (`AtQueueStart | OncePerRun | Timer | EveryStep`) in `src/GameBot.Domain/Commands/SelfReschedule/SelfRescheduleOption.cs`
- [X] T005 [P] Create `SelfReschedulePayload` (typed reader over `SequenceActionPayload.Parameters`: `Option`, `TimerTimeOfDay?`, `TimerRelativeOffset?`) in `src/GameBot.Domain/Commands/SelfReschedule/SelfReschedulePayload.cs` (depends on T004)
- [X] T006 [P] Add `OriginatingQueueId` (`string?`) to `src/GameBot.Service/Services/ExecutionLog/ExecutionLogContext.cs`
- [X] T007 Create `IQueueRunRegistry` (TryAdd / TryGet / Remove over active `QueueRunHandle`s) in `src/GameBot.Service/Services/QueueExecution/IQueueRunRegistry.cs`
- [X] T008 Implement `QueueRunRegistry` (singleton owning `ConcurrentDictionary<string,QueueRunHandle>`) in `src/GameBot.Service/Services/QueueExecution/QueueRunRegistry.cs` (depends on T007)
- [X] T009 Refactor `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` to use `IQueueRunRegistry` instead of the private `_runs` dictionary (register on start, lookup in `ScheduleRelative`, remove in `RunAsync` finally) (depends on T008)
- [X] T010 Add the four ephemeral registers + `SelfRescheduleEntry` record (`PendingOncePerRun`, `PendingNextCycleStart`, `PendingTimerFirings`, `EveryStepInjections`) to `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs` (depends on T004)
- [X] T011 Create `ISelfRescheduleCoordinator` + `SelfRescheduleResult` + `SelfRescheduleOutcome` (`Scheduled | NotRunning`) in `src/GameBot.Service/Services/QueueExecution/ISelfRescheduleCoordinator.cs` (depends on T004)
- [X] T012 Create `SelfRescheduleCoordinator` skeleton (ctor deps: `IQueueRunRegistry`, `ISequenceRepository`, `TimeProvider`; `ScheduleSelf` throws `NotImplemented` per option for now) in `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs` (depends on T008, T010, T011)
- [X] T013 Register `IQueueRunRegistry` and `ISelfRescheduleCoordinator` as singletons in `src/GameBot.Service/Program.cs` (depends on T008, T012)
- [X] T014 Add optional `actionDispatcher` callback (`Func<SequenceActionPayload,CancellationToken,Task<ActionDispatchResult>>?`) to `SequenceRunner.ExecuteAsync`; in `ExecuteSingleStepAsync`, dispatch a `reschedule-self` action step through it before the command fallback, record the returned outcome as the step result, and never early-stop, in `src/GameBot.Domain/Services/SequenceRunner.cs` (depends on T003)
- [X] T015 In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`: read `parentContext.OriginatingQueueId`, copy it onto every child `ExecutionLogContext` it builds (origin-through-nesting, FR-018), and supply an `actionDispatcher` that calls `ISelfRescheduleCoordinator` (or short-circuits to no-op when the id is empty) (depends on T006, T012, T014)
- [X] T016 In `QueueExecutionService.RunOneSequenceAsync`, set `OriginatingQueueId = queue.Id` on the parent `ExecutionLogContext` so queue-driven firings are marked as queue-originated, in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (depends on T006)

### Foundational tests

- [X] T017 [P] Unit test `QueueRunRegistry` add/get/remove + isolation in `tests/unit/Queues/QueueRunRegistryTests.cs` (depends on T008)
- [X] T018 [P] Unit test `SelfReschedulePayload` parsing of option + timer fields from a `SequenceActionPayload` in `tests/unit/Sequences/SelfReschedulePayloadTests.cs` (depends on T005)
- [X] T019 [P] Unit test `SequenceRunner` dispatches a `reschedule-self` action step through the callback, records the outcome, and continues (non-terminating, FR-012); also assert the dispatch path performs no I/O / ADB call (SC-006), in `tests/unit/Sequences/SequenceRunnerActionDispatchTests.cs` (depends on T014)

**Checkpoint**: Action type, payload, origin propagation, registry, runner dispatch, and coordinator
shell are in place and unit-covered. User stories can now begin.

---

## Phase 3: User Story 1 - Author a self-reschedule action gated by an IF condition (Priority: P1) 🎯 MVP

**Goal**: An author can place a self-reschedule action under an IF branch; when the condition is true
during a queue run the sequence fires again within the same run (via the Once-Per-Run option); when
false, no extra firing occurs.

**Independent Test**: Add the action under an IF branch forced true, run a queue containing the
sequence, confirm a second firing during the same run; force the condition false in another run and
confirm no additional firing.

### Tests for User Story 1 ⚠️ (write first, ensure they FAIL)

- [X] T020 [P] [US1] Contract test: a sequence with a `reschedule-self` action (option `OncePerRun`) round-trips unchanged through create/read/update, and the action validates as accepted, in `tests/contract/Sequences/SelfRescheduleActionContractTests.cs`
- [X] T021 [P] [US1] Unit test: `SelfRescheduleCoordinator.ScheduleSelf` with `OncePerRun` injects a `SelfRescheduleEntry` into the run's `PendingOncePerRun`, in `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`
- [X] T021a [P] [US1] Unit test: `ScheduleSelf` against a queue with no active run (run removed mid-sequence) returns `NotRunning` and is treated as a logged no-op (data-model §5 race), in `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`
- [X] T022 [P] [US1] Unit test: the run loop drains `PendingOncePerRun` after the once-per-run pass of the current cycle (fires before the cycle ends) and counts the firing toward `executed`, in `tests/unit/Queues/QueueExecutionServiceTests.cs`
- [X] T022a [P] [US1] Unit/integration test: two accepted self-reschedules of the same sequence in one run produce two independent firings (validates the list-based registers vs. most-recent-wins, edge case "Multiple self-reschedules in one run"), in `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`
- [X] T023 [P] [US1] Integration test: a queue run whose sequence self-reschedules behind an IF forced true produces a second firing; forced false produces exactly one firing; also assert that when the sequence already appears multiple times in the template the reschedule *adds* a firing rather than altering existing entries (edge case), in `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`
- [X] T023a [P] [US1] Integration test: a nested child sequence of a queue-driven run self-reschedules into the parent's queue run — origin propagates through nesting (FR-018, clarification Q5), in `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`
- [X] T024 [P] [US1] web-ui Jest: author a `reschedule-self` action inside an IF branch in the sequence editor and confirm it serializes/round-trips, in `src/web-ui/src/pages/__tests__/SequencesPage.reschedule.spec.tsx`

### Implementation for User Story 1

- [X] T025 [US1] Implement `ScheduleSelf` for `OncePerRun` (enqueue onto `PendingOncePerRun`, return `Scheduled`) in `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`
- [X] T026 [US1] In the run loop, drain `PendingOncePerRun` immediately after the once-per-run pass within the current cycle, firing each entry via `RunOneSequenceAsync` and counting toward `executed`, in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (depends on T025)
- [X] T027 [US1] In the `SequenceExecutionService` dispatcher, record the action as a sequence step with outcome `scheduled`/`executed` + a message (basic decision entry) for the queue-run case; the no-queue no-op wording is handled in US3 (T046), in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`
- [X] T028 [US1] Recognize `reschedule-self` as a supported action type and require a valid `option` in `src/GameBot.Domain/Services/ActionPayloadValidationService.cs`
- [X] T029 [US1] Add the `reschedule-self` action type to the sequence editor (third `actionType` alongside `command`/`WaitForImage`) with a `Once Per Run` option and payload serialization, in `src/web-ui/src/pages/SequencesPage.tsx` and `src/web-ui/src/types/sequenceFlow.ts`

**Checkpoint**: US1 fully functional — the IF-gated action fires the sequence again (Once Per Run)
within the run and round-trips through authoring.

---

## Phase 4: User Story 2 - Choose any schedule option when rescheduling (Priority: P1)

**Goal**: The action offers all four schedule options with the same inputs as the queue-template
editor, each firing at the moment its option dictates.

**Independent Test**: Configure the action with each option (and timer params), run the queue, and
confirm the re-firing occurs at the option's moment (immediately/next step for Once Per Run; at the
elapsed/target time for Timer; after each subsequent step for After Every Step; next cycle start for
At Queue Start, with non-cycling fallback to the next iteration boundary).

### Tests for User Story 2 ⚠️ (write first, ensure they FAIL)

- [X] T030 [P] [US2] Unit test: `Timer` relative offset resolves `now+offset` and fires once at the first boundary at/after it (fake `TimeProvider`); include the offset-0 case firing at the next boundary (edge case), in `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`
- [X] T031 [P] [US2] Unit test: `Timer` time-of-day resolves to `today@time` (past ⇒ next boundary) and fires once, in `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`
- [X] T032 [P] [US2] Unit test: `EveryStep` fires after each subsequent normal step and is loop-safe/idempotent per sequence (no unbounded self-chain), in `tests/unit/Queues/QueueExecutionServiceTests.cs`
- [X] T033 [P] [US2] Unit test: `AtQueueStart` on a cycling run fires at next cycle start; on a non-cycling run falls back to the next iteration boundary, in `tests/unit/Queues/QueueExecutionServiceTests.cs`
- [X] T034 [P] [US2] Contract test: validation rejects `Timer` with both/neither timer fields, a negative offset, and a timer field on a non-`Timer` option, in `tests/contract/Sequences/SelfRescheduleActionContractTests.cs`
- [X] T035 [P] [US2] web-ui Jest: the option dropdown offers all four options and, for `Timer`, shows time-of-day vs relative-offset inputs matching the queue-template editor, in `src/web-ui/src/pages/__tests__/SequencesPage.reschedule.spec.tsx`

### Implementation for User Story 2

- [X] T036 [US2] Implement `ScheduleSelf` `Timer` (relative + time-of-day → resolved `FireAt` into `PendingTimerFirings`, via `TimeProvider`) in `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`
- [X] T037 [US2] Implement `ScheduleSelf` `EveryStep` (idempotent insert into `EveryStepInjections` keyed by sequence id) in `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`
- [X] T038 [US2] Implement `ScheduleSelf` `AtQueueStart` (cycling ⇒ `PendingNextCycleStart`; non-cycling ⇒ `PendingOncePerRun` fallback, decided from `queue.CycleExecution`) in `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`
- [X] T039 [US2] Run loop: drain `PendingTimerFirings` at each iteration boundary when `now ≥ FireAt`, fire once, remove, count toward `executed`, in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`
- [X] T040 [US2] Run loop: drain a snapshot of `EveryStepInjections` after each subsequent normal step (loop-safe; not counted toward `executed`), in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`
- [X] T041 [US2] Run loop: drain `PendingNextCycleStart` at the top of the next cycle before the once-per-run pass, in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`
- [X] T042 [US2] Extend validation: `Timer` mutual-exclusivity + non-negative offset (reuse feature 059 `RelativeOffsetParser` bound) + option-specific timer rules, in `src/GameBot.Domain/Services/ActionPayloadValidationService.cs`
- [X] T043 [US2] web-ui: option dropdown (all four) + `Timer` time-of-day/relative-offset inputs mirroring `QueueEntryList.tsx`, in `src/web-ui/src/pages/SequencesPage.tsx` (+ a `components/sequences/RescheduleActionConfig.tsx` if the page grows too large)

**Checkpoint**: US1 and US2 both work — every option fires at its correct moment with template-parity
inputs.

---

## Phase 5: User Story 3 - No-op with success when not started from a queue (Priority: P1)

**Goal**: A sequence containing the action runs correctly outside any queue — the action does nothing
and reports success, with a clear log note.

**Independent Test**: Run a sequence with the action standalone (not from a queue); the action is
recorded executed-but-no-op with success, the sequence completes normally, and nothing is scheduled.

### Tests for User Story 3 ⚠️ (write first, ensure they FAIL)

- [X] T044 [P] [US3] Unit test: the dispatcher returns a success no-op (no coordinator call) when `OriginatingQueueId` is null/empty, in `tests/unit/Sequences/SequenceRunnerActionDispatchTests.cs`
- [X] T045 [P] [US3] Integration test: a standalone sequence run (no queue) records the action as success no-op, schedules nothing, and runs the remaining steps, in `tests/integration/Sequences/SelfRescheduleStandaloneIntegrationTests.cs`

### Implementation for User Story 3

- [X] T046 [US3] In the dispatcher, when `OriginatingQueueId` is empty, perform no scheduling and record a success no-op step with reason "no originating queue, no reschedule performed", in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`
- [X] T047 [US3] Confirm the standalone `sequences/{id}/execute` path leaves `OriginatingQueueId` unset (no parent queue context); add a regression assertion in `tests/integration/Sequences/SelfRescheduleStandaloneIntegrationTests.cs`

**Checkpoint**: The same sequence is safe in both queue and standalone contexts.

---

## Phase 6: User Story 4 - See the reschedule reflected in the execution logs (Priority: P2)

**Goal**: Each self-reschedule decision and the firing it produces are visible and attributable in the
execution logs.

**Independent Test**: Trigger a self-reschedule, let the firing occur, and confirm the logs show the
action (option + resolved timing + current-run-only) and the resulting attributable firing; and that
an accepted-but-never-due reschedule shows "did not fire" without failing the run.

### Tests for User Story 4 ⚠️ (write first, ensure they FAIL)

- [X] T048 [P] [US4] Integration test: logs show the action entry (chosen option, resolved timing, current-run-only) and the rescheduled firing tagged as self-reschedule-originated and attributable to the sequence, in `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`
- [X] T049 [P] [US4] Unit test: a reschedule accepted but not yet due when the run is stopped is abandoned (no firing), the run is not marked failed, and the log indicates it did not fire (FR-015/FR-017), in `tests/unit/Queues/QueueExecutionServiceTests.cs`

### Implementation for User Story 4

- [X] T050 [US4] Enrich the action decision log entry with `option`, resolved timing (target instant / "next cycle" / "this cycle"), and `outcome` (`scheduled`|`noop` + reason), in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`
- [X] T051 [US4] Tag self-reschedule-originated firings in `RunOneSequenceAsync` with a "scheduled by self-reschedule" note + originating action id for attribution, in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`
- [X] T052 [US4] Ensure accepted-but-never-due entries are discarded at run end without affecting success/failure (run termination stays governed by once-per-run), in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`

**Checkpoint**: All four user stories independently functional and observable.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T053 [P] Update `docs/architecture.md` — add the self-reschedule action to the domain model, capability map, and sequence-action surface; refresh the "Last reviewed" date (constitution Principle V)
- [X] T054 [P] Set this spec's `Status` line to Implemented and update `specs/STATUS.md` accordingly
- [X] T055 [P] Add a perf note for the run-loop draining change (O(pending) per boundary, no I/O) to the PR description / plan (SC-006, Principle IV)
- [X] T056 Run `quickstart.md` end-to-end and confirm full green gates: `dotnet test` + `src/web-ui` `vite build` + `jest`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
- **US1 (Phase 3)**: depends on Foundational. MVP.
- **US2 (Phase 4)**: depends on Foundational; builds on US1's coordinator/run-loop scaffolding (shares those files) but is independently testable per option.
- **US3 (Phase 5)**: depends on Foundational (origin context); small, mostly independent of US1/US2.
- **US4 (Phase 6)**: depends on Foundational; logging detail observes firings produced by US1/US2.
- **Polish (Phase 7)**: depends on all targeted stories being complete.

### Within Each User Story

- Write tests first and confirm they fail.
- Coordinator option logic → run-loop drain → validation → web-ui.
- Same-file tasks (the coordinator and `QueueExecutionService` run loop are touched by US1/US2/US4) are **not** marked `[P]` across stories — sequence them to avoid conflicts.

### Parallel Opportunities

- Setup T002 ∥ nothing else needed.
- Foundational: T003, T004, T006 are `[P]` (distinct files); T005 after T004; the registry chain (T007→T008→T009) and coordinator chain (T010/T011→T012→T013) are sequential; foundational tests T017–T019 are `[P]`.
- Within a story, all test tasks marked `[P]` run together; web-ui (T024/T029, T035/T043) is `[P]` against backend tasks (different files).
- US3 (Phase 5) can largely proceed in parallel with US2 — it touches the dispatcher no-op path, not the coordinator option logic.

---

## Parallel Example: User Story 1

```bash
# Tests first (all parallel — different files):
Task: "Contract test SelfRescheduleActionContractTests.cs"
Task: "Unit test coordinator OncePerRun injection"
Task: "Unit test run-loop drains PendingOncePerRun"
Task: "Integration test IF-gated second firing"
Task: "web-ui Jest author action under IF"

# Then implementation (T025→T026 sequential; T027/T028/T029 parallelizable across files)
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → 2 Foundational (critical, blocks everything).
2. Phase 3 US1 → **STOP and validate**: IF-gated Once-Per-Run reschedule fires the sequence again.
3. Demo the MVP.

### Incremental Delivery

1. Foundation ready.
2. US1 (Once Per Run) → validate → demo.
3. US2 (all options) → validate each option's timing → demo.
4. US3 (no-op success) → validate standalone safety → demo.
5. US4 (log observability) → validate attribution + not-due handling → demo.
6. Polish: architecture doc, spec status, perf note, full quickstart + green gates.

---

## Notes

- `[P]` = different files, no incomplete dependency.
- The coordinator (`SelfRescheduleCoordinator.cs`) and the run loop (`QueueExecutionService.cs`) are
  edited across US1/US2/US4 — keep those edits sequential within a single working copy.
- Reuse `TimeProvider` (feature 059) for all wall-clock reads so timer tests stay deterministic.
- Keep everything ephemeral: nothing about a reschedule may touch the queue template or any saved
  config (FR-010, SC-005) — assert this in T020/T023.
- Verify each test fails before implementing; commit after each task or logical group.

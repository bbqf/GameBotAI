# Tasks: Step-Through Sequence Execution

**Input**: Design documents in `C:\src\GameBot\specs\127-step-through-sequence-execution\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/step-through-api.md, quickstart.md

**Tests**: Tests are required. Constitution Principle II requires tests for all executable logic.

**Format**: `- [ ] T### [P] [US#] Description with file path`. `[P]` means the task can run in parallel
(different file, no open dependency).

**STE**: All text and code comments in these tasks obey Principle VI.

## Path Conventions

- Domain: `src/GameBot.Domain/Services/`
- Service: `src/GameBot.Service/`
- Web UI: `src/web-ui/src/`
- Tests: `tests/unit/`, `tests/integration/`, `tests/contract/`

---

## Phase 1: Setup

- [X] T001 Create the folder `src/GameBot.Domain/Services/StepThrough/` and the folder `src/GameBot.Service/Services/StepThrough/`
- [X] T002 [P] Create the folder `src/web-ui/src/components/stepthrough/` with a `__tests__` sub-folder
- [X] T003 [P] Create the test folders `tests/unit/StepThrough/`, `tests/integration/StepThrough/`, and `tests/contract/StepThrough/`

---

## Phase 2: Foundational (blocks all user stories)

**Goal**: Shared model, path logic, runner entry point, preview option, log origin, and session guard.

- [X] T004 [P] Add the types `StepNode`, `Frame`, `FrameKind`, and `HistoryEntry` in `src/GameBot.Domain/Services/StepThrough/StepThroughModels.cs` (fields from data-model.md)
- [X] T005 [P] Write unit tests for path build, parse, and flatten (nested loop body, if then, if else, steps with empty `StepId`) in `tests/unit/StepThrough/StepPathTests.cs`
- [X] T006 Implement `StepPath` (parse, build, flatten to `StepNode` list with `depth` and `selectable`) in `src/GameBot.Domain/Services/StepThrough/StepPath.cs`
- [X] T007 In `src/GameBot.Domain/Services/SequenceRunner.cs`, change `EvaluateLoopConditionAsync`, `EvaluateStepGuardAsync`, `ResolveCondition`, `DescribeBreakCondition`, `EvaluateResolvedConditionAsync`, and `ExecuteSingleStepAsync` (leaf part) to `internal`. Do not change their behavior
- [X] T008 Add an `internal` method `ExecuteLeafAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs`. It runs one step that is not a container. It does the guard, delay, gate, wait-for-image, action dispatch, and command dispatch. It returns a step result. Real runs must call the same code
- [X] T009 [P] Add `ExecutionOptions` (with `PreviewEffects`) and the optional argument on `ForceExecuteDetailedAsync` in `src/GameBot.Service/Services/CommandExecutor.cs` and its interface. Add `PreviewedEffects` to `CommandForceExecutionResult`. The default keeps old behavior
- [X] T010 Write unit tests for preview mode (reschedule-self and notify are skipped and listed, tap still runs, null options keep old behavior) in `tests/unit/StepThrough/CommandExecutorPreviewTests.cs`
- [X] T011 Implement the preview rule in `src/GameBot.Service/Services/CommandExecutor.cs` for `reschedule-self` and `notify` (use the pure fire-time part of `ISelfRescheduleCoordinator`; write no state)
- [X] T012 [P] Add the nullable `Origin` field to `ExecutionLogContext`, the log entry DTO, and the mapper in `src/GameBot.Service/Services/ExecutionLog/`. Add the `?origin=` filter to the list endpoint
- [X] T013 [P] Write a contract test that lists every sequence action type and marks it `runs` or `previews`. It must fail for an unlisted type in `tests/contract/StepThrough/ActionTypePreviewListTests.cs`
- [X] T014 [P] Write unit tests for the session guard (no queue, queue running, queue already paused, pause, resume only when paused by us) in `tests/unit/StepThrough/StepThroughSessionGuardTests.cs`
- [X] T015 Implement `StepThroughSessionGuard` (find the owning queue by device serial through `IQueueRunRegistry`; pause with `EnterPolicyPause("step-through", now)`; resume with `ResumeFromPolicyPause`) in `src/GameBot.Service/Services/StepThrough/StepThroughSessionGuard.cs`

**Checkpoint**: Foundation builds. Run `dotnet build` and the new unit tests. Fix all failures before Phase 3.

---

## Phase 3: User Story 1 - Run the next step automatically (P1) MVP

**Goal**: Start a step-through, run one step per push, see status, see the marker move, see "complete".

**Independent test**: A saved 3-step sequence. Three pushes run three steps in order. After the last one the state is `complete`.

### Tests for US1

- [X] T016 [P] [US1] Unit tests for the cursor and frames: linear order, count loop, while loop, repeat-until loop, `maxIterations`, if then and else, break, failed step, step guard skipped, end of sequence, a command reference that was deleted (same failure message as a real run), and the sequence time limit (it does not apply, and a step keeps its own timeout) in `tests/unit/StepThrough/SequenceStepperTests.cs`
- [X] T017 [P] [US1] Parity test: run each fixture through the real runner (dry run) and through the stepper. Compare the order of visited paths (SC-002) in `tests/integration/StepThrough/StepperParityTests.cs`
- [X] T018 [P] [US1] Unit tests for ending steps (`break` outside a loop, `fail`, `reschedule-self`): cursor goes to the next step, entry has the note "sequence would end here". A `break` outside a loop has the note "no loop is active" in `tests/unit/StepThrough/SequenceStepperEndingTests.cs`
- [X] T019 [P] [US1] Unit test that `lastRun` is false and the entry says "lastRun is not evaluated" in `tests/unit/StepThrough/SequenceStepperLastRunTests.cs`
- [X] T020 [P] [US1] Contract tests for `POST /api/step-through`, `GET /api/step-through/{id}`, `POST .../run-next`, `POST .../cancel`, `DELETE .../{id}`. Test these errors: `sequence_not_found`, `session_unavailable`, `session_in_use`, `step_running`, `sequence_complete`, and `queue_running` (with `queueId`, `queueName`, and `canPause` in `details`) in `tests/contract/StepThrough/StepThroughRunContractTests.cs`
- [X] T021 [P] [US1] Web UI tests: panel shows nodes with indent, marker, running state, disabled button while running, "complete" state in `src/web-ui/src/components/stepthrough/__tests__/StepThroughPanel.test.tsx`

### Implementation for US1

- [X] T022 [US1] Implement `SequenceStepper` (cursor, frame stack, next-step rules, one leaf per call, no time limit scope, entry notes) in `src/GameBot.Domain/Services/StepThrough/SequenceStepper.cs`
- [X] T023 [US1] Add the `IStepThroughService` interface in `src/GameBot.Service/Services/StepThrough/IStepThroughService.cs`
- [X] T024 [US1] Implement `StepThroughService` (create, get, run-next in a background task with its own `CancellationTokenSource`, cancel, delete, per-session limit, version hash, the `queue_running` refusal with `StepThroughSessionGuard`, `PreviewEffects`, log origin `step-through`) in `src/GameBot.Service/Services/StepThrough/StepThroughService.cs`
- [X] T025 [US1] Implement the routes for start, get, run-next, cancel, and delete in `src/GameBot.Service/Endpoints/StepThroughEndpoints.cs`. Map them in the endpoint setup. Keep `Program.cs` thin
- [X] T026 [US1] Register services and endpoints in the DI setup used by the existing services (same file pattern as the queue services)
- [X] T027 [P] [US1] Add DTO descriptions for the new types in a new file `src/GameBot.Service/Swagger/StepThroughSchemaFilter.cs`. Register it in `src/GameBot.Service/Swagger/SwaggerConfig.cs`
- [X] T028 [P] [US1] Implement the API client in `src/web-ui/src/services/stepThrough.ts`
- [X] T029 [US1] Implement the `useStepThrough` hook (start, poll every 2 s and every 500 ms while running, run, cancel, end on unmount, `sendBeacon` on page close) in `src/web-ui/src/components/stepthrough/useStepThrough.ts`
- [X] T030 [US1] Implement `StepThroughPanel` (game session picker, node list with indent and marker, **Run next step**, **Cancel step**, and the states `running` and `complete`) in `src/web-ui/src/components/stepthrough/StepThroughPanel.tsx`
- [X] T031 [US1] Open the panel from the Sequences page with a **Step through** button in `src/web-ui/src/pages/SequencesPage.tsx`

**Checkpoint**: US1 works alone. Run T016 to T021 and `npm run build`. Do the first row of the quickstart check list.

---

## Phase 4: User Story 2 - Choose the next step manually (P1)

**Goal**: Select any step as the next step. Repeat a step. Start at a middle step.

**Independent test**: Select step 3, run it, select step 1, run it. Both runs are in the history. The marker follows.

- [X] T032 [P] [US2] Unit tests for `select`: jump into a loop body (frames open at iteration 1), jump back, repeat a failed step, header row not selectable, after-run cursor follows the normal order from the chosen step in `tests/unit/StepThrough/SequenceStepperSelectTests.cs`
- [X] T033 [P] [US2] Contract tests for `POST .../select` and for `startPath` on start (`unknown_step`, `not_selectable`, `step_running`, `sequence_changed`) in `tests/contract/StepThrough/StepThroughSelectContractTests.cs`
- [X] T034 [P] [US2] Web UI tests: click a row to select, marker moves, header rows are not clickable, **Run next step** stays enabled after "complete" when a step is selected in `src/web-ui/src/components/stepthrough/__tests__/StepThroughSelect.test.tsx`
- [X] T035 [US2] Implement `SelectAsync` and `startPath` in `src/GameBot.Domain/Services/StepThrough/SequenceStepper.cs` and `src/GameBot.Service/Services/StepThrough/StepThroughService.cs`
- [X] T036 [US2] Add the `select` route in `src/GameBot.Service/Endpoints/StepThroughEndpoints.cs`
- [X] T037 [US2] Add the select action and the row click handler to `src/web-ui/src/services/stepThrough.ts` and `src/web-ui/src/components/stepthrough/StepThroughPanel.tsx`

**Checkpoint**: US1 and US2 pass together. Do the second row of the quickstart check list.

---

## Phase 5: User Story 3 - Run history, status, values, and queue safety (P2)

**Goal**: Show history like the execution log. Restart. Set values. Handle a queue that owns the session. Handle a lost view.

**Independent test**: Run four steps (one fails). The history shows four entries. **Restart** clears them.

- [X] T038 [P] [US3] Unit tests for history: cap at 1,000, `afterSeq` filter, iteration number on loop entries, `effects` for previewed actions, restart keeps the parameter values and the queue pause, and clears the outcomes, in `tests/unit/StepThrough/StepThroughHistoryTests.cs`
- [X] T039 [P] [US3] Unit tests for values: change between steps keeps history and cursor, absent outcome behaves like a real run, refused while running in `tests/unit/StepThrough/StepThroughValuesTests.cs`
- [X] T040 [P] [US3] Unit tests for the lease sweeper: expired lease cancels the running step, resumes a queue paused by us, leaves a queue that was already paused, removes the session in `tests/unit/StepThrough/StepThroughLeaseSweeperTests.cs`
- [X] T041 [P] [US3] Contract tests for `restart` (keeps the queue pause and the parameter values), `values`, `pause-queue`, `queue_run_active`, and `no_owning_queue` in `tests/contract/StepThrough/StepThroughQueueContractTests.cs`
- [X] T042 [P] [US3] Contract test that a step-through of `reschedule-self` (top level and inside a command) changes no queue schedule and no daily record (SC-006) in `tests/contract/StepThrough/StepThroughNoSideEffectTests.cs`
- [X] T043 [P] [US3] Web UI tests: history list, restart, parameter form, queue banner with **Pause queue** in `src/web-ui/src/components/stepthrough/__tests__/StepThroughHistory.test.tsx`
- [X] T044 [US3] Implement history, restart, and values in `src/GameBot.Service/Services/StepThrough/StepThroughService.cs`
- [X] T045 [US3] Implement `pause-queue` and the resume on end (not on restart) in `src/GameBot.Service/Services/StepThrough/StepThroughService.cs` using `StepThroughSessionGuard`
- [X] T046 [US3] Implement `StepThroughLeaseSweeper` (hosted service, 10 s sweep, 90 s lease) in `src/GameBot.Service/Services/StepThrough/StepThroughLeaseSweeper.cs`. Register it
- [X] T047 [US3] Add the `restart`, `values`, and `pause-queue` routes in `src/GameBot.Service/Endpoints/StepThroughEndpoints.cs`
- [X] T048 [US3] Add the history list, **Restart**, parameter and outcome form, and queue banner to `src/web-ui/src/components/stepthrough/StepThroughPanel.tsx` and `src/web-ui/src/services/stepThrough.ts`
- [X] T049 [P] [US3] Show a **step-through** badge and the origin filter in the execution log view. Change `src/web-ui/src/pages/ExecutionLogs.tsx` and `src/web-ui/src/services/executionLogsApi.ts`. Add a test in `src/web-ui/src/pages/__tests__/ExecutionLogs.stepThrough.test.tsx`

**Checkpoint**: Do the last three rows of the quickstart check list.

---

## Phase 6: User Story 4 - Saved sequences only (P2)

**Goal**: Block the step-through for a new or edited sequence. Detect a change after start.

**Independent test**: A new sequence has the entry disabled with a reason. Save it and the entry is enabled. Edit a step and it is disabled again.

- [X] T050 [P] [US4] Web UI tests: disabled with reason for a new sequence, enabled after save, disabled when the form is dirty in `src/web-ui/src/pages/__tests__/SequencesPage.stepThrough.spec.tsx`
- [X] T051 [P] [US4] Contract test: change the stored sequence after start, then `run-next` and `select` return `409 sequence_changed`. Also test an empty sequence and a legacy flow-graph sequence in `tests/contract/StepThrough/StepThroughVersionContractTests.cs`
- [X] T052 [US4] Add the saved-only rule and the reason text to the **Step through** button in `src/web-ui/src/pages/SequencesPage.tsx`
- [X] T053 [US4] Show the `sequence_changed` message with a **Restart** action in `src/web-ui/src/components/stepthrough/StepThroughPanel.tsx`

**Checkpoint**: All four stories pass.

---

## Phase 7: Polish and Cross-Cutting

- [X] T054 Update `docs/architecture.md` (API surface, new services, preview option, log origin) and refresh the "Last reviewed" date (Principle V)
- [X] T055 [P] Add the spec to `specs/STATUS.md`. Set the `Status` line in `specs/127-step-through-sequence-execution/spec.md` to Implemented
- [X] T056 [P] Add a changelog entry in STE for the user-visible feature
- [X] T057 [P] Check all new text (messages, comments, API descriptions, UI text) against Principle VI
- [X] T058 Measure the state read time (limit: p95 under 50 ms) and the status delay after a step ends (SC-003, limit: 1 second). Write a perf note for the PR
- [X] T059 Run `dotnet build`, all .NET tests, `npm run build`, and `npx jest`. Fix every failure. Check the coverage of the new code (80% line, 70% branch). Do not use lint or `tsc --noEmit` as the gate (known old failures)
- [ ] T060 Do the full quickstart check list on the local service with a real emulator session. Time the start to the first step (SC-001, limit: 30 seconds)

---

## Dependencies and Order

- Phase 1 then Phase 2 then the user stories. Phase 2 blocks all stories.
- US1 is the MVP. US2 needs the stepper and service from US1.
- US3 needs US1. It is independent of US2.
- US4 needs the panel from US1 and the version hash from T024.
- Phase 7 comes last.
- Inside each story: tests first (they must fail), then implementation.
- T007 then T008. T008 then T022. T009 then T010 then T011. T011 then T024 (preview).
- T015 then T024. T015 then T045. T006 then T022.

## Parallel Examples

- Phase 2: T004, T005, T009, T012, T013, and T014 touch different files and can run together.
- US1 tests: T016 to T021 can run together.
- US1 after the service exists: T028 (client) and T027 (Swagger) can run with T025.
- US3 tests: T038 to T043 can run together.

## Implementation Strategy

**MVP**: Phase 1, Phase 2, and Phase 3 (US1). It gives start, run next, status, and the complete state.
Stop and check the first quickstart row on a real emulator.

**Increments**:
1. Add US2 (manual selection). It is a small change on top of US1.
2. Add US3 (history, values, pause-queue, lease). The queue refusal (`queue_running`) is part of T024, so US1 already has it.
3. Add US4 (saved-only rule).

**Safety note**: The queue refusal in `run-next` is a safety rule. It is in T024 and is tested in T020.

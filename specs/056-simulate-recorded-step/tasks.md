# Tasks: Simulate Recorded Step

**Input**: Design documents from `specs/056-simulate-recorded-step/`  
**Prerequisites**: plan.md âœ… spec.md âœ… research.md âœ… data-model.md âœ… contracts/ âœ…

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

---

## Phase 1: Setup (New Files)

**Purpose**: Create new file skeletons so parallel work in Phase 2 can proceed without merge conflicts.

- [X] T001 [P] Create `src/GameBot.Service/Endpoints/StepsEndpoints.cs` with empty class and minimal API registration scaffold (no logic yet)
- [X] T002 [P] Create `src/web-ui/src/components/commands/VisualStepPicker/stepUtils.ts` as empty module (no logic yet)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before any user story can be implemented.

**âš ï¸ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Extract single-step dispatch from `ExecuteCommandRecursiveAsync` into a private `ExecuteOneStepAsync(sessionId, step, context, ct)` helper in `src/GameBot.Service/Execution/CommandExecutor.cs`; refactor existing loop to call the helper (no behaviour change)
- [X] T004 Add `Task<CommandForceExecutionResult> ForceExecuteStepAsync(string? sessionId, CommandStep step, CancellationToken ct)` to `src/GameBot.Service/Execution/ICommandExecutor.cs` and implement it in `src/GameBot.Service/Execution/CommandExecutor.cs` by delegating to `ExecuteOneStepAsync` after session resolution (depends on T003)
- [X] T005 [P] Add `StepExecutionStatus = 'idle' | 'running' | 'success' | 'error'` type, `StepRunResult` type, `executionStatus: StepExecutionStatus` and optional `errorMessage?: string` fields to all three `RecordedStep` variants, and `isExecuting: boolean` to `PickerState` in `src/web-ui/src/types/picker.ts`; update all `ADD_STEP` dispatch sites to initialize with `executionStatus: 'idle'`
- [X] T006 [P] Add `executeStep(step: CommandStepDto, sessionId?: string)` function to `src/web-ui/src/services/commands.ts` that posts to `POST /api/steps/execute` with body `{ step, sessionId }`

**Checkpoint**: Foundation ready â€” all three user story phases can now proceed.

---

## Phase 3: User Story 1 â€” Run Last Recorded Step (Priority: P1) ðŸŽ¯ MVP

**Goal**: A user can click Run on the most recently recorded step and see the emulator execute it, with inline success/error feedback, without leaving the recorder.

**Independent Test**: Open the recorder, record one tap step, click Run, observe the emulator tap and a âœ“ badge on the step. Edit the step (delete and re-record) and verify the badge resets to idle.

### Tests for User Story 1

- [X] T007 [P] [US1] Write xUnit integration test for `POST /api/steps/execute` covering: successful PrimitiveTap execution returns 202 with `status: executed`; missing `referenceImageId` returns 400; emulator unavailable returns 503; 10s timeout returns 200 with `status: timeout` â€” in `src/GameBot.Service.Tests/Endpoints/StepsEndpointsTests.cs`
- [X] T008 [P] [US1] Write Jest unit tests for `usePickerState` covering: `RUN_STEP_START` sets step to running + sets `isExecuting`; `RUN_STEP_COMPLETE` with success sets step to success + clears `isExecuting`; `RUN_STEP_COMPLETE` with error sets step to error + sets `errorMessage`; new step via `ADD_STEP` initializes with `executionStatus: 'idle'` â€” in `src/web-ui/src/components/commands/VisualStepPicker/__tests__/usePickerState.run.test.ts`

### Implementation for User Story 1

- [X] T009 [US1] Implement `POST /api/steps/execute` body in `src/GameBot.Service/Endpoints/StepsEndpoints.cs`: deserialize `ExecuteStepRequest`, validate step (reject `Command` type; run existing `ValidateStep` checks), convert to domain via `ToDomainStep()`, create `CancellationTokenSource(10s)` linked to request CT, call `ForceExecuteStepAsync`, handle timeout by returning 200 with `status: "timeout"` outcome (depends on T001, T004)
- [X] T010 [US1] Implement `toCommandStepDto(step: RecordedStep): CommandStepDto` in `src/web-ui/src/components/commands/VisualStepPicker/stepUtils.ts` covering all three step types: PrimitiveTap (imageId â†’ referenceImageId, offsetX/Y), KeyInput (key), Swipe (startX/Y, endX/Y, durationMs) (depends on T002, T005)
- [X] T011 [US1] Add `RUN_STEP_START` and `RUN_STEP_COMPLETE` reducer cases to `usePickerState.ts`: `RUN_STEP_START` sets `isExecuting: true` and target step's `executionStatus: 'running'`; `RUN_STEP_COMPLETE` sets `isExecuting: false`, updates step to `success` or `error` with `errorMessage` from `StepRunResult` (depends on T005)
- [X] T012 [US1] Implement exported `runStep(id: string): Promise<void>` in `usePickerState.ts`: dispatch `RUN_STEP_START`, call `toCommandStepDto` + `executeStep`, dispatch `RUN_STEP_COMPLETE` with result; map `status !== 'executed'` as error (depends on T006, T010, T011)
- [X] T013 [US1] Add **â–¶ Run** button to each step row in `src/web-ui/src/components/commands/VisualStepPicker/RecordedStepList.tsx`: disabled when `step.executionStatus === 'running'` or `isExecuting`; calls `onRunStep(step.id)` prop; shows spinner icon when `executionStatus === 'running'` (depends on T011)
- [X] T014 [US1] Add execution status badge to step row in `src/web-ui/src/components/commands/VisualStepPicker/RecordedStepList.tsx`: âœ“ when `success`; âœ— with `errorMessage` tooltip when `error`; no badge when `idle` (depends on T013)
- [X] T015 [US1] Disable drag handle and remove button in `RecordedStepList.tsx` when `isExecuting` is true; wire `onRunStep` and `isExecuting` props from `VisualStepPicker.tsx`; strip `executionStatus` and `errorMessage` in `handleConfirm` before calling parent `onConfirm` (depends on T013, T014)

**Checkpoint**: US1 fully functional â€” run button works on the last recorded step, emulator executes it, success/error badge appears, recorder stays open.

---

## Phase 4: User Story 2 â€” Run Any Individual Step (Priority: P2)

**Goal**: A user can click Run on any step in the list (not just the last), and only that step executes; all other Run buttons are disabled until it completes.

**Independent Test**: Record three steps, click Run on the first step, observe only that step executes; verify all other Run buttons are disabled during execution.

### Tests for User Story 2

- [X] T016 [P] [US2] Write React component test for `RecordedStepList` covering: Run button on step[0] disabled while step[1] has `executionStatus: 'running'`; Run button on step[0] enabled once step[1] completes â€” in `src/web-ui/src/components/commands/VisualStepPicker/__tests__/RecordedStepList.run.test.tsx`

### Implementation for User Story 2

- [X] T017 [US2] Verify `isExecuting` guard in `runStep` prevents concurrent dispatches: add early-return guard at the top of `runStep` in `usePickerState.ts` if `state.isExecuting` is already true (depends on T012)
- [X] T018 [US2] Confirm step-row Run buttons receive `isExecuting` correctly in `RecordedStepList.tsx` when a non-last step is running â€” no new code expected if T013/T015 wired `isExecuting` as a prop; verify by running the component test from T016 (depends on T016)

**Checkpoint**: US2 verified â€” run works on any step and concurrent execution is blocked.

---

## Phase 5: User Story 3 â€” Run All Recorded Steps in Sequence (Priority: P3)

**Goal**: A user can click Run all to execute every recorded step in order; execution stops at the first failure and highlights that step.

**Independent Test**: Record three steps, click Run all, observe emulator executes them in recorded order with step highlight; introduce a bad step in the middle, click Run all, observe execution stops at the bad step.

### Tests for User Story 3

- [X] T019 [P] [US3] Write Jest unit tests for `runAll` in `usePickerState.ts` covering: all steps succeed â†’ all statuses become `success`; step[1] fails â†’ step[1] is `error`, step[2] remains `idle`, `isExecuting` resets to false â€” in `src/web-ui/src/components/commands/VisualStepPicker/__tests__/usePickerState.run.test.ts` (extend T008 file)

### Implementation for User Story 3

- [X] T020 [US3] Add `RUN_ALL_DONE` reducer case to `usePickerState.ts`: sets `isExecuting: false` (depends on T011)
- [X] T021 [US3] Implement exported `runAll(): Promise<void>` in `usePickerState.ts`: dispatch `RUN_STEP_START` for each step sequentially, call `executeStep`, dispatch `RUN_STEP_COMPLETE`; break loop on `status !== 'executed'`; dispatch `RUN_ALL_DONE` when loop ends (success or failure) (depends on T012, T020)
- [X] T022 [US3] Add **Run all** button to toolbar in `src/web-ui/src/components/commands/VisualStepPicker/VisualStepPicker.tsx`: disabled when `steps.length === 0` or `isExecuting`; calls `runAll()` from picker state (depends on T021)
- [X] T023 [US3] Add currently-executing step highlight style in `RecordedStepList.tsx` (e.g. left border accent or row background) when `step.executionStatus === 'running'` during a multi-step run â€” leverages existing badge logic (depends on T014)

**Checkpoint**: US3 verified â€” Run all executes steps in order, stops on failure, highlights failed step.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T024 [P] Verify `executionStatus` reset on step deletion: confirm that `REMOVE_STEP` action in `usePickerState.ts` removes the step entirely (no stale status entry); verify `ADD_STEP` always initializes `executionStatus: 'idle'` (behaviour already in T005; this is a verification task â€” add assertion to T008 tests if missing)
- [X] T025 [P] Run `vite build` in `src/web-ui/` and confirm zero new type errors; run `jest` in `src/web-ui/` and confirm all new tests pass; run `dotnet test` in `src/GameBot.Service.Tests/` and confirm new integration tests pass; manually time a PrimitiveTap step execution end-to-end and confirm the result badge appears within 3 seconds (SC-002); record timing observation in the PR description

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies â€” start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion â€” blocks all user stories
- **US1 (Phase 3)**: Depends on Phase 2 â€” MVP; deliver after this phase
- **US2 (Phase 4)**: Depends on Phase 3 â€” extends US1 with verification
- **US3 (Phase 5)**: Depends on Phase 2 â€” can start after Foundational; US1 overlap makes sequential easier
- **Polish (Phase 6)**: Depends on all user story phases

### Within Each Phase

- [P] tasks have no dependencies on each other and can run in parallel
- Non-[P] tasks must complete in listed order within the phase
- Tests within each story should be written before implementation where feasible (constitution requirement)

### Parallel Opportunities

```
Phase 1:  T001 â•‘ T002
Phase 2:  T003 â†’ T004 (sequential)   â•‘   T005 â•‘ T006 (parallel with T003/T004)
Phase 3:  T007 â•‘ T008 (tests, parallel)  â†’  T009..T015 (sequential)
Phase 4:  T016 â•‘ T017  â†’  T018
Phase 5:  T019 (test)  â†’  T020 â†’ T021 â†’ T022 â†’ T023
Phase 6:  T024 â•‘ T025
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001â€“T002)
2. Complete Phase 2: Foundational (T003â€“T006)
3. Complete Phase 3: User Story 1 (T007â€“T015)
4. **STOP and VALIDATE**: Open recorder, record a step, click Run, confirm emulator executes it
5. Ship / demo MVP

### Incremental Delivery

1. Setup + Foundational â†’ backend endpoint + frontend types/service ready
2. US1 â†’ Run button per step, inline status badge (MVP)
3. US2 â†’ Verify concurrent-run prevention (no UI change expected)
4. US3 â†’ Add Run all button

---

## Notes

- `executionStatus` and `errorMessage` are UI-only fields; strip them in `handleConfirm` before passing steps to the command editor (T015)
- FR-002b ("reset on edit") is satisfied by the delete + re-record flow: `REMOVE_STEP` eliminates the status; `ADD_STEP` initializes `'idle'`. No separate `EDIT_STEP` action needed since the recorder has no in-place step editing today.
- The 10-second timeout is enforced server-side (T009). No client-side `AbortController` needed.
- [P] tasks operate on different files and have no shared mutable state â€” safe to parallelize.

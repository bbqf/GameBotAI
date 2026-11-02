# Tasks: Wait for Still Step

**Input**: Design documents in `C:\src\GameBot\specs\174-wait-for-still-step\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/wait-for-still-step.md, quickstart.md

**Tests**: Tests are included. The constitution (Principle II) and plan.md require them.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`. `[P]` means the task can run in parallel (different files, no open dependency).

**Language**: All text uses ASD-STE100 Simplified Technical English.

**Test-compile rule**: A test file compiles only when the types it names exist. Before you write or run a test, finish the task that creates each type the test uses. The "Test-compile dependencies" section lists each case.

## Phase 1: Setup

- [X] T001 Add the constant `WaitForStill = "WaitForStill"` to `src/GameBot.Domain/Actions/ActionTypes.cs`
- [X] T002 Add `ActionTypes.WaitForStill` to the `All` list in `src/GameBot.Domain/Actions/SequenceActionTypes.cs`
- [X] T003 [P] Create `src/GameBot.Domain/WaitForStill/WaitForStillOutcomes.cs` with constants `Still`, `TimeoutElapsed` and method `IsWaitForStillOutcome(string)`

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Pure Domain parts that every story uses. Each test task here needs its code task first (test-compile rule).

- [X] T004 [P] Create `src/GameBot.Domain/Vision/FrameDifference.cs`: static `Compute(Bitmap previous, Bitmap current, PixelRegion? region)` that returns the mean absolute R, G, B difference from 0 to 1 with `LockBits`. Return 1 when the frame sizes differ (research R4)
- [X] T005 [P] Create `src/GameBot.Domain/WaitForStill/WaitForStillTracker.cs`: state `previousTime`, `stableStart`; methods `Observe(double difference, DateTimeOffset now)` and `Reset()` (data-model state transitions, research R5)
- [X] T006 Create `src/GameBot.Domain/WaitForStill/WaitForStillPayload.cs`: `TryRead(SequenceActionPayload, out payload, out error)` and `Validate(SequenceActionPayload)`. Defaults: stableMs 300, maxDiff 0.01, timeoutMs 5000. Reject stableMs above timeoutMs, unknown fields, and an invalid region (use `PixelRegion.Validate`). Skip `{{name}}` references (research R8)
- [X] T007 [P] Write unit tests in `tests/unit/WaitForStill/FrameDifferenceTests.cs` (identical frames, full change, region only, different sizes). Needs T004
- [X] T008 [P] Write unit tests in `tests/unit/WaitForStill/WaitForStillTrackerTests.cs` (baseline, stable start, reset on large diff, reset after failed capture, maxDiff 0). Needs T005
- [X] T009 [P] Write unit tests in `tests/unit/WaitForStill/WaitForStillPayloadTests.cs` (defaults, each range rule, stableMs above timeoutMs, region rules, parameter reference, unknown field). Needs T006 and T001
- [X] T010 [P] Write unit tests in `tests/unit/WaitForStill/WaitForStillOutcomesTests.cs` that assert the set of outcome names of the step is exactly `still` and `timeout_elapsed`, that `IsWaitForStillOutcome` accepts only these two names, and that `failed` and `image_unavailable` are not in the set (FR-010). Needs T003

**Checkpoint**: Domain parts and their tests pass.

---

## Phase 3: User Story 1 - Wait until a pan or scroll has ended (Priority: P1) MVP

**Goal**: The step ends with `still` when the screen stops, or with `timeout_elapsed` at the time limit.

**Independent Test**: Run a sequence with a `WaitForStill` step on a screen that moves and stops. The outcome is `still`. On a screen that never stops, the outcome is `timeout_elapsed`.

**Order in this phase**: Executor tasks first (T011 to T016). Then tests that run the executor (T017, T018). The other tasks follow the listed dependencies.

- [X] T011 [US1] Create `src/GameBot.Service/Services/WaitForStill/WaitForStillHandler.cs`: `ExecuteAsync(SequenceActionPayload action, string? sessionId, CancellationToken ct)`. Capture loop with `ISessionFrameSource.Capture`, `FrameDifference`, `WaitForStillTracker`, `WaitForImageSchedule.NextWait`, `AppConfig.CaptureIntervalMs`; dispose bitmaps; keep one previous frame; return `ActionDispatchResult`. Follow research R6: a null frame or a non-cancel exception calls `tracker.Reset()`, disposes the previous frame, waits one capture interval, and tries again until the deadline; the catch for capture errors uses a filter that excludes `OperationCanceledException`; no session id waits to the deadline and returns `timeout_elapsed` with the message `the screen could not be read`. Needs T001 to T006
- [X] T012 [US1] Register `WaitForStillHandler` in `src/GameBot.Service/GameBotServiceSetup.cs`. Needs T011
- [X] T013 [US1] Route `WaitForStill` to the handler in the action dispatcher path used by `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. Needs T011 and T012
- [X] T014 [US1] Make `IsServiceLevelAction` return true for `WaitForStill` and record its outcome in `RecordServiceLevelActionAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs` (research R3). Needs T003
- [X] T015 [US1] Add the execution log entry for the step in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, next to the quiz block (research R10). Needs T013
- [X] T016 [US1] Call `WaitForStillPayload.Validate` in `src/GameBot.Domain/Services/SequenceStepValidationService.cs` and in the load check in `src/GameBot.Domain/Commands/FileSequenceRepository.cs`. Needs T006
- [X] T017 [P] [US1] Write unit tests in `tests/unit/WaitForStill/WaitForStillHandlerTests.cs` with a fake `ISessionFrameSource`: still screen, moving then still, never still, no session (outcome `timeout_elapsed`, message `the screen could not be read`). Add the capture-fault and cancel tests of research R6: (a) a null frame is a skip and retry, and the next good frame is a new baseline; (b) a capture call that throws a non-cancel exception is a skip and retry, not a failure, and the stable time restarts from zero; (c) time spent in a failed capture never counts as stable time; (d) a screen with no readable frame until the deadline ends with `timeout_elapsed`; (e) cancel during the wait passes up as `OperationCanceledException`, is not treated as a failed capture, and gives no outcome. Also assert that the result outcome is always `still` or `timeout_elapsed` for these cases (FR-010). Needs T011 (the file does not compile before the handler exists)
- [X] T018 [P] [US1] Write an integration test in `tests/integration/Sequences/WaitForStillSequenceExecutionIntegrationTests.cs` for a full sequence run (still and timeout cases). Needs T011 to T016
- [X] T019 [US1] Add the preview text in `src/GameBot.Service/Services/StepThrough/StepThroughActionPreview.cs`
- [X] T020 [P] [US1] Update `tests/unit/Sequences/SequenceActionTypeSyncTests.cs` for the new type and add a runner outcome test in `tests/unit/Sequences/`. Needs T001, T002, T014
- [X] T021 [P] [US1] Add a validation 400 contract test in `tests/contract/Sequences/` for an invalid `WaitForStill` payload. Needs T016
- [X] T022 [US1] Add the payload text for the new type in `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs` and update `tests/contract/Sequences/PrimitiveActionTypesOpenApiTests.cs`. Needs T002

**Checkpoint**: A sequence with a `WaitForStill` step runs and ends with `still` or `timeout_elapsed`.

---

## Phase 4: User Story 2 - Watch only a part of the screen (Priority: P2)

**Goal**: Only pixels inside `region` change the result. A region outside the screen is handled safely.

**Independent Test**: Change pixels outside the region and the step ends with `still`. Change a pixel inside and the step does not end with `still` while the change continues.

**Dependency**: The region code and the region tests need the executor tasks first. Do T011 to T016 (handler, registration, routing, runner, log, validation) before T023 to T025.

- [X] T023 [US2] In `src/GameBot.Service/Services/WaitForStill/WaitForStillHandler.cs`, clip the region with `PixelRegion.ClipTo`; on a null result return `failed` with the message "region is outside the screen" (research R7). Needs T011
- [X] T024 [P] [US2] Add region cases to `tests/unit/WaitForStill/WaitForStillHandlerTests.cs`: change outside region has no effect (SC-003), change inside region resets the wait, region cut to the screen, region fully outside gives the existing failed result and adds no outcome name (FR-010). Needs T017 and T023
- [X] T025 [US2] Add the region input to the `WaitForStill` step in `tests/integration/Sequences/WaitForStillSequenceExecutionIntegrationTests.cs` (animated area outside the region). Needs T018, T023, and the executor tasks T012, T013, T015

**Checkpoint**: Region rules work and pass SC-003.

---

## Phase 5: User Story 3 - Use the outcome in later steps (Priority: P2)

**Goal**: A later `commandOutcome` condition can branch on `still` or `timeout_elapsed`. The sequence editor can build the step.

**Independent Test**: Run a branching sequence on a still screen and on a moving screen. Each run takes the correct branch.

**Dependency**: The outcome tests need the executor tasks first. Do T011 to T016 and T027 before T029.

- [X] T026 [P] [US3] Write tests in `tests/unit/Sequences/StepOutcomeStatesTests.cs` for `still` and `timeout_elapsed` as expected states, and for `still` matching `success`. These tests fail until T027 is done
- [X] T027 [US3] Allow `still` and `timeout_elapsed` in `src/GameBot.Domain/Services/StepOutcomeStates.cs` (`IsExpectedStateAllowed`, `Matches`) (research R9). Needs T003
- [X] T028 [US3] Update the `commandOutcome` `expectedState` text in `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs` for the two new names, and extend `tests/contract/Sequences/PrimitiveActionTypesOpenApiTests.cs`. Needs T022
- [X] T029 [US3] Add an integration test for a branch on the outcome in `tests/integration/Sequences/WaitForStillSequenceExecutionIntegrationTests.cs`. Needs T011 to T016 (executor) and T027
- [X] T030 [P] [US3] Create `src/web-ui/src/components/sequences/WaitForStillFields.tsx` with inputs for region x, y, width, height, `stableMs`, `maxDiff`, `timeoutMs` (each a `ParameterizableField`) and the default values (research R11)
- [X] T031 [P] [US3] Write Vitest tests in `src/web-ui/src/components/sequences/__tests__/WaitForStillFields.test.tsx`: each field renders with its default, a change calls the update handler, a `{{name}}` reference is accepted. Needs T030 (import fails before it exists)
- [X] T032 [US3] Route the new type to the form in `src/web-ui/src/components/sequences/PrimitiveActionFields.tsx`. Needs T030
- [X] T033 [US3] Add the "Add wait for still step" button and the default step (defaults of FR-008) in `src/web-ui/src/pages/SequencesPage.tsx`. Needs T030 and T032
- [X] T034 [US3] Write a Vitest UI test in `src/web-ui/src/pages/__tests__/SequencesPageWaitForStill.test.tsx` that renders `SequencesPage`, clicks the "Add wait for still step" button, and checks that a step of type `WaitForStill` is added and that the `WaitForStillFields` inputs show the default values (stableMs 300, maxDiff 0.01, timeoutMs 5000) (FR-012). Needs T032 and T033
- [X] T035 [US3] Accept `still` and `timeout_elapsed` as expected states in `src/web-ui/src/lib/validation.ts` and update its tests

**Checkpoint**: All three stories work. An author can build, save, run, and branch on the step.

---

## Phase 6: Polish and Cross-Cutting

- [X] T036 [P] Update `docs/architecture.md`: action list, new paragraph "WaitForStill step (feature 174)", outcomes, poll note, and the "Last reviewed" date
- [X] T037 [P] Add an entry to `CHANGELOG.md` for the new step
- [X] T038 [P] Set the status in `specs/174-wait-for-still-step/spec.md` and update `specs/STATUS.md`
- [X] T039 Check `specs/174-wait-for-still-step/contracts/wait-for-still-step.md` against the final code: the validation messages, the run result table, the log entry text, the preview text, the OpenAPI text, and the skip-and-retry and cancel notes. Fix the contract file or the code where they differ. Needs T001 to T035
- [X] T040 Run `scripts/ci/measure-shards.ps1` (absolute path) after the new tests exist and fix the shard balance if the rule fails
- [X] T041 Run the full build and all test projects (`dotnet build`, `dotnet test`, `npm test` in `src/web-ui`) and fix all failures. Confirm SC-005: no existing `WaitForImage` or fixed wait test changed
- [X] T042 Run the steps in `specs/174-wait-for-still-step/quickstart.md` to check the result and to measure the end times for the timing note: step end time against `stableMs` after the last change (SC-001), against `timeoutMs` on a screen that never stops (SC-002), and against a fixed wait after a pan (SC-004)
- [ ] T043 Put a short timing note in the PR description (Constitution Principle IV). The note shows the measured end times for SC-001, SC-002, and SC-004 from T042. Needs T042. Do this when you open the PR
- [X] T044 Bump the version in `installer/versioning/version.override.json` before the push (run `git fetch origin master`, compare with `origin/master`, set `patch`, `updatedBy`, `updatedAtUtc`). This is the last task

---

## Dependencies and Order

- Phase 1 first. Phase 2 needs T001 to T003 and blocks all stories.
- US1 (Phase 3) needs Phase 2. US2 (Phase 4) needs the US1 executor tasks. US3 (Phase 5) needs the US1 executor tasks for T029; the web UI tasks T030 to T035 can start after Phase 2.
- Executor tasks: T011 (handler), T012 (registration), T013 (routing), T014 (runner), T015 (log entry), T016 (validation).
- Region tests and outcome tests need the executor tasks first: T023 to T025 (US2) and T029 (US3) need T011 to T016 done.
- T023 (region clip) edits the handler from T011, so do T023 after T011.
- T028 edits the same file as T022, so do T028 after T022.
- T034 needs the button (T033) and the form routing (T032).
- Phase 6 needs all stories. T039 needs all code and test tasks. T043 needs T042. T044 is last.
- Inside a story: executor code first, then the tests that run it.

## Test-compile dependencies

- T007 needs T004. T008 needs T005. T009 needs T006 and T001. T010 needs T003.
- T017 and T024 need `WaitForStillHandler` (T011). T024 also needs T023 to pass.
- T018, T025, and T029 need the executor tasks T011 to T016. T029 also needs T027.
- T020 needs T001, T002, and T014.
- T026 compiles at once, but passes only after T027.
- T031 needs T030. T034 needs T030, T032, and T033.
- Build the Domain project before the Service project, and the Service project before the test projects. A build error in a code task blocks all test projects.

## Parallel Examples

- Phase 2: T004, T005, T006 in parallel. Then T007, T008, T009, T010 in parallel.
- US1: After T011 to T016, run T017, T018, T020, T021 in parallel.
- US3: T026, T030 in parallel. T031 after T030.
- Polish: T036, T037, T038 in parallel.

## Implementation Strategy

- MVP: Phase 1, Phase 2, and US1. This gives a working `WaitForStill` step on the whole screen.
- Then add US2 (region), then US3 (outcome branch and editor).
- Finish with Polish: docs, contract check, shard check, timing note in the PR description, version bump as the last task.

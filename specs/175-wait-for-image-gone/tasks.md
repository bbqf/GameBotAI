---

description: "Task list for the feature: Wait Until an Image Is Gone"
---

# Tasks: Wait Until an Image Is Gone

**Input**: Design documents from `/specs/175-wait-for-image-gone/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/wait-for-image-until.md, quickstart.md

**Tests**: Tests are required by the plan, FR-009, and Constitution Principle II. In each phase, write each test first and make sure it fails before you write the code that makes it pass. The test for the log mapping (T013) and the test for the preview text (T014) come before their code (T019, T020).

**Organization**: Tasks are grouped by user story. Each story can be built and tested alone.

**Task rules** (from the spec "Task generation notes"):

- One task only holds the counter logic (T007). Other tasks use the counter and do not write it again.
- Each conditional task has a skip rule.
- The version bump task (T038) comes before the CHANGELOG task (T039).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependency on an open task)
- **[Story]**: The user story the task belongs to (US1, US2, US3)

## Phase 1: Setup

**Purpose**: Check the starting state.

- [x] T001 Run the build and the existing `WaitForImage` tests in `tests/unit` and `tests/contract` to record a green baseline (SC-005)
- [x] T002 Read `src/GameBot.Domain/Services/SequenceRunner.cs` (`WaitForImageAsync`, `PollImageConditionAsync`, `RecordWaitForImageStepAsync`), `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`, and `src/GameBot.Domain/Vision/ImageDetectionPassScope.cs`. Confirm that the evaluator writes `LastPass` on each pass, also a pass with no frame (research R2). Note the result for T009

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Shared model, constants, and the count rule.

- [x] T003 [P] Write failing unit tests for `WaitForImageUntil.TryParse` (present, absent, other case, invalid value, null) in `tests/unit/Commands/WaitForImageUntilTests.cs`
- [x] T004 [P] Write failing unit tests for `AbsentCaptureCounter` (Seen resets, Failed resets, Absent adds one, reach at the needed count, needed count 1, flicker) in `tests/unit/Commands/AbsentCaptureCounterTests.cs`
- [x] T005 [P] Create `WaitForImageUntil` (constants `Present`, `Absent`, `DefaultAbsentCaptures`; `TryParse(string?, out bool isAbsent)`) in `src/GameBot.Domain/Commands/Execution/WaitForImageUntil.cs`
- [x] T006 Add the properties `Until` (default `present`) and `AbsentCaptures` (default 2) to `src/GameBot.Domain/Commands/WaitForImageConfig.cs`, and carry both through the copy in `src/GameBot.Domain/Parameters/CommandStepResolver.cs`
- [x] T007 [P] Create `PassKind` (`Seen`, `Absent`, `Failed`) and `AbsentCaptureCounter` (`Observe(PassKind)`) in `src/GameBot.Domain/Commands/Execution/AbsentCaptureCounter.cs`. This is the only task for the counter logic
- [x] T008 Run T003 and T004 and make sure they pass
- [x] T009 Conditional: fix `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs` so that it writes `LastPass` on every pass, and add a test in `tests/unit/Commands/`. Skip rule: skip this task when T002 confirmed that `LastPass` is written on every pass, also a pass with no frame

**Checkpoint**: Model and count rule are ready.

---

## Phase 3: User Story 1 - Wait until a toast or banner has cleared (Priority: P1) MVP

**Goal**: With `until` `absent`, the step ends with `image_absent` when the image is gone, and with `timeout_elapsed` when the image stays.

**Independent Test**: Run a sequence with a `WaitForImage` step (`until` `absent`). Check `image_absent` soon after the image clears, `timeout_elapsed` when it stays, and `image_unavailable` with no capture source.

### Tests for User Story 1 (write first, make sure they fail)

- [x] T010 [P] [US1] Write failing runner tests in `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs`: image leaves, image already absent, image stays to timeout, no evaluator gives `image_unavailable`, evaluator throws, no image id gives the present-mode result, cancellation records no outcome, `stepOutcomes` holds `success`
- [x] T011 [P] [US1] Write a failing runner test with a fake clock in `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs` (SC-004): the image leaves the screen after 1 s, the absent-mode step ends with `image_absent` before 5.3 s of fake time, and the step ends earlier than a fixed wait of 5.3 s
- [x] T012 [P] [US1] Write a failing integration test for a full run through the API with a fake screen source in `tests/integration/Sequences/WaitForImageAbsentSequenceIntegrationTests.cs`
- [x] T013 [P] [US1] Write a failing test that `image_absent` maps to the status `success` in `tests/unit/ExecutionLogs/ExecutionLogServiceMapStepStatusTests.cs` (FR-009)
- [x] T014 [P] [US1] Write a failing test that the preview text names the absent mode in `tests/unit/StepThrough/WaitForImageAbsentPreviewTests.cs` (FR-009)
- [x] T015 [P] [US1] Add a failing preview text case for the absent mode in `src/web-ui/src/pages/__tests__/SequencesPage.stepThrough.spec.tsx`

### Implementation for User Story 1

- [x] T016 [US1] Add the absent poll loop in `src/GameBot.Domain/Services/SequenceRunner.cs`. Use `WaitForImageSchedule.NextWait`, evaluate in `Present` mode, read `ImageDetectionPassScope.Current?.LastPass` to give `PassKind`, and feed `AbsentCaptureCounter` (T007). Select the loop when `Until` is `absent`. Leave the present path unchanged
- [x] T017 [US1] Record the outcome `image_absent` in `RecordWaitForImageStepAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs` with `success` in `stepOutcomes` (research R6)
- [x] T018 [US1] Add `Until` and `AbsentCaptures` to `WaitForImagePayloadContract` in `src/GameBot.Service/Models/SequenceStepContracts.cs`, and read them in `MapWaitForImageConfig` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`
- [x] T019 [P] [US1] Map `image_absent` to the status `success` in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` and set the exit condition text in `src/GameBot.Service/Models/ExecutionLogs.cs` and `src/GameBot.Domain/Logging/ExecutionLogModels.cs`. This makes T013 pass. Skip rule: skip the edit of a file that already holds the text
- [x] T020 [P] [US1] Add the preview text that names the absent mode in `src/GameBot.Service/Services/StepThrough/StepThroughActionPreview.cs` and in the matching preview text of `src/web-ui/src/pages/SequencesPage.tsx`. This makes T014 and T015 pass
- [x] T021 [US1] Run T010, T011, T012, T013, T014, and T015 and make sure they pass

**Checkpoint**: User Story 1 works alone (MVP).

---

## Phase 4: User Story 2 - Ignore a short flicker (Priority: P2)

**Goal**: `absentCaptures` sets the number of consecutive absent captures. The default is 2. A failed capture does not count.

**Independent Test**: Feed seen, absent, seen, then absent captures. Check the step ends only after the needed consecutive count.

### Tests for User Story 2 (write first, make sure they fail)

- [x] T022 [P] [US2] Add failing runner tests to `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs`: flicker with `absentCaptures` 2, default 2, `absentCaptures` 1, failed capture resets the count, short `timeoutMs` gives `timeout_elapsed` (SC-001, SC-003)
- [x] T023 [P] [US2] Write a failing web UI test file `src/web-ui/src/pages/__tests__/SequencesPageWaitForImageUntil.test.tsx`: the select "Wait until", the number field "Absent captures" shown only for `absent`, the save body, and the load of a saved step

### Implementation for User Story 2

- [x] T024 [US2] Make T022 pass. Check that the loop from T016 uses `AbsentCaptures` (default 2 when not set). Do not write the counter logic again; use T007. Skip rule: skip this task when T022 passes without a change
- [x] T025 [P] [US2] Add the types `until` and `absentCaptures` in `src/web-ui/src/services/sequences.ts`, and the outcome label `image_absent` in `src/web-ui/src/services/executionLogsApi.ts` and `src/web-ui/src/pages/executionLogGrid.ts`
- [x] T026 [US2] Add the form state, the defaults, the load code, the save code, and the two fields "Wait until" and "Absent captures" in `src/web-ui/src/pages/SequencesPage.tsx`. Save omits both fields when `until` is `present`. The level is desktop-only (FR-011); follow docs/mobile-ui-principles.md. This makes T023 pass
- [x] T027 [P] [US2] Conditional: change `src/web-ui/src/components/sequences/PrimitiveActionFields.tsx` for the two fields. Skip rule: skip this task when the `WaitForImage` form is inline in `SequencesPage.tsx` and not routed to this file

**Checkpoint**: User Stories 1 and 2 work.

---

## Phase 5: User Story 3 - Region support, old steps unchanged, validation (Priority: P2)

**Goal**: `detectionTarget` and `region` work in absent mode. Steps without `until` do not change. Invalid values give a clear error.

**Independent Test**: Run a step without `until` and check that it is unchanged. Run an absent step with a `region` and check that only the region counts. Save an invalid `until` and an invalid `absentCaptures` and check for the error.

### Tests for User Story 3 (write first, make sure they fail)

- [x] T028 [P] [US3] Write failing validation tests (invalid `until`, `absentCaptures` 0, negative, not a whole number, `{{name}}` reference skipped, `absentCaptures` with `present` accepted, case-insensitive `until`) in `tests/unit/Sequences/WaitForImageUntilValidationTests.cs`
- [x] T029 [P] [US3] Add a runner test for the `region` case (image outside the region counts as absent) and a test for a step without `until` that is the same as before, in `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs`
- [x] T030 [P] [US3] Add contract test cases for the payload fields `until` and `absentCaptures` and the `image_absent` text in `tests/contract/Sequences/PrimitiveActionTypesOpenApiTests.cs`

### Implementation for User Story 3

- [x] T031 [US3] Add the checks for `until` and `absentCaptures` to `ValidateWaitForImagePayload` in `src/GameBot.Domain/Services/SequenceStepValidationService.cs`. Each error has a clear message with a remedy. Check that the load check in `FileSequenceRepository` uses the same rule. Skip rule: skip the repository edit when it already calls this validator
- [x] T032 [US3] Describe `until`, `absentCaptures`, `image_absent`, and the arrival-check note in `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs` (`ActionTypes.WaitForImage`)
- [x] T033 [US3] Conditional: change `src/GameBot.Service/Swagger/SwaggerConfig.cs` if T030 shows that the schema filter alone is not enough. Skip rule: skip this task when T030 passes after T032
- [x] T034 [US3] Run T028, T029, and T030 and fix any failure. Run all existing `WaitForImage` tests and check that they pass without change (SC-005)

**Checkpoint**: All user stories work.

---

## Phase 6: Polish and Cross-Cutting Concerns

- [x] T035 [P] Update the WaitForImage sections and the poll schedule note in `docs/architecture.md`, with `until`, `absentCaptures`, `image_absent`, and the arrival-check note (FR-012). Update "Last reviewed"
- [x] T036 [P] Update `specs/STATUS.md` and the status line in `specs/175-wait-for-image-gone/spec.md`. Skip rule: skip `specs/STATUS.md` when the file does not exist
- [x] T037 Check that `specs/175-wait-for-image-gone/contracts/wait-for-image-until.md` matches the final code
- [x] T038 Bump `patch` in `installer/versioning/version.override.json` as CLAUDE.md states (run `git fetch origin master`, compare with `origin/master`; set `updatedBy` and `updatedAtUtc`). Skip rule: skip when the file already has the right value
- [x] T039 Add an entry to `CHANGELOG.md` that uses the version from T038
- [x] T040 Run the quickstart steps in `specs/175-wait-for-image-gone/quickstart.md`, and write a timing note for SC-001, SC-002, and SC-004 for the PR description
- [x] T041 Conditional: measure the test shards with `scripts/ci/measure-shards.ps1` and balance them. Skip rule: skip this task when the shard balance rule does not need a change after the new test files
- [x] T042 Run the full build, all test suites, and the Vitest suite. Check that all pass

---

## Dependencies and Order

- Phase 1 first. Phase 2 blocks all user stories.
- US1 (Phase 3) has no dependency on US2 or US3. It is the MVP.
- US2 (Phase 4): T024 needs the loop from T016. The web UI tasks T025, T026, and T027 can start after Phase 2.
- US3 (Phase 5): the validation tasks T028 and T031 can start after Phase 2. T029 needs T016.
- Phase 6 is last. T038 comes before T039.
- In each phase, the tests come before the code.

## Parallel Examples

- Phase 2: T003, T004, T005, and T007 run together (different files).
- US1: T010 to T015 run together. T019 and T020 run together.
- US2: T022 and T023 run together. T025 runs with T024.
- US3: T028, T029, and T030 run together.
- Polish: T035 and T036 run together.

## Implementation Strategy

1. Do Phases 1 and 2.
2. Do US1 and stop to check it (MVP).
3. Add US2 (flicker guard and editor), then US3 (validation, region, API text).
4. Do the polish phase and the version bump before the push.

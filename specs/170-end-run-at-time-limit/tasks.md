# Tasks: End a run at its time limit without a failure status

**Input**: Design documents from `/specs/170-end-run-at-time-limit/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-changes.md, quickstart.md

**Tests**: Tests are required (plan.md, Constitution Check II). Write each test before its code and see it fail. All new tests live in new files. Do not add them to the existing `SequenceTimeLimitLogIntegrationTests.cs` or to other existing test files.

**Organization**: Tasks are grouped by user story. A [P] task changes a file that no other task in the same phase changes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- Tasks that change the same file have no [P] and run in order.

## Path Conventions

- Domain: `src/GameBot.Domain/Commands/`
- Service: `src/GameBot.Service/`
- Web UI: `src/web-ui/src/`
- Tests: `tests/unit/`, `tests/integration/`, `tests/contract/`

---

## Phase 1: Setup

- [X] T001 Read `src/GameBot.Service/Services/SequenceExecution/SequenceTimeLimitScope.cs`, `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, and `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (`ResolveWatchdogTimeoutAsync`, `RunOneSequenceAsync`, `FinalizeAbandonedAsync`). Read the feature 122 code for `excludeFromSuccessNotifications` as the pattern. Make no change.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The stored option, its allowed values, the shared error message, and validation on save.

- [X] T002 Add the constants `OnTimeLimitFail = "fail"`, `OnTimeLimitEnd = "end"`, the list of allowed values, the helper `IsValidOnTimeLimit(string?)`, and the shared error-message constant `OnTimeLimitInvalidMessage` (text: `onTimeLimit must be one of: fail, end.`) to `src/GameBot.Domain/Commands/SequenceTimeLimits.cs`. The API and the repository MUST both use this one constant (FR-006). Do not change `DefaultWatchdogTimeoutMs` (240000) or any limit rule (FR-007).
- [X] T003 Add the optional string member `OnTimeLimit` (JSON name `onTimeLimit`) to `src/GameBot.Domain/Commands/CommandSequence.cs`. The file omits the member when the value is `fail` or absent (same pattern as feature 122).
- [X] T004 Reject an unknown `onTimeLimit` value on save in `src/GameBot.Domain/Commands/FileSequenceRepository.cs`. Use the constant `OnTimeLimitInvalidMessage` from T002. Do not write the text again in this file.
- [X] T005 [P] Add unit tests for the helper in `tests/unit/Sequences/SequenceTimeLimitsOnTimeLimitTests.cs` (new file): allowed values, unknown value, case-sensitive check, the text of `OnTimeLimitInvalidMessage` names the field and lists `fail, end`, default limit still 240000.
- [X] T006 [P] Add repository tests in `tests/unit/Sequences/FileSequenceRepositoryOnTimeLimitTests.cs` (new file): `end` round trip, `fail` and absent omit the member, unknown value is rejected with the message `OnTimeLimitInvalidMessage`, an old file without the member loads as `fail` (FR-010).

**Checkpoint**: The option is stored and validated.

---

## Phase 3: User Story 1 - A planned time-limit stop is not a failure (Priority: P1) MVP

**Goal**: A run that reaches its time limit on a sequence with `onTimeLimit` = `end` gets the status `success`, keeps the reason `sequence_time_limit` and the limit value, and the queue counts the firing as successful.

**Independent Test**: Run a sequence with `end` and a short limit over a long step. The log shows `success` and `sequence_time_limit`.

### Tests for User Story 1

- [X] T007 [P] [US1] Add scope tests in `tests/unit/Sequences/SequenceTimeLimitScopeEndsRunAtLimitTests.cs` (new file) for the new member `EndsRunAtLimit` (true for `end`, false otherwise, kept with `HasElapsed`; a stop request keeps `HasElapsed` false).
- [X] T008 [P] [US1] Add integration tests in `tests/integration/Sequences/SequenceTimeLimitEndLogIntegrationTests.cs` (new file): limit reached with `end` gives status `success`, reason `sequence_time_limit`, and the limit value; finish before the limit gives `success` and no reason; a step failure before the limit gives `failure`.
- [X] T009 [P] [US1] Add the default-limit test in the new file `tests/integration/Sequences/SequenceTimeLimitEndDefaultLimitTests.cs`. A sequence with `end` and no `watchdogTimeoutMs` ends at the default limit 240000 ms (use the test clock or a test override of the timer) with status `success`, reason `sequence_time_limit`, and `timeLimitMs` 240000 (spec scenario 4).
- [X] T010 [P] [US1] Add the same-time fault test in `tests/integration/Sequences/SequenceTimeLimitEndFaultTests.cs` (new file): a step fault recorded at the same time as the limit keeps the status `failure` (spec scenario 5). Also test that the abandoned path keeps `failure` when the unwinding exception is not the cancel of the limit timer.
- [X] T011 [P] [US1] Add the in-flight step test (unit) in `tests/unit/Sequences/SequenceExecutionServiceInFlightStepCancelTests.cs` (new file): the limit is reached while a step is in progress. Show that the in-flight step is cancelled as today (the step token is cancelled and the step does not finish its work), and that only the final status changes (`success` for `end`, `failure` for `fail`).
- [X] T012 [P] [US1] Add queue tests in `tests/unit/Queues/QueueExecutionServiceEndAtLimitTests.cs` (new file): a firing that ends at the limit with `end` is recorded as `Success`, sends no failure notification, and makes no daily retry (FR-004a); a firing with `fail` or no option keeps all current behaviour.
- [X] T013 [P] [US1] Add the parent-handling test in `tests/integration/Queues/ParentHandlingUnchangedAtTimeLimitTests.cs` (new file). Use the same case twice: a parent sequence that starts a child run, once with the child on `fail` and once with the child on `end`. Compare the parent run status and the queue record of the two cases. They MUST be equal to today's result for `fail`. Only the child run status differs for `end` (spec scenario 6, FR-004a note).

### Implementation for User Story 1

- [X] T014 [US1] Add the member `EndsRunAtLimit` to `src/GameBot.Service/Services/SequenceExecution/SequenceTimeLimitScope.cs` and take it in `Push`.
- [X] T015 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, extend `ResolveWatchdogTimeoutAsync` to return the limit (default 240000 when the sequence has none) and the option together. A lookup failure gives `fail`. Pass the option to `SequenceTimeLimitScope.Push`. Run after T014.
- [X] T016 [US1] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, set the final status in `FinalizeAbandonedAsync` and in the normal finalize site (research R-003): `success` plus reason `sequence_time_limit` plus limit value only when the scope has elapsed, `EndsRunAtLimit` is true, and no step failed. A step fault keeps `failure`. A stop request still wins. The in-flight step is still cancelled as today. The summary says that the run ended at its time limit by choice. Run after T014.
- [X] T017 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (`RunOneSequenceAsync`), record an `end` stop as `SequenceRunStatus.Success`, return true, and set `watchdogEnded` false, so there is no failure notification and no daily retry (FR-004a). Do not change any parent-sequence logic. Run after T015.

**Checkpoint**: A planned stop ends as `success` and the queue counts it as successful.

---

## Phase 4: User Story 2 - A reader can tell a time-limit stop from a real fault (Priority: P1)

**Goal**: Structured fields (status and reason) separate the two cases, with no need to read the message.

**Independent Test**: One `end` stop and one real fault differ in status and reason only.

- [X] T018 [P] [US2] Add integration tests in `tests/integration/Sequences/SequenceTimeLimitReaderFilterTests.cs` (new file). Contract tests: filter the log by status `success` and reason `sequence_time_limit` to find the `end` stop; a real fault has status `failure` and no reason; the two differ with no message read (spec US2 scenario 1). Soft check, in a separate test method with a comment that it is not a contract: the summary text says that the stop was by choice (US2 scenario 2). The contract tests MUST NOT read the summary text.
- [X] T019 [US2] If T018 fails, change the log write in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` so that a real fault never carries the reason. Do not add a log status or a new field (FR-008). Run after T016.

**Checkpoint**: A reader separates the two cases with structured fields.

---

## Phase 5: User Story 3 - Existing sequences keep the current behaviour (Priority: P1)

**Goal**: A sequence without the option, or with `fail`, behaves as before.

**Independent Test**: Run an old sequence past its limit. The status is `failure` and the reason is `sequence_time_limit`.

- [X] T020 [P] [US3] Add regression tests in `tests/integration/Sequences/SequenceTimeLimitFailRegressionTests.cs` (new file): a sequence saved before this feature (no member) and a sequence with `fail` both give `failure` and `sequence_time_limit` at the limit (SC-002, SC-004). Do not edit `SequenceTimeLimitLogIntegrationTests.cs`.
- [X] T021 [P] [US3] Add a test in `tests/unit/Queues/QueueExecutionServiceFailAtLimitTests.cs` (new file) that the queue behaviour for `fail` and for no option is unchanged (failure record, notification, retry).

**Checkpoint**: Existing runs are unchanged.

---

## Phase 6: User Story 4 - An author sets the option and gets clear errors (Priority: P2)

**Goal**: The author sets and reads `onTimeLimit` in the API and in the web UI. An unknown value gives a clear error.

**Independent Test**: Save with `end`, read it back. Save with an unknown value and see the error.

### Tests for User Story 4

- [X] T022 [P] [US4] Add API tests in `tests/integration/Sequences/SequenceOnTimeLimitApiIntegrationTests.cs` (new file): POST, PUT, and PATCH with `end` return the value; absent member on POST gives `fail`; PUT without the member resets to `fail`; PATCH without the member keeps the value; unknown value, non-string value, and JSON null give 400 with `onTimeLimit must be one of: fail, end.`; the check also runs on `dryRun`; every response shape holds `onTimeLimit`.
- [X] T023 [P] [US4] Add the shared-message test in `tests/integration/Sequences/OnTimeLimitSharedMessageTests.cs` (new file): send an unknown value through the API and save it through `FileSequenceRepository`. Show that both error messages equal `SequenceTimeLimits.OnTimeLimitInvalidMessage` and equal each other (FR-006).
- [X] T024 [P] [US4] Add round-trip tests in `tests/integration/Sequences/SequenceOnTimeLimitExportImportTests.cs` (new file): the option stays after a gamepack export and import (FR-001, edge case on export and import).
- [X] T025 [P] [US4] Add contract tests in `tests/contract/Sequences/SequenceOnTimeLimitOpenApiTests.cs` (new file): request and response schemas describe `onTimeLimit`, the allowed values, and the default; the description of `cancellationReason` states both cases.
- [X] T026 [P] [US4] Add UI tests in `src/web-ui/src/pages/__tests__/SequencesPage.onTimeLimit.spec.tsx` (new file): the select shows in the create and the edit form; the create body sends `onTimeLimit` only for `end`; the edit body always sends the value; the select loads the saved value.
- [X] T027 [P] [US4] Add the mobile-rule UI test in `src/web-ui/src/pages/__tests__/SequencesPage.onTimeLimitLayout.spec.tsx` (new file). The select follows `docs/mobile-ui-principles.md` (FR-012): the control has a hit area of at least 44 x 44 CSS px (min height and min width of 44 px in the applied style or class), and an input font of at least 16 px. Also check one column with no horizontal scroll, as far as the test environment can check it.

### Implementation for User Story 4

- [X] T028 [US4] Add the member `OnTimeLimit` to the request and response models in `src/GameBot.Service/Models/SequenceStepContracts.cs`. Always write `fail` or `end` in a response.
- [X] T029 [US4] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, check `onTimeLimit` first in POST, PUT, and PATCH (and in `dryRun`) and return the 400 error from `contracts/api-changes.md`, with the constant `SequenceTimeLimits.OnTimeLimitInvalidMessage` as the message. POST and PUT without the member give `fail`. PATCH without the member keeps the saved value. Map the member in all response shapes. Run after T028.
- [X] T030 [P] [US4] Update `src/GameBot.Service/Swagger/SequenceTimeLimitSchemaFilter.cs` and `src/GameBot.Service/Swagger/SwaggerConfig.cs` with the field text and the new `cancellationReason` text (FR-011). Do not change `watchdogTimeoutMs` text.
- [X] T031 [P] [US4] Add `onTimeLimit?: 'fail' | 'end'` to the types in `src/web-ui/src/services/sequences.ts`.
- [X] T032 [US4] Add the select "When the time limit is reached" (options "Fail the run" and "End the run as success", with a hint) to the create form and the edit form in `src/web-ui/src/pages/SequencesPage.tsx`. Phone support level: full. Follow `docs/mobile-ui-principles.md`: one column, a hit area of at least 44 x 44 CSS px, an input font of at least 16 px. Run after T031.

**Checkpoint**: An author sets, reads, and gets errors for the option in the API and the UI.

---

## Phase 7: Polish and cross-cutting

- [X] T033 [P] Update `docs/architecture.md` (execution log, queue watchdog, sequence fields) to describe `onTimeLimit` and the log result table from `data-model.md` (FR-011).
- [X] T034 [P] Update the sequence authoring guide in `docs/` to describe `onTimeLimit` and the log result (FR-011). Find the file with a search for the text `watchdogTimeoutMs` in `docs/`.
- [X] T035 [P] Add an entry for feature 170 (issue #354) in `CHANGELOG.md` under Unreleased, in STE.
- [X] T036 Run the manual check in `specs/170-end-run-at-time-limit/quickstart.md`. Record the result in the same file.
- [X] T037 Run `dotnet build` and `dotnet test` for the solution, and `npm test` and `npm run build` in `src/web-ui`. Fix each failure.
- [X] T038 Set the Status line in `specs/170-end-run-at-time-limit/spec.md` to `Implemented`, and add the row `| 170 | End a Run at Its Time Limit Without a Failure Status | Implemented |` after the row for 169 in `specs/STATUS.md`.
- [x] T039 Version bump, before the push of the branch for the PR: run `git fetch origin master` and compare `installer/versioning/version.override.json` with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`. Run last, after T037 and T038.

---

## Dependencies and Execution Order

- Phase 1, then Phase 2 (blocks all stories). Inside Phase 2: T002 -> T003 -> T004; T005 and T006 run after T002 to T004.
- US1 (Phase 3) needs Phase 2. Inside US1: T014 -> T015 and T014 -> T016; T015 -> T017 (T015 and T017 change the same file, so they run in order). T016 changes a different file from T015 and T017.
- US2 (Phase 4) needs T016. US3 (Phase 5) needs T016 and T017 and has no change in source files.
- US4 (Phase 6) needs Phase 2. It can run in parallel with US1 to US3, except for tests that need run behaviour. Inside US4: T028 -> T029; T031 -> T032. T023 needs T029.
- Phase 7 needs all stories. T039 is the last task.

## Parallel Example

```text
Phase 2 tests:  T005, T006
US1 tests:      T007, T008, T009, T010, T011, T012, T013
US4 tests:      T022, T023, T024, T025, T026, T027
US4 code:       T030, T031 (with T028 -> T029 on the other files)
```

## Implementation Strategy

- MVP: Phase 1, Phase 2, and US1 (T001 to T017). Then US2 and US3 as checks, then US4 to give the author a way to set the option.
- Deliver in order, and run the tests at each checkpoint.

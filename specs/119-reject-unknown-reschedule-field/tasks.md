# Tasks: Reject an unknown field in a reschedule-self payload

**Input**: Design documents from `/specs/119-reject-unknown-reschedule-field/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/reschedule-self-validation.md, quickstart.md

**Tests**: Tests are in scope (plan.md Test Plan, spec SC-004). Write each test first. The unknown-field tests must fail before the fix (T010). The guard tests (the "known fields" tests, the scope guard, and the `Bogus` option tests) pass before and after the fix.

**Organization**: Tasks are grouped by user story. The whole fix is one check in one method, so US1 holds the code change. US2 and US3 are regression guards.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: The user story that the task belongs to (US1, US2, US3)

## Phase 1: Setup

**Purpose**: Confirm a green baseline before any change.

- [X] T001 Build the solution. Run these existing tests: `tests\unit\Sequences\OcrOffsetValidationTests.cs`, `tests\unit\Sequences\SequenceStepValidationServiceActionTypeTests.cs`, `tests\contract\Sequences\SelfRescheduleActionContractTests.cs`. Record that they pass.

---

## Phase 2: Foundational

**Purpose**: No blocking work. The feature adds no new project, file in `src`, or dependency. Go to Phase 3.

**Checkpoint**: Baseline is green (T001).

---

## Phase 3: User Story 1 - Author sees an error for a field that does not exist (Priority: P1) MVP

**Goal**: A `reschedule-self` payload with an unknown top-level field gets a 400 that names each unknown field.

**Independent Test**: Post a create request (with and without `dryRun: true`) with the payload `{ "option": "Timer", "timerTimeOfDay": "11:00", "nextDay": true }`. The response is 400 and the error text contains `nextDay`.

### Tests for User Story 1 (write first, must fail before T010)

- [X] T002 [P] [US1] Create `tests\unit\Sequences\RescheduleSelfUnknownFieldValidationTests.cs`. Use the validator setup of `tests\unit\Sequences\OcrOffsetValidationTests.cs`.
- [X] T003 [US1] In the file from T002, add a test. A valid Timer payload with the field `nextDay` gives an error. The error contains `nextDay` and the known-field list.
- [X] T004 [US1] In the same file, add a test. Two unknown fields give one error. Assert that each name is in the message. Do not assert the order.
- [X] T005 [US1] In the same file, add a test. The keys `nextDay` and `NextDay` are both unknown. Assert that the error lists each key as the author wrote it.
- [X] T006 [US1] In `tests\contract\Sequences\SelfRescheduleActionContractTests.cs`, add two POST tests for a step with `nextDay`. With `dryRun: true`, the response is 400 and the body contains `nextDay`. Without `dryRun`, the response is 400 and `GET /api/sequences/{id}` returns 404 for the POST id. Use unique sequence ids. Leave no data in the shared bin data dir.
- [X] T007 [US1] In the same contract file, add a PUT test. Create a valid sequence first. Send PUT with a `reschedule-self` step that has `nextDay`. Assert 400. Then GET the sequence and assert that it is unchanged. Use a unique sequence id.
- [X] T008 [US1] In the same contract file, add a PATCH test. Create a valid sequence first. Send PATCH with a `reschedule-self` step that has `nextDay`. Assert 400. Then GET the sequence and assert that it is unchanged. Use a unique sequence id.

### Implementation for User Story 1

- [X] T009 [US1] Run the new tests from T003 to T008. Confirm that they fail because no error names the unknown field.
- [X] T010 [US1] In `src\GameBot.Domain\Services\SequenceStepValidationService.cs`, open `ValidateRescheduleSelfPayload`. Add the check after the `SelfReschedulePayload.TryRead` success branch.
- [X] T011 [US1] In the same method, build a static known-key set. Use the constants `OptionKey`, `TimerTimeOfDayKey`, `TimerRelativeOffsetKey`, and `OcrOffsetKey` of `SelfReschedulePayload`. Use `StringComparer.OrdinalIgnoreCase`.
- [X] T012 [US1] Collect the top-level keys of `action.Parameters` that are not in the set. Keep the payload order. If the list is not empty, add one error to `errors`.
- [X] T013 [US1] Write the error as `Step '<label>' reschedule-self payload has unknown field(s): <a, b>. Known fields: option, timerTimeOfDay, timerRelativeOffset, ocrOffset.`
- [X] T014 [US1] Do not change `SelfReschedulePayload.TryRead`, the load path, the nested `ocrOffset` check, or other action types.
- [X] T015 [US1] Run the tests from T003 to T008. Confirm that they pass.

**Checkpoint**: US1 works alone. This is the MVP.

---

## Phase 4: User Story 2 - A valid payload stays valid (Priority: P1)

**Goal**: Every payload with only known fields, valid today, stays valid. Field names match without regard to letter case.

**Independent Test**: Validate one step for each known option and field combination. Each stays valid.

### Tests for User Story 2

- [X] T016 [US2] In `tests\unit\Sequences\RescheduleSelfUnknownFieldValidationTests.cs`, add tests. Each payload stays valid: `Timer` with `timerTimeOfDay`; `Timer` with `timerRelativeOffset`; `Timer` with `ocrOffset`; `OncePerRun`; `AtQueueStart`; `EveryStep`.
- [X] T017 [US2] In the same file, add a test. The key `TimerTimeOfDay` (other letter case) stays valid.
- [X] T018 [US2] In the same file, add a scope-guard test. An unknown field inside `ocrOffset` is not rejected.
- [X] T019 [US2] In the same file, add a test. `SelfReschedulePayload.TryRead` still succeeds with an unknown key, for example `nextDay`. This guards FR-009. It passes before and after T010.
- [X] T020 [US2] In `tests\contract\Sequences\SelfRescheduleActionContractTests.cs`, add a test. A payload with known fields only returns success. Use a unique sequence id. Remove the sequence at the end.
- [X] T021 [US2] Run the tests from T016 to T020. Confirm that they pass with the T010 change.
- [X] T022 [US2] Run all existing `reschedule-self` tests. Confirm that all stay green (SC-002).

**Checkpoint**: US1 and US2 both pass.

---

## Phase 5: User Story 3 - The wrong option check does not change (Priority: P2)

**Goal**: `option: "Bogus"` keeps the same 400 and message.

**Independent Test**: Post a step with `option: "Bogus"`. The message is "is not a known schedule option (expected one of AtQueueStart, OncePerRun, Timer, EveryStep)".

### Tests for User Story 3

- [X] T023 [US3] In `tests\unit\Sequences\RescheduleSelfUnknownFieldValidationTests.cs`, add a test. `option: "Bogus"` with only known keys gives the same message as before. It gives no unknown-field error.
- [X] T024 [US3] In the same file, add a test. `option: "Bogus"` with `nextDay` gives the option error. It gives no unknown-field error. The unknown field shows after the author fixes the option.
- [X] T025 [US3] In `tests\contract\Sequences\SelfRescheduleActionContractTests.cs`, add a test. `option: "Bogus"` returns 400 with the unchanged message.
- [X] T026 [US3] In the same contract file, add a test. `option: "Bogus"` with `nextDay` returns 400. The body has the option error. The body does not name an unknown field.
- [X] T027 [US3] Run the tests from T023 to T026. Confirm that they pass.

**Checkpoint**: All three stories pass.

---

## Phase 6: Polish and Cross-Cutting Concerns

- [X] T028 [P] Search `docs\architecture.md` for text about `reschedule-self` validation. If the text is affected, update it in STE and update the "Last reviewed" date. If not, record "no change" and do not edit the file.
- [X] T029 [P] Add an entry to `CHANGELOG.md` in section `[Unreleased]`, subsection `Fixed`. The repo has this file. Follow the style of the existing entries. State the unknown-field rejection, the issue #228, and the compatibility note: a stored sequence with an unknown field still loads and runs, and the next PUT or PATCH of that sequence gets a 400 until the author removes the unknown field.
- [X] T030 [P] In `src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs`, open the `ActionTypes.RescheduleSelf` text. Add one sentence: "The service rejects an unknown top-level field." Check that an existing test on this text still passes. Update that test if it compares the full text.
- [X] T031 [P] Set the `Status` line of `specs\119-reject-unknown-reschedule-field\spec.md` and the row in `specs\STATUS.md` to the finished state.
- [X] T032 Run the full build and the unit and contract test projects. Confirm that they are green.
- [X] T033 Run the checks in `specs\119-reject-unknown-reschedule-field\quickstart.md`.
- [X] T034 Record the line and branch coverage of `ValidateRescheduleSelfPayload`. Compare them with the targets of constitution Principle II (80% line, 70% branch).

---

## Dependencies and Execution Order

- Phase 1 (T001) first. Phase 2 has no tasks.
- US1: T002 first. T003 to T005 edit the file from T002, so do them one at a time. T006 to T008 edit the contract file, so do them one at a time. The unit tests and the contract tests can run in parallel with each other. Then T009, then T010 to T014, then T015.
- US2 and US3 tests (T016 to T020, T023 to T026) can be written in parallel with the US1 tests, but they run after T010. The tests in one file must be edited one task at a time.
- Polish (T028 to T034) after all stories. T032 to T034 come last.

### Parallel Example

```text
T002 to T005 unit tests (new file)     T006 to T008 contract tests (SelfRescheduleActionContractTests.cs)
T020, T025, T026 edit the same contract file as T006 to T008: do them one after the other.
T028 docs   T029 changelog   T030 OpenAPI text   T031 status lines
```

## Implementation Strategy

- MVP: Phase 1 and US1 (T001 to T015). This delivers the fix.
- Then add US2 and US3 guards, then polish.
- Do not commit as part of task generation. Commit after implementation with `git commit -F <file>`.

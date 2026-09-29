# Tasks: One format for timerTimeOfDay

**Input**: Design documents in `C:\src\GameBot\specs\118-unify-timer-time-format\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Tests**: Required. Tests come first and MUST fail before the fix (Constitution II, FR-007).
**Rule**: All new text is in Simplified Technical English (Constitution VI). Use absolute paths in PowerShell. Use Edit/Write for UTF-8 files.

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

## Phase 1: Setup

- [X] T001 Confirm the baseline. Run `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and note that it passes. In `C:\src\GameBot\src\GameBot.Domain\Services\SequenceStepValidationService.cs`, confirm that it holds no own parse of `timerTimeOfDay` (research R-002). Record the result. If it holds a second parse, do task T001a.
- [X] T001a Conditional task. Do this task only if T001 finds a second parse in `SequenceStepValidationService.cs`. Route that parse through `TimerTimeOfDayFormat.TryParse`. Finish T001a before T010. If T001 finds no second parse, mark T001a as not needed.
- [X] T002 [P] Search `C:\src\GameBot\tests` for two kinds of test. First, a test that uses a not-strict `timerTimeOfDay` value (for example `PM`, single-digit hour). Second, a test that asserts an old error text: the template message with `HH:mm`, or the sequence message with `HH:mm:ss`. List each test that you find for change in T017. Do T002 before T010 (research R-006).

---

## Phase 2: Foundational

**Purpose**: The shared parser API. Stories 1 to 3 depend on it.

- [X] T003 Add the class shell `TimerTimeOfDayFormat` in `C:\src\GameBot\src\GameBot.Domain\Services\TimerTimeOfDayFormat.cs` with the public members `AcceptedFormatText` (const `HH:mm or HH:mm:ss (24-hour)`), `TryParse(string?, out TimeOnly)`, and `Format(TimeOnly)`. Give `TryParse` and `Format` a temporary body that throws `NotImplementedException`, so that the tests compile and fail. Add STE doc comments.

**Checkpoint**: Solution builds. Tests can call the parser.

---

## Phase 3: User Story 1 - Same accept/reject set in both validators (Priority: P1) MVP

**Goal**: The template endpoint and the sequence validator accept and reject the same strings.

**Independent Test**: Send one string list to both endpoints. The results are equal (same-result test).

### Tests first (MUST fail before T010)

- [X] T004 [P] [US1] Add `C:\src\GameBot\tests\unit\Sequences\TimerTimeOfDayFormatTests.cs`. Add a `[Theory]` accept list (`00:00`, `09:05`, `15:30`, `23:59`, `00:00:00`, `15:30:45`, `23:59:59`). Add a reject list (`null`, empty, space only, `24:00`, `24:00:00`, `23:60`, `12:00:60`, `9:30`, `9:30:00`, `11:00 PM`, ` 15:30`, `15:30 `, `15:30:`, `15.30`, `1530`, `15:30:45.123`, `abc`). Add `Format` cases: zero seconds gives `HH:mm`, non-zero seconds give `HH:mm:ss`, and a value with sub-second ticks gives the text without the ticks. Add a round-trip test.
- [X] T005 [P] [US1] In `C:\src\GameBot\tests\unit\Sequences\SelfReschedulePayloadTests.cs`, add cases: `HH:mm` and `HH:mm:ss` parse; each reject value fails (`11:00 PM`, `9:30`, ` 11:00`, `24:00`).
- [X] T006 [P] [US1] In `C:\src\GameBot\tests\integration\QueueTemplates\QueueTemplatesScheduleTypeTests.cs`, add tests with the current helpers: save `15:30:45` returns 201 and the response has `15:30:45`; save `15:30:00` returns `15:30`; save `15:30` returns `15:30`; save `5pm`, `24:00`, ` 15:30`, `9:30` returns 400. A GET after a save returns the same text as the save response. The seconds tests cover FR-009. Add one regression assertion: an empty or missing `timerTimeOfDay` keeps the "exactly one of timerTimeOfDay or timerRelativeOffset" error, and its text does not change.
- [X] T007 [P] [US1] Add `C:\src\GameBot\tests\integration\Sequences\TimerTimeOfDayParityTests.cs`. Use a list of at least 12 strings (valid and invalid, both formats). For each string, POST a template with a Timer entry, and POST a sequence with `dryRun: true` and a `reschedule-self` Timer step. Assert that the accept/reject results are equal, and that the accepted set equals the expected set (SC-001, FR-001).
- [X] T008 [US1] Run the new tests with `dotnet test` on the unit and integration projects. Confirm that the new tests FAIL (the parser throws or the behavior differs). Record the failing test names.

### Implementation

- [X] T009 [US1] Implement `TimerTimeOfDayFormat` in `C:\src\GameBot\src\GameBot.Domain\Services\TimerTimeOfDayFormat.cs`. `TryParse`: return false for null. Call `TimeOnly.TryParseExact(value, new[] { "HH:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)`. `Format`: `HH:mm` when `Second == 0`, else `HH:mm:ss`, with `InvariantCulture`.
- [X] T010 [US1] In `C:\src\GameBot\src\GameBot.Domain\Commands\SelfReschedule\SelfReschedulePayload.cs` (line about 74), replace `TimeOnly.TryParse` with `TimerTimeOfDayFormat.TryParse`.
- [X] T011 [US1] In `C:\src\GameBot\src\GameBot.Service\Endpoints\QueueTemplatesEndpoints.cs`, use `TimerTimeOfDayFormat.TryParse` for validation (line about 61) and for the stored value (line about 86). Use `TimerTimeOfDayFormat.Format` for the response (line about 238). This task covers FR-009.
- [X] T017 [US1] Update the existing tests that T002 listed. Do this in two parts, because T010 and T014 break these tests. Part A runs right after T010 and T011, before T012: if T002 found a test that pins a not-strict `timerTimeOfDay` value, change that test to a strict value. Part B runs right after T014, in Phase 4: if T002 found a test that asserts an old error text, change it to the new text (`HH:mm or HH:mm:ss`). T017 depends on T002, T010, and T011 for part A. T017 depends on T014 for part B. State each change in the commit note.
- [X] T012 [US1] Run the tests of T004 to T007 with `dotnet test`. Confirm that they PASS. Also confirm that the existing `HH:mm` tests still PASS (SC-002). Do this run only after part A of T017 is done. The tests that T017 part A changed must PASS with their new strict values.

**Checkpoint**: The bug is fixed. MVP done.

---

## Phase 4: User Story 2 - Error message names the accepted format (Priority: P2)

**Goal**: Both error messages name `HH:mm or HH:mm:ss`.

**Independent Test**: Send an invalid value to each endpoint. Both messages contain the same format text.

- [X] T013 [P] [US2] Add message tests. In `SelfReschedulePayloadTests.cs`, assert that each reject error contains `HH:mm or HH:mm:ss`. In `QueueTemplatesScheduleTypeTests.cs`, assert that the 400 message contains `HH:mm or HH:mm:ss`. In `TimerTimeOfDayParityTests.cs`, assert that both messages contain the same phrase (SC-004). Confirm that they FAIL before T014.
- [X] T014 [US2] Change the error text to `timerTimeOfDay '{value}' is not a valid time of day; accepted format: HH:mm or HH:mm:ss (24-hour), for example '15:30' or '15:30:00'` in `SelfReschedulePayload.cs` (keep the step label prefix) and in `QueueTemplatesEndpoints.cs` (keep the `entries[{i}].` prefix). Use `TimerTimeOfDayFormat.AcceptedFormatText`. Confirm that T013 tests PASS. Then do part B of T017, and run the changed tests.

---

## Phase 5: User Story 3 - Same rule in the API description (Priority: P3)

**Goal**: The OpenAPI text and doc comments state the same rule.

**Independent Test**: Read the OpenAPI document. Both descriptions contain `HH:mm or HH:mm:ss`.

- [X] T015 [P] [US3] In `C:\src\GameBot\tests\contract\Sequences\PrimitiveActionTypesOpenApiTests.cs`, add an assertion that the `reschedule-self` payload description contains `HH:mm or HH:mm:ss`. Add the new file `C:\src\GameBot\tests\contract\QueueTemplates\TemplateTimerTimeOfDayOpenApiTests.cs`. In it, add a contract test that the template entry `timerTimeOfDay` OpenAPI text contains the same phrase. Confirm that they FAIL before T016. Use the template entry text from `contracts/timer-time-of-day-format.md`, section OpenAPI text.
- [X] T016 [US3] Change the `RescheduleSelf` text in `C:\src\GameBot\src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs` (line about 59) to `timerTimeOfDay (HH:mm or HH:mm:ss, 24-hour, service-local time of day, no other form)`. Change the doc comments in `C:\src\GameBot\src\GameBot.Service\Contracts\QueueTemplates\TemplateEntrySaveRequest.cs`, `C:\src\GameBot\src\GameBot.Service\Contracts\QueueTemplates\QueueTemplateDetailResponse.cs` (state the output rule), and `C:\src\GameBot\src\GameBot.Domain\QueueTemplates\QueueTemplateEntry.cs`. First step: find the source of the template entry description (research R-005). If none exists, add the note: a small schema-filter note. Use the template entry text from `contracts/timer-time-of-day-format.md`, section OpenAPI text. Confirm that T015 tests PASS.

---

## Phase 6: Polish and Cross-Cutting

- T017 moved to Phase 3 (after T011). Its second part runs after T014 in Phase 4.
- [X] T018 [P] Change the `timerTimeOfDay` sentence (line about 237) and the "Last reviewed" line in `C:\src\GameBot\docs\architecture.md`.
- [X] T019 [P] Add a "Fixed" entry (118, #226) in `C:\src\GameBot\CHANGELOG.md`.
- [X] T020 [P] Add row 118 to `C:\src\GameBot\specs\STATUS.md`.
- [X] T021 Run the full gate: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`; `dotnet test` for the unit, integration, and contract projects. This run covers FR-006, FR-008, and SC-002: current tests keep their results, and no other schedule behavior changes; `dotnet format whitespace "C:\src\GameBot\GameBot.sln" --verify-no-changes --include <changed and new C# files>`.
- [X] T022 Run the checks in `C:\src\GameBot\specs\118-unify-timer-time-format\quickstart.md`.
- [X] T023 Set the Status of `C:\src\GameBot\specs\118-unify-timer-time-format\spec.md` to "Implemented".

---

## Dependencies

- Phase 1 then Phase 2 then Phase 3 (MVP) then Phases 4 and 5 (they can run in parallel after T012) then Phase 6.
- Inside each story: tests, confirm failure, implementation, confirm pass.
- T009 depends on T003. T010 and T011 depend on T009. T001a (if needed) must finish before T010. T014 depends on T010 and T011. T016 is independent of T014.
- T017 depends on T002. Part A of T017 runs after T010 and T011 and before T012. Part B of T017 runs after T014. T012 waits for part A of T017.

## Parallel Examples

- US1 tests: T004, T005, T006, T007 (different files).
- Polish: T018, T019, T020.

## Implementation Strategy

MVP is User Story 1 (T001 to T012): one shared strict parser in both validators. T017 part A is also in the MVP. Then add the messages (US2) and the OpenAPI text (US3). Finish with the docs and the gate.

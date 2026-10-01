---

description: "Task list for OCR Offset Day Part"
---

# Tasks: OCR Offset Day Part

**Input**: Design documents from `/specs/124-ocr-offset-day-part/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ocr-duration-text.md, quickstart.md

**Tests**: Tests are required (FR-015). Write the tests first. They MUST fail before the parser change.

**Organization**: Tasks are grouped by user story. All parser tests share one test file. All resolver tests share another test file. The parser change is in one source file. Tasks that edit the same file are NOT marked [P] and run in order. The parser change is in User Story 1 (day token before a time token) and User Story 4 (day token alone). User Stories 2 and 3 add tests that prove their rules.

**Test coverage rule**: Each check has one owner test. The resolver booking and limit result is owned by User Story 3 (T011). The "offset source ocr" log line text is owned by User Story 1 (T006). No test repeats the other test's assertion.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story label (US1 to US4)

## Phase 1: Setup

- [X] T001 Run `dotnet build` for the solution. Run the existing tests in `tests\unit\Sequences\CooldownDurationParserTests.cs` and `tests\unit\Sequences\OcrOffsetResolverTests.cs`. Record that they pass before any change.
- [X] T002 Read `src\GameBot.Domain\Commands\SelfReschedule\CooldownDurationParser.cs` and `src\GameBot.Service\Services\SequenceExecution\OcrOffsetResolver.cs`. Find the existing time regex, the `NormalizeDigits` step, the overflow handling, and the min and max check.

## Phase 2: Foundational

No blocking prerequisite. The parser signature `TryParse(string?, out TimeSpan)` does not change.

## Phase 3: User Story 1 - Book a long timer with a day part (Priority: P1)

**Goal**: The parser reads an optional "<N>d" directly before the first time token ("HH:MM:SS" or "MM:SS") and adds N days to the offset.

**Independent Test**: `TryParse("1d 23:29:10")` gives 1 day 23:29:10. `TryParse("Free in 1d 23:29:10")` gives the same value. The "offset source ocr" log line shows "1.23:29:10".

### Tests for User Story 1

- [X] T003 [US1] In `tests\unit\Sequences\CooldownDurationParserTests.cs`, add tests for "1d 23:29:10", "Free in 1d 23:29:10", "1d23:29:10" (no space), "1D 23:29:10" (upper case), "11d 23:29:10" (11 days), and "1d 05:30" (1 day 5 minutes 30 seconds). Expected values include the day part (FR-001, FR-002, FR-004, FR-007).
- [X] T004 [US1] In `tests\unit\Sequences\CooldownDurationParserTests.cs`, add tests for text where the day token does not count. Each gives 23:29:10, the same as before this change: "Reward 23:29:10", "3days 23:29:10", "23:29:10 1d", "1d left 23:29:10", "1w 23:29:10", and "1 d 23:29:10" (FR-005, FR-006, FR-014).
- [X] T005 [US1] In `tests\unit\Sequences\CooldownDurationParserTests.cs`, add two separate overflow tests. Case 1 (integer overflow): "999999999999d 00:00:01". Case 2 (range overflow): "2147483647d 00:00:01" (the day count fits in an integer, but the total is outside the `TimeSpan` range). For each, `TryParse` MUST return `false` and MUST NOT throw (FR-009).
- [X] T006 [US1] In `tests\unit\Sequences\OcrOffsetResolverTests.cs`, add a test for the "offset source ocr" log line: OCR text "1d 23:29:10" with `max` "2.00:00:00" writes a log line that shows the parsed value with the day part ("1.23:29:10"). Assert the log text only. Do not assert the booked offset here (T011 owns it) (FR-011, SC-006).
- [X] T007 [US1] In `tests\unit\Sequences\OcrOffsetResolverTests.cs`, add a test: OCR text "2147483647d 00:00:01" uses the fallback offset with reason "parse-failed", and the reason is not "ocr-error" (range overflow, FR-009).

### Implementation for User Story 1

- [X] T008 [US1] In `src\GameBot.Domain\Commands\SelfReschedule\CooldownDurationParser.cs`, add the day regex `(?<![A-Za-z0-9])(\d+)[dD](?![A-Za-z])` with the same culture-invariant option and 200 ms timeout. Match it on the text after `NormalizeDigits` (the digit fix runs first). Find the first time token with the existing regex. If a time token exists, match the day regex at the end of the text before it, with only white space after the day token (`\s*\z`). If it matches, add the days. Parse the day digits with `int.TryParse` and return `false` when it fails. Compute `days * 86400 + hours * 3600 + minutes * 60 + seconds` in a `checked` block. Catch `OverflowException` and `ArgumentOutOfRangeException` and return `false`. Results for text without a day part MUST stay the same (FR-008, FR-009).
- [X] T009 [US1] In `src\GameBot.Domain\Commands\SelfReschedule\CooldownDurationParser.cs`, update the XML comment of `CooldownDurationParser`. Describe the day part, the optional white space, the case-insensitive "d", the rule that the day token is directly before the time token, the digit fix that runs first, and the two overflow failures.
- [X] T010 [US1] Run the tests in `tests\unit\Sequences`. Make T003 to T007 pass.

**Checkpoint**: User Story 1 works alone.

## Phase 4: User Story 2 - Keep timers without a day part unchanged (Priority: P1)

**Goal**: Results for "HH:MM:SS", "MM:SS", and text with no duration stay the same.

**Independent Test**: All existing parser tests pass with no change to expected values.

- [X] T011 [US2] Check that no existing expected value in `tests\unit\Sequences\CooldownDurationParserTests.cs` changed. If a test for "23:29:10", "05:30", or text with no duration is missing, add it with the old result. Run the full parser test class (FR-008, SC-003).

**Checkpoint**: No regression.

## Phase 5: User Story 3 - Apply min and max limits to the total offset (Priority: P2)

**Goal**: The `min` and `max` limits apply to the total offset with days. No code change is expected. This phase owns the resolver result (the booked offset and the out-of-bounds fallback).

**Independent Test**: "1d 23:29:10" with `max` "2.00:00:00" stays 1 day 23:29:10.

- [X] T012 [US3] In `tests\unit\Sequences\OcrOffsetResolverTests.cs`, add a test: `max` "2.00:00:00" and text "Free in 1d 23:29:10" gives source Ocr and a booking of 1 day 23:29:10 ahead of now, not changed (US1 scenario 3, US3 scenario 1, SC-001, SC-005). Assert the result only. Do not assert the log text.
- [X] T013 [US3] In `tests\unit\Sequences\OcrOffsetResolverTests.cs`, add a test: `max` "1.00:00:00" and text "1d 23:29:10" uses the fallback with reason "out-of-bounds". The resolver does not clamp.
- [X] T014 [US3] In `tests\unit\Sequences\OcrOffsetResolverTests.cs`, add two tests: `min` "1.00:00:00" and text "23:29:10" uses the fallback with reason "out-of-bounds"; the default `max` (24:00:00) and text "1d 23:29:10" uses the fallback with reason "out-of-bounds".
- [X] T015 [US3] Run the resolver tests. If a test fails, fix the cause only in the parser. Do not change `OcrOffsetResolver.cs` unless the test proves a defect.

**Checkpoint**: Limits work on the total.

## Phase 6: User Story 4 - Read a day count alone (Priority: P3)

**Goal**: "2d" gives 2 days, only when the text has no time token.

**Independent Test**: `TryParse("2d")` gives 2 days.

- [X] T016 [US4] In `tests\unit\Sequences\CooldownDurationParserTests.cs`, add tests: "2d" and "Free in 2d" give 2 days. "Reward" and "3days" give a parse failure. A lone "Id" and a lone "ld" give 1 day, because the digit fix runs first and changes them to "1d" (the accepted OCR risk in research.md Decision 3) (FR-003, FR-005).
- [X] T017 [US4] In `src\GameBot.Domain\Commands\SelfReschedule\CooldownDurationParser.cs`, when the text has no time token, return the first day token (after the digit fix) as N days, with the same overflow handling as T008. Make T016 pass.

**Checkpoint**: All stories work.

## Phase 7: Polish and Cross-Cutting Concerns

- [X] T018 [P] In `src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs`, change the `RescheduleSelf` `ocrOffset` description. State that the OCR text can have a day part "<N>d" before the time, and that `max` (default 24:00:00) must allow a read of one day or more, for example "2.00:00:00". Use STE (FR-012).
- [X] T019 [P] Search `docs\architecture.md` for a statement of the OCR text format. If it states the format, add the day part and refresh its "Last reviewed" date. If not, make no change.
- [X] T020 Search `tests` for a test of the `PrimitiveActionSchemaFilter` `RescheduleSelf` text. If one exists, update it for the new text (the same file as T018, so run after T018). If none exists, add no test.
- [X] T021 Check that `SelfReschedulePayload.cs`, `SequenceStepValidationService.cs`, `OcrOffsetResolver.cs`, and the other offset sources need no change (FR-013, FR-014). Set the spec `Status` in `specs\124-ocr-offset-day-part\spec.md` at the end of the work.
- [X] T022 Run the steps in `specs\124-ocr-offset-day-part\quickstart.md`. Run `dotnet build` and `dotnet test` for the solution. All tests MUST pass.

## Dependencies and Execution Order

- Phase 1 first. Phase 2 has no task.
- Tests T003 to T007 come before T008. They MUST fail first. T003 to T005 and T016 share one test file. T006, T007, and T012 to T014 share another test file. These tasks run in order.
- T008, T009, and T017 edit one source file and run in order. T010 follows T009.
- US2 (T011) follows T010. US3 tests (T012 to T014) need T008. US4 (T016, T017) follows T010 because T017 changes the same parser file.
- Polish (T018 to T022) follows all stories. T018 and T019 edit different files from each other and from T016 and T017, so they can run in parallel with T016 and T017.

## Parallel Opportunities

- The only [P] tasks are T018 and T019 (different files, no shared dependency).
- The parser test file and the resolver test file are different files. A second person can write T006 and T007 while the first writes T003 to T005, but each file stays one task at a time.

## Implementation Strategy

- **MVP**: Phase 1 and User Story 1 (T001 to T010). This fixes the observed defect.
- Then the US2 check, US3 tests, US4 alone-day case, and last the documentation and full test run.

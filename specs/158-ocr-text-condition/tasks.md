---

description: "Task list for the ocrText sequence condition"
---

# Tasks: OCR Text Condition for Sequences

**Input**: Design documents from `/specs/158-ocr-text-condition/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ocr-text-condition.md, quickstart.md

**Tests**: The plan and SC-003 require tests. Test tasks are in each story phase and come before the code they test.

**Organization**: Tasks are grouped by user story. The condition is one new leaf, so US1 builds the base path. US2, US3, and US4 add to it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an open task). Tasks that edit the same file are never marked [P].
- **[Story]**: The user story of the task (US1 to US4)
- All paths are relative to `C:\src\GameBot`
- Failure reason codes (closed list, FR-013): `no-session`, `capture-failed`, `ocr-unavailable`, `region-outside-frame`, `region-invalid`, `ocr-error`, `parse-failed`
- A `pattern` time-out is a plain mismatch with no reason code. The run log says "pattern timed out" (FR-017).

## Phase 1: Setup

**Purpose**: Find the exact places to change.

- [ ] T001 Search the repository for `LastRunStepCondition` and list each switch or guard that needs an `ocrText` case. Check `src/GameBot.Domain/Commands/SequenceStepCondition.cs`, `src/GameBot.Domain/Services/CompositeConditionValidator.cs`, `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`, `src/GameBot.Domain/Services/SequenceRunner.cs`, `src/GameBot.Domain/Commands/FileSequenceRepository.cs`, `src/GameBot.Domain/Services/StepThrough/SequenceStepper.cs`, `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`. Do not change a file in this task.
- [ ] T002 [P] Find every run entry point in `src/GameBot.Service` (search for callers of `SequenceExecutionService.ExecuteAsync`, `ExecuteCoreAsync`, and `_runner.ExecuteAsync`, and `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.StepThrough.cs`). For each one, note if it has an emulator session (queue run, ad-hoc run, nested sequence call, dry run, no-capture run). Do not change a file in this task. T019 and T020 use this list (FR-020).
- [ ] T003 [P] Read `src/GameBot.Service/Services/Ocr/OcrReadService.cs`, `OcrRegionReader`, and `OcrTextParsers`. Note the region-inside-frame rule and the parser API (`TryGetName`, `Names`, `TryParse`) that the reader and the endpoint reuse.

---

## Phase 2: Foundational (blocking)

**Purpose**: Domain types, rules, scope, DTO, and save-time wiring that all stories use.

**CRITICAL**: No story work starts before this phase is complete.

- [ ] T004 [P] Create `OcrTextConditionRules` in `src/GameBot.Domain/Commands/OcrTextConditionRules.cs`: `Validate` (region required with width and height above zero, exactly one of `expected` and `pattern` not null, valid regex; each message names the field) and `Matches` (`expected`: ordinal compare of trimmed text, an empty text is compared like any text; `pattern`: `Regex.IsMatch` with `CultureInvariant` and a 1 second time limit; a time-out gives false and sets a flag so the log says "pattern timed out"). Add XML comments.
- [ ] T005 [P] Create `src/GameBot.Domain/Services/OcrTextEvaluationScope.cs` with `OcrTextReadResult`, `OcrTextEvaluationRecord` (fields in data-model.md, a method that formats the log line, `reason=<code>` when set, "pattern timed out" when the flag is set), the seven failure reason code constants, and `OcrTextEvaluationScope` (`AsyncLocal` `Current`, `Reader` delegate, `Records` list with a drain method, `Push` that returns an `IDisposable` which restores the earlier scope; same pattern as `SequenceRunContext`).
- [ ] T006 [P] Add `OcrTextStepCondition` (`Region` as `PixelRegion`, `Expected`, `Pattern`, `Parser`) with the discriminator `ocrText` to `src/GameBot.Domain/Commands/SequenceStepCondition.cs`.
- [ ] T007 [P] Add `OcrTextConditionContract` (discriminator `ocrText`, all fields optional in JSON, `region` as `PixelRegionDto`) to `src/GameBot.Service/Models/SequenceStepContracts.cs`.
- [ ] T008 Add the `ocrText` case to `CompositeConditionValidator.Walk` in `src/GameBot.Domain/Services/CompositeConditionValidator.cs`. It calls `OcrTextConditionRules.Validate` and uses the message format `Step '<id>' condition at <path>: <text>`. Depends on T004, T006.
- [ ] T009 [P] Add the `ocrText` guard case in `src/GameBot.Domain/Commands/FileSequenceRepository.cs`. Depends on T006.
- [ ] T010 Add the domain-to-DTO and contract-to-domain mapping for `ocrText` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`. A missing field gives a 400 `invalid_request` from validation. Depends on T004, T006, T007, T008.

**Checkpoint**: A sequence with an `ocrText` condition saves and loads. Invalid forms are rejected.

---

## Phase 3: User Story 1 - Branch on text read from the screen (Priority: P1) MVP

**Goal**: An `If` step with an `ocrText` condition takes the branch that matches the text in the region.

**Independent Test**: Run a saved sequence on a frame where the region shows the expected text, then on a frame with other text. The first run takes `then`. The second run takes `else`.

### Tests for User Story 1

- [ ] T011 [P] [US1] Create `tests/unit/Sequences/OcrTextConditionRulesTests.cs`: region width or height zero or negative; both or neither of `expected` and `pattern`; invalid regex; trim of white space; case-sensitive `expected`; `pattern` matches anywhere; pattern time-out gives false as a plain mismatch with the "pattern timed out" flag and no reason code (FR-017); empty text read with empty `expected` is true; empty text with a `pattern` that matches empty text is true; empty text with a non-empty `expected` is false (FR-019).
- [ ] T012 [P] [US1] Create `tests/unit/Sequences/OcrTextConditionEvaluatorTests.cs`: leaf true and false with a fake reader; `negate`; no scope gives false with reason `no-session`; the reader failure reasons `region-invalid` and `region-outside-frame` give false with that one code; a plain mismatch has no reason; a pattern time-out gives false, no reason code, and a record that says "pattern timed out"; an empty read is compared like any text; the fake input sink gets no call (FR-011).
- [ ] T013 [P] [US1] Create `tests/unit/Services/Ocr/OcrTextConditionReaderTests.cs`: reason `capture-failed` (no frame), `ocr-unavailable`, `region-outside-frame`, `region-invalid`, `ocr-error`; an empty read returns an empty text and no reason; the raw text equals the text of `OcrReadService` for the same frame and region (SC-005).
- [ ] T014 [P] [US1] Create `tests/contract/Sequences/OcrTextConditionContractTests.cs`: save-time 400 errors that name `region`, `expected` and `pattern`, `pattern`; and a valid save and read-back.
- [ ] T015 [P] [US1] Create `tests/integration/Sequences/OcrTextConditionRunTests.cs`: an `If` step takes `then` for text "0" and `else` for text "3"; a `pattern` that matches the end of "Interaction attempts left: 0"; an integration assertion that no tap, swipe, key, or other input reaches the fake emulator input sink during the run (FR-011).

### Implementation for User Story 1

- [ ] T016 [US1] Add the `ocrText` case to `EvaluateLeafAsync` and `Describe` in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`. Read through `OcrTextEvaluationScope.Current.Reader`, match with `OcrTextConditionRules.Matches`, append one `OcrTextEvaluationRecord`, and apply `negate` after the result. With no scope: false with reason `no-session`. A pattern time-out is false with no reason code. Depends on T004, T005, T006.
- [ ] T017 [P] [US1] Create `src/GameBot.Service/Services/Ocr/OcrTextConditionReader.cs`: `ISessionFrameSource.Capture`, then `OcrRegionReader.Read`. Return one reason code from the closed list for each failure (`capture-failed`, `ocr-unavailable`, `region-outside-frame`, `region-invalid`, `ocr-error`). Do not copy code from `OcrReadService`. Depends on T005.
- [ ] T018 [US1] Register `OcrTextConditionReader` in the service container in `src/GameBot.Service/GameBotServiceSetup.cs`. Depends on T017.
- [ ] T019 [US1] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, add a static `PushOcrScope` method (same pattern as `PushRunContext`). Call it in `ExecuteCoreAsync` so that every run entry point that has an emulator session pushes the scope with the reader (queue run, ad-hoc run, nested sequence call). A run entry point with no session (dry run, no capture) pushes no scope, so the condition is false with `no-session` (FR-020). Use the entry point list from T002. Depends on T002, T005, T017, T018.

**Checkpoint**: US1 works alone. T011 to T015 pass.

---

## Phase 4: User Story 2 - Use the condition in every condition place (Priority: P1)

**Goal**: `ocrText` works in `If`, `Loop`, `breakCondition`, `all`, `any`, `none`, and in step-through and gamepack files.

**Independent Test**: One sequence for each place takes the expected path.

### Tests for User Story 2

- [ ] T020 [P] [US2] Create `tests/unit/Sequences/OcrTextConditionCompositeTests.cs`: `ocrText` as a child of `all`, `any`, and `none`, mixed with `imageVisible`, `commandOutcome`, and `lastRun` children.
- [ ] T021 [P] [US2] Create `tests/integration/Sequences/OcrTextConditionPlacesRunTests.cs`: one run test each for `Loop`, `breakCondition`, `all`, `any`, and `none` (SC-003). Also one test for each run entry point from T002, with a session (the scope is pushed and the read works) and without a session (false with `no-session`, no crash) (FR-020).
- [ ] T022 [P] [US2] Create `tests/unit/StepThrough/SequenceStepperOcrTextTests.cs`: step-through does not read the screen, shows the note `ocrText is not evaluated`, and treats the leaf as true as for `lastRun`.
- [ ] T023 [P] [US2] Add a case to `tests/unit/Gamepacks/GamepackTypeDiscriminatorCheckTests.cs`: a gamepack sequence member with an `ocrText` condition passes the check and loads.
- [ ] T024 [P] [US2] Create `tests/unit/Gamepacks/OcrTextUnknownParserRuntimeTests.cs`: a sequence that enters by a path other than the sequence endpoint (for example gamepack import, with no endpoint check) has an `ocrText` condition with an unknown parser. At run time the condition is false with reason `parse-failed` (FR-018).

### Implementation for User Story 2

- [ ] T025 [US2] Add the `ocrText` case to `DescribeBreakCondition` in `src/GameBot.Domain/Services/SequenceRunner.cs`. Depends on T006.
- [ ] T026 [US2] Add the note `ocrText is not evaluated` for the `ocrText` leaf in `src/GameBot.Domain/Services/StepThrough/SequenceStepper.cs`, with the same pattern as `lastRun`. Depends on T022.
- [ ] T027 [US2] Check `src/GameBot.Domain/Gamepacks/GamepackTypeDiscriminatorCheck.cs`. It reads polymorphism metadata, so no change is expected. If it holds a type list, add `ocrText`. Depends on T006, T023.

**Checkpoint**: All six places pass. Step-through shows the note. The gamepack check accepts `ocrText`.

---

## Phase 5: User Story 3 - Read the text with a parser (Priority: P2)

**Goal**: With `parser` `hh:mm:ss`, the condition compares the parsed value. Unparsed text gives false.

**Independent Test**: A frame with "02:10:35" and parser `hh:mm:ss` is true. A frame with text that does not parse is false with reason `parse-failed`.

### Tests for User Story 3

- [ ] T028 [US3] Add tests to `tests/contract/Sequences/OcrTextConditionContractTests.cs`: parser `HH:MM:SS` is accepted and stored as `hh:mm:ss`; an unknown parser gives a 400 that names `parser` and the supported parsers. Depends on T014.
- [ ] T029 [P] [US3] Create `tests/unit/Sequences/OcrTextConditionParserTests.cs`: parsed value compared with `expected` and `pattern`; a parse failure gives false with reason `parse-failed` and the text read; an unknown parser name that reaches the evaluator through a non-endpoint path gives false with `parse-failed` (FR-018).
- [ ] T030 [US3] Add parser cases to `tests/unit/Services/Ocr/OcrTextConditionReaderTests.cs`: `Parsed` is set when the text parses; reason `parse-failed` when it does not; reason `parse-failed` when the parser name is unknown. Depends on T013.

### Implementation for User Story 3

- [ ] T031 [US3] Add parser use to `src/GameBot.Service/Services/Ocr/OcrTextConditionReader.cs`: call `OcrTextParsers.TryParse` when a parser is named, set `Parsed`, or reason `parse-failed` (also for an unknown name). Depends on T017, T030.
- [ ] T032 [US3] In `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`, compare `Parsed` (not the raw text) when a parser is named, and put the compared text in the record. Depends on T016, T029, T031.
- [ ] T033 [US3] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, check the `parser` name with `OcrTextParsers.TryGetName` (without case), store the canonical name, and reject an unknown name with a message that names `parser` and the supported parsers. This is the only save-time parser check (FR-018). Depends on T010, T028.

**Checkpoint**: Parser cases pass. Canonical name `hh:mm:ss` is stored for `HH:MM:SS`.

---

## Phase 6: User Story 4 - See the text that was read in the run log (Priority: P1)

**Goal**: Each evaluation puts the text read, the expected text or pattern, the result, and any reason code in the run log.

**Independent Test**: Run a sequence with an `ocrText` condition. The step log has the text that was read.

### Tests for User Story 4

- [ ] T034 [P] [US4] Create `tests/integration/Sequences/OcrTextConditionRunLogTests.cs`: a log line with the text read, expected or pattern, and result; a line for a child of `all`, `any`, and `none`; no record for a child skipped by short-circuit (SC-004); `reason=<code>` shows for a failure; "pattern timed out" shows for a time-out with no reason code.
- [ ] T035 [US4] Add a no-session case to `tests/integration/Sequences/OcrTextConditionRunTests.cs`: a run with no emulator session gives false, reason `no-session` in the run log, and the run does not crash. Depends on T015.

### Implementation for User Story 4

- [ ] T036 [US4] In `src/GameBot.Domain/Services/SequenceRunner.cs`, drain the scope records after each condition evaluation and append one line per record to the step detail. Depends on T005, T025, T034.

**Checkpoint**: The run log shows the text for every evaluated `ocrText` condition.

---

## Phase 7: Polish and Cross-Cutting

- [ ] T037 [P] Create `src/GameBot.Service/Swagger/OcrTextConditionSchemaFilter.cs` with field descriptions and the note that the web UI does not know this type.
- [ ] T038 Register the `ocrText` schema in `src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs`. Depends on T007, T037.
- [ ] T039 [P] Create `tests/contract/Sequences/OcrTextConditionOpenApiTests.cs`: the OpenAPI document lists `ocrText` with its fields (FR-012). Depends on T038.
- [ ] T040 [P] Update `docs/architecture.md` with the `ocrText` condition and the failure reason codes.
- [ ] T041 [P] Add an entry to `CHANGELOG.md`.
- [ ] T042 [P] Update `specs/STATUS.md`.
- [ ] T043 Version bump before the push: run `git fetch origin master` and compare `installer/versioning/version.override.json` with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`. Use absolute paths.
- [ ] T044 Build the solution and run all test projects. Confirm that existing condition tests and `POST /api/ocr/read` tests pass without change (SC-006). Depends on all tasks.
- [ ] T045 Run the steps of `specs/158-ocr-text-condition/quickstart.md`. Depends on T044.

---

## Dependencies and Order

- Phase 1, then Phase 2, then stories. US1 first (MVP). US2, US3, and US4 start after Phase 2, but they edit files that US1 creates (`SequenceStepConditionEvaluator.cs`, `OcrTextConditionReader.cs`, `OcrTextConditionRunTests.cs`, `OcrTextConditionContractTests.cs`). Run US1 before US3 and US4.
- Same-file chains (run in order, never [P]): `SequenceStepConditionEvaluator.cs` T016 then T032; `OcrTextConditionReader.cs` T017 then T031; `SequenceRunner.cs` T025 then T036; `SequencesEndpoints.cs` T010 then T033; `OcrTextConditionContractTests.cs` T014 then T028; `OcrTextConditionReaderTests.cs` T013 then T030; `OcrTextConditionRunTests.cs` T015 then T035; `SequenceExecutionService.cs` T019.
- US2 and US4 are independent of US3.
- T043 runs before the push. T044 and T045 run last.

## Parallel Examples

- Phase 2: T004, T005, T006, T007, T009 touch different files. Run them together; T008 and T010 follow.
- US1 tests: T011 to T015 run together.
- US2 tests: T020 to T024 run together.
- Polish: T037, T040, T041, T042 run together.

## Implementation Strategy

- MVP: Phase 1, Phase 2, and US1. Stop and check with the independent test.
- Then add US2 and US4 (both P1), then US3 (P2), then Polish.

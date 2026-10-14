# Tasks: Quiz Answer Service for Sequences

**Input**: Design documents in `C:\src\GameBot\specs\142-quiz-answer-service\`
**Tests**: Included. The plan and the constitution (Principle II) require unit, contract, and integration tests.
**Format**: `- [ ] T### [P?] [US?] Description with file path`
**Root**: `C:\src\GameBot`. Short names below: `Domain` = `C:\src\GameBot\src\GameBot.Domain`, `Service` = `C:\src\GameBot\src\GameBot.Service`, `Unit` = `C:\src\GameBot\tests\unit`, `Contract` = `C:\src\GameBot\tests\contract`, `Integration` = `C:\src\GameBot\tests\integration`.

**Ordering note (same validation files)**: T019, T029, and T040 edit the same validation files (`Domain\Services\SequenceStepValidationService.cs` and `Domain\Services\ActionPayloadValidationService.cs`). Do these tasks in sequence, in ID order. Do not run them in parallel.

## Phase 1: Setup

- [ ] T001 Add the constants `QuizLookup` (`quiz-lookup`) and `QuizLearn` (`quiz-learn`) in `Domain\Actions\ActionTypes.cs`
- [ ] T002 Create the folder `Domain\Quiz\` and add the entity `QuizRecord` and the type `QuizLookupResult` in `Domain\Quiz\QuizRecord.cs` and `Domain\Quiz\QuizLookupResult.cs` (fields per data-model.md)
- [ ] T003 [P] Confirm in `C:\src\GameBot\src\web-ui` the files that hold the primitive step type list and field form (`SequencesPage.tsx`, `PrimitiveActionFields.tsx`) for T044

## Phase 2: Foundational (blocks all user stories)

- [ ] T004 [P] Implement `QuizTextNormalizer` and `QuizSimilarity` (normalize, Levenshtein similarity 0 to 1) in `Domain\Quiz\QuizTextNormalizer.cs` and `Domain\Quiz\QuizSimilarity.cs`
- [ ] T005 [P] Add unit tests for the normalizer and the similarity in `Unit\Quiz\QuizTextNormalizerTests.cs`
- [ ] T006 [P] Define `IQuizAnswerRepository` (find, upsert, list, get, delete), `IQuizRandom`, and `QuizStoreUnavailableException` in `Domain\Quiz\IQuizAnswerRepository.cs`, `Domain\Quiz\IQuizRandom.cs`, and `Domain\Quiz\QuizStoreUnavailableException.cs`
- [ ] T007 Implement `FileQuizAnswerRepository` (one JSON file per store under `<dataRoot>\quiz\`, SHA-256 file name of the lower-case store name, per-store `SemaphoreSlim`, temp file and replace, damaged file throws `QuizStoreUnavailableException`) in `Domain\Quiz\FileQuizAnswerRepository.cs`
- [ ] T008 [P] Add repository unit tests (restart persistence, concurrent writes, damaged file, key upsert, list skips a damaged store) in `Unit\Quiz\FileQuizAnswerRepositoryTests.cs`
- [ ] T009 [P] Add `QuizLookupPayload` and `QuizLearnPayload` (read parameters, region type, defaults) in `Domain\Quiz\QuizLookupPayload.cs` and `Domain\Quiz\QuizLearnPayload.cs`
- [ ] T010 Add both types to `SequenceActionTypes.All` in `Domain\Actions\SequenceActionTypes.cs` (site 1)
- [ ] T011 Add both types to `supportedActionTypes` in `Domain\Commands\FileSequenceRepository.cs` (site 2)
- [ ] T012 Add both types to `IsServiceLevelAction` in `Domain\Services\SequenceRunner.cs` (site 4)
- [ ] T013 Add `QuizLookupResultKeeper` (per run map, removed at run end) in `Service\Services\Quiz\QuizLookupResultKeeper.cs`

## Phase 3: User Story 1 - Answer a quiz from the store (P1)

**Goal**: `quiz-lookup` gives `pick_N`, `unreadable`, or `service_unavailable`.
**Independent test**: One stored record, one lookup on the same question: outcome `pick_N`, log `known`.

- [ ] T014 [P] [US1] Unit tests for the lookup handler (known, reordered answers, guess, unreadable region, OCR fault gives unreadable, service_unavailable, answer below limit gives guess) in `Unit\Quiz\QuizLookupHandlerTests.cs`
- [ ] T015 [P] [US1] Log-capture test (FR-020a) for the lookup outcomes (`pick_N` known, `pick_N` guess, `unreadable`, `service_unavailable`): assert outcome, `known` or `guess`, normalized question, picked position, and no secret (no credential, token, or device identifier). Open the logging gate component before capture. File: `Unit\Quiz\QuizLookupLogTests.cs`
- [ ] T016 [US1] Implement the lookup logic in `Service\Services\Quiz\QuizLookupHandler.cs` (one frame capture, `OcrRegionReader`, normalize, find, match answer text, random guess through `IQuizRandom`, keep result, one log line per research R10)
- [ ] T017 [US1] Register the repository, `IQuizRandom`, keeper, and `QuizLookupHandler` through one extension method (keep `Program.cs` thin) in `Service\GameBotServiceSetup.cs`. Do this after T016.
- [ ] T018 [US1] Route `quiz-lookup` in `DispatchActionAsync` and the outcome check in `Service\Services\SequenceExecution\SequenceExecutionService.cs` (site 5)
- [ ] T019 [US1] Add lookup shape validation (game, category, store, question, 2+ answers, region size, similarity 0 to 1) in `Domain\Services\SequenceStepValidationService.cs` and `Domain\Services\ActionPayloadValidationService.cs` (site 3). Same files as T029 and T040: keep the order.
- [ ] T020 [P] [US1] Add the step-through preview for `quiz-lookup` (no store change, no device read) in `Service\Services\StepThrough\StepThroughActionPreview.cs` (site 6)
- [ ] T021 [P] [US1] Add the `quiz-lookup` entry, with an example step, to `PayloadDescriptions` in `Service\Swagger\PrimitiveActionSchemaFilter.cs` (site 7)
- [ ] T022 [US1] Contract test: a sequence with a wrong `quiz-lookup` shape returns 400, not 500, in `Contract\Quiz\QuizStepValidationContractTests.cs`
- [ ] T023 [US1] Test that a later `commandOutcome` condition reads the lookup outcome, in `Unit\Quiz\QuizLookupHandlerTests.cs`

## Phase 4: User Story 2 - Learn the correct answer (P1)

**Goal**: `quiz-learn` stores the correct answer of a finished lookup.
**Independent test**: Lookup on an unknown question, learn, lookup again: log `known`.

- [ ] T024 [P] [US2] Unit tests for the learn handler (stored, no match, ambiguous, by position, by region, missing lookup result, service_unavailable, repeat gives one record, replaced answer, guess not stored) in `Unit\Quiz\QuizLearnHandlerTests.cs`
- [ ] T025 [P] [US2] Log-capture test (FR-020a) for the learn outcomes (`stored`, `skipped_no_match`, `skipped_ambiguous`, `service_unavailable`) and for the "replaced" and "no lookup result" log lines: assert outcome and normalized question, and no secret. File: `Unit\Quiz\QuizLearnLogTests.cs`
- [ ] T026 [US2] Implement the learn logic in `Service\Services\Quiz\QuizLearnHandler.cs` (read keeper, match by similarity per R4a, upsert, log "replaced")
- [ ] T027 [US2] Register `QuizLearnHandler` in the same extension method in `Service\GameBotServiceSetup.cs`. Do this after T026.
- [ ] T028 [US2] Route `quiz-learn` in `Service\Services\SequenceExecution\SequenceExecutionService.cs`
- [ ] T029 [US2] Add learn shape validation (lookup reference to an earlier `quiz-lookup` step, exactly one of region or position, position at least 1) in `Domain\Services\SequenceStepValidationService.cs`. Same files as T019 and T040: keep the order.
- [ ] T030 [P] [US2] Add the step-through preview for `quiz-learn` in `Service\Services\StepThrough\StepThroughActionPreview.cs`
- [ ] T031 [P] [US2] Add the `quiz-learn` entry, with an example step, to `PayloadDescriptions` in `Service\Swagger\PrimitiveActionSchemaFilter.cs`
- [ ] T032 [US2] Contract test: wrong `quiz-learn` shapes return 400, not 500, in `Contract\Quiz\QuizStepValidationContractTests.cs`
- [ ] T033 [US2] Integration test of the full loop (lookup guess, learn, lookup known) in `Integration\Quiz\QuizLoopIntegrationTests.cs`
- [ ] T034 [US2] Contract test: the `PayloadDescriptions` entries of `quiz-lookup` and `quiz-learn` hold the example step from `contracts\quiz-steps.md`, and each example passes save-time validation, in `Contract\Quiz\QuizStepExamplesContractTests.cs`. Do this after T021 and T031.

## Phase 5: User Story 3 - Inspect and correct the store (P2)

**Goal**: The operator lists, reads, and deletes records.
**Independent test**: Store two records, list, get, delete one, list again.

- [ ] T035 [P] [US3] Contract tests for the three operations (list with `store`, `game`, and `category` filters, case rules, get, delete, 404 on unknown or bad id) in `Contract\Quiz\QuizEndpointsContractTests.cs`
- [ ] T036 [US3] Implement `GET /api/quiz/records`, `GET /api/quiz/records/{id}`, and `DELETE /api/quiz/records/{id}` per `contracts\quiz-api.md` in `Service\Endpoints\QuizEndpoints.cs`
- [ ] T037 [US3] Map the endpoints in `Service\GameBotServiceSetup.cs` and add the OpenAPI descriptions

## Phase 6: User Story 4 - Keep questions apart by game and category (P2)

**Goal**: A lookup finds only records of its game and category.
**Independent test**: Same question in two categories gives two answers; a step without category is rejected.

- [ ] T038 [P] [US4] Unit tests for isolation by game, category, and store, and for category and store compare without case (game exact), in `Unit\Quiz\FileQuizAnswerRepositoryTests.cs`
- [ ] T039 [US4] Enforce the game and category filters in `FindBest` and reject a blank category or game in the upsert, in `Domain\Quiz\FileQuizAnswerRepository.cs`
- [ ] T040 [US4] Add the game and category rules (missing or blank gives a validation error) in `Domain\Services\SequenceStepValidationService.cs` and `Domain\Services\ActionPayloadValidationService.cs`. Same files as T019 and T029: keep the order.
- [ ] T041 [US4] Contract test: a step without category or game returns 400, and no record without category exists (SC-007), in `Contract\Quiz\QuizStepValidationContractTests.cs`
- [ ] T042 [US4] Single performance test: one lookup on a store with 200 records finishes in under 1 second (SC-005) and in the 50 ms in-memory budget of the unit test, in `Unit\Quiz\QuizLookupPerformanceTests.cs`. Do not add a second performance test.

## Phase 7: Polish

- [ ] T043 [P] Add the type-list sync test (FR-022): loop over `SequenceActionTypes.All` and check for each type a `PayloadDescriptions` entry, the `FileSequenceRepository` allow-list, the validation service, `IsServiceLevelAction` (or its documented non-service set), the dispatch, and the step-through preview. The failure message names the type and the place. File: `Unit\Sequences\SequenceActionTypeSyncTests.cs`
- [ ] T044 [P] Add the two type names to the step type list in `src\web-ui\src\pages\SequencesPage.tsx` and a basic field form for both types in `src\web-ui\src\components\sequences\PrimitiveActionFields.tsx`, add Jest tests, and run `vite build` and `jest` in `C:\src\GameBot\src\web-ui`
- [ ] T045 [P] Update `C:\src\GameBot\docs\architecture.md` (action types, API surface, persistence layout, last-reviewed date) and add a CHANGELOG entry
- [ ] T046 [P] Run the `quickstart.md` checks by hand where the environment allows
- [ ] T047 Run the full .NET build and test suite and fix failures
- [ ] T048 Coverage check (FR-023, R13): run the repository coverage run and read the result for `Domain\Quiz`, `Service\Services\Quiz`, the quiz endpoints, and the new validation branches. Targets: at least 80% line and 70% branch. Add tests for any gap and run again.
- [ ] T049 Set the `Status` line in `C:\src\GameBot\specs\142-quiz-answer-service\spec.md` and update `C:\src\GameBot\specs\STATUS.md`
- [ ] T050 Version bump: run `git fetch origin master`, compare `C:\src\GameBot\installer\versioning\version.override.json` with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `origin/master` patch plus 1. Set `updatedBy` and `updatedAtUtc`. Do nothing if the value is already right. Do this before the PR push.

## Dependencies

- Phase 1, then Phase 2 (blocks all stories).
- Handler DI registration (T017, T027) comes after the handler logic (T016, T026).
- US1 and US2 share `SequenceExecutionService.cs`, the preview file, and the schema filter: do US1 first. US2 needs the keeper (T013) and the lookup (T016).
- Validation tasks T019, T029, and T040 edit the same files: strict ID order, no parallel run.
- US3 needs only Phase 2 (repository). Its endpoint registration (T037) edits `GameBotServiceSetup.cs`: do it after T017 and T027.
- US4 needs Phase 2 and the validation from T019.
- Polish last. T048 (coverage) comes after T047. T050 is the last task before the push.

## Parallel examples

- Phase 2: T004, T006, T009 at the same time; then T005, T008.
- After Phase 2: US3 test T035 and endpoint T036 in parallel with US1 tasks T014 and T015.
- Inside a story: the [P] tasks for preview, swagger, and tests touch different files.

## Implementation strategy

MVP = Phase 1, Phase 2, US1, US2 (lookup and learn work end to end). Then US3 (operator control), US4 (isolation checks), and Polish (sync test, coverage check, version bump).

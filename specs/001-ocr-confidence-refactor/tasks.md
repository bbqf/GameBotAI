# Tasks: OCR Confidence via TSV

## Phase 1: Setup

- [ ] T001 Confirm Tesseract availability and path detection via `GAMEBOT_TESSERACT_PATH` (scripts/local-verify-tesseract.ps1)
- [ ] T002 Add sample TSV fixture assets (tests/TestAssets/ocr/tsv/clear_text.tsv)
- [ ] T003 Add mixed-quality TSV fixture (tests/TestAssets/ocr/tsv/mixed_quality.tsv)
- [ ] T004 Add malformed TSV fixture (tests/TestAssets/ocr/tsv/malformed.tsv)
- [ ] T005 Document required env vars in `ENVIRONMENT.md` (append OCR section)

## Phase 2: Foundational

- [ ] T006 Create `src/GameBot.Domain/Triggers/Evaluators/TesseractTsvParser.cs`
- [ ] T007 Implement TSV header validation in parser file
- [ ] T008 Implement TSV row parsing & token construction in parser file
- [ ] T009 Implement aggregation helper excluding -1 & empty tokens in parser file
- [ ] T010 Add unit tests for header validation (tests/unit/Ocr/TesseractTsvParserHeaderTests.cs)
- [ ] T011 Add unit tests for row parsing (tests/unit/Ocr/TesseractTsvParserRowsTests.cs)
- [ ] T012 Add unit tests for aggregation (tests/unit/Ocr/TesseractTsvParserAggregationTests.cs)
- [ ] T013 Add unit tests for malformed TSV handling (tests/unit/Ocr/TesseractTsvParserMalformedTests.cs)
- [ ] T014 Integrate parser into `TesseractProcessOcr` (src/GameBot.Domain/Triggers/Evaluators/TesseractProcessOcr.cs)
- [ ] T015 Replace legacy confidence heuristic with TSV aggregate (src/GameBot.Domain/Triggers/Evaluators/TesseractProcessOcr.cs)
- [ ] T016 Add fallback to legacy text mode when TSV fails (src/GameBot.Domain/Triggers/Evaluators/TesseractProcessOcr.cs)
- [ ] T017 Extend `OcrResult` or create `OcrEvaluationResult` DTO (src/GameBot.Domain/Triggers/Evaluators/OcrEvaluationResult.cs)
- [ ] T018 Adapt trigger evaluation to use new confidence property (src/GameBot.Domain/Services/TriggerEvaluationService.cs)
- [ ] T019 Update OpenAPI contract for OCR test endpoint (specs/001-ocr-confidence-refactor/contracts/ocr-evaluation.openapi.yaml)

## Phase 3: User Story 1 (Accurate OCR confidence)

- [ ] T020 [US1] Add integration test with clear text image (tests/integration/OcrConfidenceClearTextTests.cs)
- [ ] T021 [P] [US1] Add integration test with mixed-quality image (tests/integration/OcrConfidenceMixedQualityTests.cs)
- [ ] T022 [US1] Assert per-token confidence matches TSV conf values (tests/integration/OcrConfidenceClearTextTests.cs)
- [ ] T023 [P] [US1] Assert low-quality tokens have reduced confidence (tests/integration/OcrConfidenceMixedQualityTests.cs)
- [ ] T024 [US1] Add test for noise tokens (confidence -1 excluded) (tests/integration/OcrConfidenceNoiseTests.cs)
- [ ] T025 [US1] Add performance timing assertion (parse <5ms) (tests/integration/OcrConfidencePerformanceTests.cs)
- [ ] T026 [US1] Update quickstart with token confidence example (specs/001-ocr-confidence-refactor/quickstart.md)

## Phase 4: User Story 2 (Configurable aggregation)

- [ ] T027 [US2] Implement aggregation strategy interface (src/GameBot.Domain/Triggers/Evaluators/IAggregationStrategy.cs)
- [ ] T028 [P] [US2] Implement MeanAggregationStrategy (src/GameBot.Domain/Triggers/Evaluators/MeanAggregationStrategy.cs)
- [ ] T029 [US2] Wire strategy selection (environment/config) (src/GameBot.Domain/Triggers/Evaluators/TesseractProcessOcr.cs)
- [ ] T030 [US2] Add unit tests for mean strategy correctness (tests/unit/Ocr/AggregationStrategyMeanTests.cs)
- [ ] T031 [P] [US2] Add test verifying exclusion of -1 tokens (tests/unit/Ocr/AggregationStrategyExclusionsTests.cs)
- [ ] T032 [US2] Document aggregation configuration (specs/001-ocr-confidence-refactor/quickstart.md)

## Phase 5: User Story 3 (Backward-safe output contract)

- [ ] T033 [US3] Preserve legacy fields in `OcrResult` (src/GameBot.Domain/Triggers/Evaluators/OcrEvaluationResult.cs)
- [ ] T034 [US3] Add tokens[].confidence & aggregate confidence (src/GameBot.Domain/Triggers/Evaluators/OcrEvaluationResult.cs)
- [ ] T035 [US3] Update API serialization mapping (src/GameBot.Service/Endpoints/TriggersEndpoints.cs)
- [ ] T036 [US3] Add contract tests for schema stability (tests/contract/OcrResultSchemaTests.cs)
- [ ] T037 [P] [US3] Add integration test asserting legacy fields present (tests/integration/OcrResultBackwardCompatTests.cs)
- [ ] T038 [US3] Add JSON sample to README (README.md)
- [ ] T039 [US3] Update changelog with non-breaking addition (CHANGELOG.md)

## Phase 6: Polish & Cross-Cutting

- [ ] T040 Add logging for TSV invocation duration (src/GameBot.Domain/Triggers/Evaluators/TesseractProcessOcr.cs)
- [ ] T041 Add structured log for parse error reason (src/GameBot.Domain/Triggers/Evaluators/TesseractTsvParser.cs)
- [ ] T042 Add benchmark harness (tests/perf/OcrParsingBenchmarks.cs)
- [ ] T043 Add doc section to ENVIRONMENT.md for performance tuning (ENVIRONMENT.md)
- [ ] T044 Final pass: ensure memory cleanup of temp files (src/GameBot.Domain/Triggers/Evaluators/TesseractProcessOcr.cs)
- [ ] T045 Add coverage report validation for new code (tools/coverage/coverage-ocr.conf)
- [ ] T046 Update spec success criteria with measured values (specs/001-ocr-confidence-refactor/spec.md)
- [ ] T047 PR review checklist entry (specs/001-ocr-confidence-refactor/checklists/requirements.md)

## Dependencies & Story Order
1. Phase 1 must complete before TSV parser implementation.
2. Phase 2 foundational tasks required before any user story tests.
3. User Story 1 (Phase 3) delivers MVP (accurate confidence).
4. User Story 2 builds on parser (strategy abstraction).
5. User Story 3 depends on new result fields from earlier phases.

## Parallel Execution Examples
- T020 and T021 can run in parallel (distinct test files & fixtures).
- T028 and T031 can run in parallel (separate aggregation tests/implementation).
- T037 and T036 can run in parallel (integration vs contract tests).

## Independent Test Criteria Per Story
- US1: Clear text & mixed-quality images produce expected token confidences and aggregate metrics without schema break.
- US2: Mean aggregation yields documented result; exclusion logic validated; switching strategy configurable (future extension baseline set).
- US3: Legacy consumers still parse original fields; new confidence fields present; contract tests pass.

## MVP Scope
Deliver Phase 1–3 (Setup, Foundational, User Story 1) for initial accuracy improvements; defer strategy abstraction & backward-compat validation if necessary.

## Format Validation
All tasks follow: `- [ ] T### optional [P] optional [US#] Description with file path`.

*** End of Tasks ***
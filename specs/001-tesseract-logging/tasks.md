# Tasks: Tesseract Logging & Coverage

**Input**: Design documents from `/specs/001-tesseract-logging/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required for all executable logic. Integration + coverage verification MUST fail before implementation per Constitution.

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Verify repo tooling for .NET 9 by running `dotnet --info` and documenting output in `ENVIRONMENT.md`
- [X] T002 Ensure coverage tooling dependencies (`coverlet.collector`, `ReportGenerator`) are listed in `tests/integration/GameBot.IntegrationTests.csproj`
- [X] T003 [P] Add placeholders for `tools/coverage/` scripts directory per plan structure

---

## Phase 2: Foundational (Blocking Prerequisites)

**All user stories depend on these tasks.**

- [X] T004 Create `src/GameBot.Service/Logging/TesseractInvocationLogger.cs` skeleton with TODOs
- [X] T005 Extend `tests/TestAssets/Ocr/` directory with sample bitmap fixtures referenced in quickstart (create `fixtures/ocr/sample-score.png`)
- [X] T006 Update `.gitignore` to include `tools/coverage/output/` and `data/coverage/`
- [X] T007 Document debug logging enablement in `ENVIRONMENT.md` using plan quickstart guidance

---

## Phase 3: User Story 1 – Audit Every Tesseract Call (Priority P1) 🎯 MVP

**Goal**: Structured debug log emitted for every Tesseract invocation with sanitized arguments, stdout/stderr, duration, and correlation ID.
**Independent Test**: Enable debug level for `TesseractProcessOcr`, trigger single OCR call, confirm one log entry includes command, args, env overrides, duration, exit code, stdout/stderr with truncation flag.

### Tests (write first)
- [X] T008 [P] [US1] Add unit tests in `tests/unit/TextOcr/TesseractInvocationLoggerTests.cs` covering argument redaction + truncation behavior
- [X] T009 [P] [US1] Add integration test in `tests/integration/TextOcrTesseractTests.cs` verifying debug logs include CLI context when level = Debug

### Implementation
- [X] T010 [P] [US1] Implement `TesseractInvocationLogger` helper in `src/GameBot.Service/Logging/TesseractInvocationLogger.cs` (structured log template + truncation logic)
- [X] T011 [P] [US1] Wire helper into `src/GameBot.Domain/Triggers/Evaluators/TesseractProcessOcr.cs` (capture stdout/stderr async, sanitize args/env)
- [X] T012 [US1] Ensure `TesseractProcessOcr` respects runtime log level (skip heavy logging when Info+) and emits correlation IDs
- [X] T013 [US1] Add documentation snippet to `specs/001-tesseract-logging/quickstart.md` showing log sample (update existing section)

Checkpoint: Debug logging captures all required context without leaking secrets.

---

## Phase 4: User Story 2 – Validate OCR Pipeline with Tests (Priority P2)

**Goal**: ≥70% line coverage for Tesseract integration namespace via deterministic tests covering success, failure, timeout, malformed output.
**Independent Test**: Run coverage script, confirm Cobertura report shows ≥70% for `GameBot.Domain.Triggers.Evaluators.Tesseract*`.

### Tests
- [X] T014 [P] [US2] Add failure + timeout scenarios in `tests/integration/TextOcrTesseractTests.cs` using mocked process runner
- [X] T015 [P] [US2] Add malformed-output unit tests in `tests/unit/TextOcr/TesseractProcessOcrTests.cs`

### Implementation & Tooling
- [X] T016 [US2] Introduce injectable runner in `TesseractProcessOcr` to enable deterministic mocks (`ITestOcrProcessRunner` improvements) in `src/GameBot.Domain/Triggers/Evaluators/TesseractProcessOcr.cs`
- [X] T017 [US2] Create coverage script `tools/coverage/report.ps1` (accept namespace filter + target percent, run dotnet test with coverlet)
- [X] T018 [US2] Update `tests/integration/GameBot.IntegrationTests.csproj` to include coverlet settings + exclude noise per plan
- [X] T019 [US2] Add coverage enforcement to CI doc (`ENVIRONMENT.md` or pipeline doc) referencing script usage

Checkpoint: Running script locally/CI fails when coverage <70%.

---

## Phase 5: User Story 3 – Surface Coverage Status to Stakeholders (Priority P3)

**Goal**: Provide API/CLI summary showing coverage %, target, uncovered scenarios.
**Independent Test**: Call coverage summary endpoint or command; verify message states coverage, target, pass/fail, uncovered scenarios.

### Tests
- [ ] T020 [P] [US3] Add API contract test in `tests/contract/OcrCoverageContractTests.cs` for `/api/ocr/coverage`
- [ ] T021 [P] [US3] Add integration test hitting `GET /api/ocr/coverage` verifying JSON mirrors latest report file

### Implementation
- [ ] T022 [US3] Implement coverage summary persistence (write JSON to `data/coverage/latest.json`) inside coverage script
- [ ] T023 [US3] Add `CoverageSummaryService` in `src/GameBot.Service/Services/Ocr/CoverageSummaryService.cs` to read summary file with fallback when missing/stale
- [ ] T024 [US3] Add endpoint in `src/GameBot.Service/Endpoints/CoverageEndpoints.cs` matching OpenAPI contract
- [ ] T025 [US3] Document operator steps in `specs/001-tesseract-logging/quickstart.md` section 4 (already stubbed) with final instructions

Checkpoint: Stakeholders can retrieve latest coverage info without parsing Cobertura manually.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T026 [P] Run `dotnet format` + analyzers across touched projects
- [ ] T027 Update `README.md` and `CHANGELOG.md` summarizing new logging + coverage tooling
- [ ] T028 [P] Validate quickstart instructions end-to-end (enable debug logging, run tests, fetch API) and note issues
- [ ] T029 Ensure `tools/coverage/` output is ignored by git + update `.gitignore` if needed

---

## Dependencies & Execution Order

1. **Setup -> Foundational** (sequential)
2. **Foundational -> User Stories** (US1, US2, US3 can start after T004–T007 complete)
3. **User Story Priority**: US1 (MVP) → US2 → US3 (can run parallel once dependencies satisfied)
4. **Polish** after desired stories complete

### Story Parallelism Examples
- US1 logging helper (T010) can proceed in parallel with test authoring (T008) once foundational tasks done.
- US2 coverage script (T017) can run parallel with new tests (T014/T015) since they touch different files.
- US3 API endpoint (T024) can be developed while coverage persistence (T022) finishes, provided JSON schema finalized.

## MVP Scope
- Complete through Phase 3 (User Story 1). Provides full observability of Tesseract calls even before coverage tooling is ready.

## Task Counts
- Total tasks: 29
  - Setup: 3
  - Foundational: 4
  - US1: 6
  - US2: 6
  - US3: 6
  - Polish: 4
- Parallel-friendly tasks: T003, T005, T006, T008, T009, T010, T011, T014, T015, T017, T020, T021, T022, T026, T028, T029

## Independent Tests per Story
- **US1**: Integration test verifying debug log content (T009)
- **US2**: Coverage script run + integration scenarios (T017, T014)
- **US3**: Contract + integration tests for coverage endpoint (T020, T021)

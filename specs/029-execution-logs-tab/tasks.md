# Tasks: Execution Logs Tab

**Input**: Design documents from `C:\src\GameBot\specs\029-execution-logs-tab\`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are REQUIRED for executable logic per constitution.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., [US1], [US2], [US3])
- Include exact file paths in descriptions
- **Execution Order Rule**: Task execution order is determined by phase placement and dependency notes; task IDs are stable tracking identifiers and may not be strictly numeric by display order after later insertions.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Align routing, API contracts, and test scaffolding for feature work.

**Traceability**: Setup tasks are shared enablers for all feature requirements (FR-001 through FR-028) and all success criteria (SC-001 through SC-005).

- [X] T001 Verify baseline builds/tests via workspace tasks and capture baseline in C:\src\GameBot\specs\029-execution-logs-tab\quickstart.md
- [X] T002 Create web UI page/test stubs for execution logs in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx and C:\src\GameBot\src\web-ui\src\pages\__tests__\ExecutionLogs.test.tsx
- [X] T003 [P] Create execution logs API client stubs in C:\src\GameBot\src\web-ui\src\services\executionLogsApi.ts
- [X] T004 [P] Add contract test file scaffold for execution logs APIs in C:\src\GameBot\tests\contract\ExecutionLogs\ExecutionLogsApiContractTests.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement shared backend query/detail foundations required by all stories.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Extend query model for sort/filter defaults and validation in C:\src\GameBot\src\GameBot.Domain\Logging\ExecutionLogModels.cs
- [X] T006 [P] Implement repository-side combined sort/filter and default page size 50 in C:\src\GameBot\src\GameBot.Domain\Logging\FileExecutionLogRepository.cs
- [X] T007 [P] Add repository unit tests for case-insensitive contains filtering and sortable columns in C:\src\GameBot\tests\unit\ExecutionLogs\ExecutionLogRepositoryQueryTests.cs
- [X] T008 Update list endpoint query parameters (`sortBy`, `sortDirection`, column filters, page size) in C:\src\GameBot\src\GameBot.Service\Endpoints\ExecutionLogsEndpoints.cs
- [X] T009 [P] Add service-layer query normalization for default sort/paging in C:\src\GameBot\src\GameBot.Service\Services\ExecutionLog\ExecutionLogService.cs
- [X] T010 [P] Implement/refresh execution logs contract tests for list and detail endpoints in C:\src\GameBot\tests\contract\ExecutionLogs\ExecutionLogsApiContractTests.cs
- [X] T011 Add integration test for combined sort+filter backend behavior in C:\src\GameBot\tests\integration\ExecutionLogs\ExecutionLogsQueryIntegrationTests.cs

**Checkpoint**: Foundation ready — user stories can begin.

---

## Phase 3: User Story 1 - Review Recent Execution Outcomes (Priority: P1) 🎯 MVP

**Goal**: Provide the new Execution Logs tab with default list columns, default sorting, per-column filtering, and combined backend query behavior.

**Independent Test**: Open app, confirm tab order (`Execution` -> `Execution Logs` -> `Configuration`), see 50-row default list with timestamp/object/status, change sort by clicking headers, apply per-column free-text filters, and verify combined sort+filter results.

### Tests for User Story 1

- [X] T012 [P] [US1] Add navigation/tab order UI test in C:\src\GameBot\src\web-ui\src\components\__tests__\Navigation.test.tsx
- [X] T013 [P] [US1] Add page behavior test for default columns/sort/filter controls in C:\src\GameBot\src\web-ui\src\pages\__tests__\ExecutionLogs.test.tsx
- [X] T014 [P] [US1] Add backend integration test for default page size 50 and timestamp-desc default sort in C:\src\GameBot\tests\integration\ExecutionLogs\ExecutionLogsQueryIntegrationTests.cs
- [X] T015 [P] [US1] Add authorization-visibility parity test for Execution Logs tab/data access in C:\src\GameBot\tests\integration\ExecutionLogs\ExecutionLogsAuthorizationIntegrationTests.cs

### Implementation for User Story 1

- [X] T016 [US1] Insert Execution Logs tab between Execution and Configuration in C:\src\GameBot\src\web-ui\src\components\Navigation.tsx
- [X] T017 [US1] Register Execution Logs page route/view wiring in C:\src\GameBot\src\web-ui\src\pages\Execution.tsx and C:\src\GameBot\src\web-ui\src\pages\Configuration.tsx
- [X] T018 [US1] Apply existing authoring/execution visibility policy to Execution Logs navigation and APIs in C:\src\GameBot\src\web-ui\src\components\Navigation.tsx and C:\src\GameBot\src\GameBot.Service\Endpoints\ExecutionLogsEndpoints.cs
- [X] T019 [P] [US1] Implement list query client with sort/filter/page parameters in C:\src\GameBot\src\web-ui\src\services\executionLogsApi.ts
- [X] T020 [US1] Implement execution logs list page with default columns and sort header interactions in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx
- [X] T021 [US1] Add per-column free-text filter controls and request binding in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx
- [X] T022 [US1] Add empty-state and no-results-state messaging in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Understand One Execution in Plain Language (Priority: P1)

**Goal**: Provide a readable detail experience for a selected row with summary, related object links, optional snapshot, and step outcomes (no raw JSON).

**Independent Test**: Select a row from list and verify non-technical detail rendering, links, optional snapshot handling, and step outcomes with clear statuses/messages.

### Tests for User Story 2

- [X] T023 [P] [US2] Add detail endpoint contract coverage for required fields and 404 behavior in C:\src\GameBot\tests\contract\ExecutionLogs\ExecutionLogsApiContractTests.cs
- [X] T024 [P] [US2] Add UI test asserting no raw JSON is rendered in details panel in C:\src\GameBot\src\web-ui\src\pages\__tests__\ExecutionLogs.test.tsx
- [X] T025 [P] [US2] Add integration test for related object link and snapshot availability mapping in C:\src\GameBot\tests\integration\ExecutionLogs\ExecutionLogsDetailIntegrationTests.cs

### Implementation for User Story 2

- [X] T026 [US2] Add detail DTO mapping for user-readable fields and step outcomes in C:\src\GameBot\src\GameBot.Service\Endpoints\ExecutionLogsEndpoints.cs
- [X] T027 [P] [US2] Add detail projection helper methods for readable text defaults in C:\src\GameBot\src\GameBot.Service\Services\ExecutionLog\ExecutionLogService.cs
- [X] T028 [US2] Implement detail fetch/select behavior from row selection in C:\src\GameBot\src\web-ui\src\services\executionLogsApi.ts and C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx
- [X] T029 [US2] Implement detail panel sections (summary, links, snapshot, step outcomes) in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx
- [X] T030 [US2] Add unavailable-link and missing-snapshot user feedback states in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Use the Page Across Device Sizes (Priority: P2)

**Goal**: Provide desktop split-pane and phone drill-down variants with preserved sort/filter state and timestamp display mode toggle (exact local default, switchable relative).

**Independent Test**: At desktop width, list and details are visible together; at phone width, list-first then detail drill-down works; returning preserves filter/sort; timestamp mode toggle works.

### Tests for User Story 3

- [X] T031 [P] [US3] Add responsive layout behavior tests (desktop split, phone drill-down) in C:\src\GameBot\src\web-ui\src\pages\__tests__\ExecutionLogsResponsive.test.tsx
- [X] T032 [P] [US3] Add UI test for timestamp display toggle default exact and relative mode switch in C:\src\GameBot\src\web-ui\src\pages\__tests__\ExecutionLogs.test.tsx
- [X] T033 [P] [US3] Add UI test for latest-request-wins behavior under rapid sort/filter changes in C:\src\GameBot\src\web-ui\src\pages\__tests__\ExecutionLogs.test.tsx

### Implementation for User Story 3

- [X] T034 [US3] Implement responsive desktop split-pane and phone drill-down views in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx
- [X] T035 [US3] Implement state preservation for sort/filter while navigating list/detail on phone in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx
- [X] T036 [US3] Implement timestamp display mode toggle (default exact local, switchable relative) in C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx
- [X] T037 [US3] Implement latest-request-wins request coordination in C:\src\GameBot\src\web-ui\src\services\executionLogsApi.ts and C:\src\GameBot\src\web-ui\src\pages\ExecutionLogs.tsx

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validate performance budgets, quality gates, and documentation consistency across stories.

**Evidence Format**: For each task in this phase, record (1) command or procedure executed, (2) pass/fail result, (3) artifact path or output reference in `C:\src\GameBot\docs\regression-pass.md`.

- [X] T038 [P] Add backend/API performance test for 1,000-log dataset local p95 budgets in C:\src\GameBot\tests\integration\ExecutionLogs\ExecutionLogsPerformanceIntegrationTests.cs
- [X] T039 [P] Add CI-relaxed performance assertions and pipeline hook documentation in C:\src\GameBot\specs\029-execution-logs-tab\quickstart.md and C:\src\GameBot\docs\validation.md
- [X] T040 Run full verify (`build`, `test`, and execution logs scenarios) and record evidence in C:\src\GameBot\docs\regression-pass.md
- [X] T041 [P] Update feature docs and release notes for new tab behavior in C:\src\GameBot\CHANGELOG.md and C:\src\GameBot\docs\ui-audit.md
- [X] T042 [P] Define and run non-technical usability validation for status/step-outcome comprehension (SC-004) in C:\src\GameBot\docs\validation.md and C:\src\GameBot\docs\regression-pass.md
- [X] T043 [P] Define and run timed desktop/phone task-completion validation for execution discovery (SC-005) in C:\src\GameBot\docs\validation.md and C:\src\GameBot\docs\regression-pass.md
- [X] T044 [P] Add frontend lint/format gate execution task and evidence capture in C:\src\GameBot\docs\regression-pass.md
- [ ] T045 [P] Add backend format/static-analysis gate execution task and evidence capture in C:\src\GameBot\docs\regression-pass.md
- [X] T046 [P] Add security scan (SAST/secret scan) execution task and evidence capture in C:\src\GameBot\docs\regression-pass.md
- [X] T047 [P] Add coverage threshold verification task (>=80% line, >=70% branch for touched areas) in C:\src\GameBot\docs\regression-pass.md
- [X] T048 [P] Add CI gate verification task for test/coverage/performance blocking conditions in C:\src\GameBot\docs\validation.md

**Per-Task Done Criteria**:

- **T038**: Done when a local performance test artifact demonstrates p95 first-open <100ms and p95 filter/sort update <300ms at 1,000 logs.
- **T039**: Done when CI-relaxed thresholds (<200ms and <450ms p95) and pipeline hook documentation are recorded in `C:\src\GameBot\docs\validation.md`.
- **T040**: Done when `build`, `test`, and execution-log scenario verification outputs are captured with pass/fail outcomes in `C:\src\GameBot\docs\regression-pass.md`.
- **T041**: Done when changelog and UI audit entries describe user-visible behavior changes for Execution Logs.
- **T042**: Done when usability validation evidence shows status/step-outcome comprehension results and sample size in docs.
- **T043**: Done when timed desktop/phone discovery validation records completion times and pass-rate against SC-005.
- **T044**: Done when frontend lint/format command output is captured and marked pass.
- **T045**: Done when backend formatting/static-analysis outputs are captured and marked pass.
- **T046**: Done when SAST/secret scan outputs are captured and marked pass (or justified with approved risk note).
- **T047**: Done when coverage report demonstrates >=80% line and >=70% branch for touched areas.
- **T048**: Done when CI gate rules for tests/coverage/performance are validated as blocking on threshold failure.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all user story implementation.
- **Phase 3 (US1)**: Depends on Phase 2 completion.
- **Phase 4 (US2)**: Depends on Phase 2 completion; can proceed after/alongside US1 once list foundation exists.
- **Phase 5 (US3)**: Depends on US1 list page availability and Phase 2 backend foundations.
- **Phase 6 (Polish)**: Depends on selected user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Independent after foundational phase; MVP scope.
- **US2 (P1)**: Independent after foundational phase and list-selection plumbing from US1.
- **US3 (P2)**: Builds on US1 UI surface and finalizes responsive/state/performance behaviors.

### Within Each User Story

- Write tests first and confirm they fail.
- Implement models/query logic before endpoint/UI wiring.
- Implement endpoint/UI wiring before polish/refinement.
- Complete story checkpoint before broadening scope.

---

## Parallel Execution Examples

### User Story 1

- Run T012, T013, and T014 in parallel (separate test files).
- Run T015 in parallel with T012/T013/T014, then run T018 with T016/T017 before final US1 verification.
- Run T019 in parallel with T016/T017/T018 after foundational completion.

### User Story 2

- Run T023, T024, and T025 in parallel.
- Run T027 in parallel with T026, then complete T028-T030 sequentially.

### User Story 3

- Run T031, T032, and T033 in parallel.
- Run T036 in parallel with T034 once responsive skeleton exists.

### Polish & Governance

- Run T042 and T043 in parallel (usability validations).
- Run T044, T045, T046, and T047 in parallel before T048 CI-gate validation.

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate US1 independently (tab order, default list behavior, sort/filter correctness).
4. Demo/deploy MVP increment.

### Incremental Delivery

1. Add US2 for non-technical details and object/snapshot/step outcomes.
2. Add US3 for responsive variants, timestamp mode toggle, and latest-request-wins UX behavior.
3. Complete Phase 6 performance/documentation polish and evidence criteria.

### Parallel Team Strategy

1. One developer drives backend foundational tasks (T005-T011).
2. One developer drives US1 UI page/tab and visibility tasks (T016-T022) after foundations.
3. One developer prepares US2/US3 tests and UX refinements in parallel where marked [P].
4. One developer runs cross-cutting quality/security/coverage/usability tasks (T042-T048).

---

## Notes

- [P] tasks indicate no file-level dependency conflicts.
- [US#] labels map directly to spec user stories for traceability.
- Every task includes explicit file paths and is immediately executable.
- Setup and polish tasks without `[US#]` are intentional cross-cutting tasks and should include evidence links in `C:\src\GameBot\docs\regression-pass.md`.
- Suggested commit cadence: per task or tightly related task pair.
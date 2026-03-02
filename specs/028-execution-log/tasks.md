# Tasks: Persisted Execution Log

**Input**: Design documents from `/specs/028-execution-log/`
**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/`

**Tests**: Tests are REQUIRED for executable logic per the Constitution.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare execution-log scaffolding, contract references, and fixtures.

- [X] T001 Add execution log route constant in src/GameBot.Service/ApiRoutes.cs
- [X] T002 [P] Add execution-log sample payload fixtures in tests/TestAssets/execution-logs/
- [X] T003 [P] Add execution-log sample data folder placeholder in data/execution-logs/.gitkeep
- [X] T004 [P] Align contract artifact metadata for execution logs in specs/028-execution-log/contracts/execution-log.openapi.yaml

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build core domain/service/repository plumbing required before user story work.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T005 Create execution log domain entities and value objects in src/GameBot.Domain/Logging/ExecutionLogModels.cs
- [X] T006 [P] Add retention policy domain model in src/GameBot.Domain/Logging/ExecutionLogRetentionPolicy.cs
- [X] T007 Create execution log repository interface in src/GameBot.Domain/Logging/IExecutionLogRepository.cs
- [X] T008 Implement file-backed execution log repository in src/GameBot.Domain/Logging/FileExecutionLogRepository.cs
- [X] T009 [P] Add execution log DTOs for list/detail responses in src/GameBot.Service/Models/ExecutionLogs.cs
- [X] T010 [P] Implement execution log masking/sanitization helper in src/GameBot.Service/Services/ExecutionLog/ExecutionLogSanitizer.cs
- [X] T011 Implement execution log write/query service in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [X] T012 Wire execution-log dependencies and storage paths in src/GameBot.Service/Program.cs

**Checkpoint**: Foundation ready; user stories can be implemented independently.

---

## Phase 3: User Story 1 - Review execution outcomes (Priority: P1)

**Goal**: Persist and retrieve command/sequence execution outcomes with timestamp, identifiable object, and final status.

**Independent Test**: Run one successful command and one failing execution, then verify persisted and queryable entries contain timestamp, object identity, hierarchy context, and final status.

### Tests for User Story 1

- [X] T013 [P] [US1] Add unit tests for execution entry mapping and required fields in tests/unit/ExecutionLogs/ExecutionLogEntryMappingTests.cs
- [X] T014 [P] [US1] Add integration tests for command execution log persistence in tests/integration/ExecutionLogs/CommandExecutionLoggingIntegrationTests.cs
- [X] T015 [P] [US1] Add integration tests for sequence execution log persistence in tests/integration/ExecutionLogs/SequenceExecutionLoggingIntegrationTests.cs
- [X] T016 [P] [US1] Add contract tests for execution-log list/detail endpoints in tests/contract/OpenApiContractTests.cs
- [X] T017 [P] [US1] Add integration test for historical log actionability after object rename/delete in tests/integration/ExecutionLogs/ExecutionLogObjectSnapshotIntegrationTests.cs

### Implementation for User Story 1

- [X] T018 [US1] Add execution-log endpoints (list/detail) in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs
- [X] T019 [US1] Map execution-log endpoints in service startup in src/GameBot.Service/Program.cs
- [X] T020 [US1] Persist command execution results to execution log in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T021 [US1] Persist sequence execution results to execution log in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [X] T022 [US1] Capture immutable object identity snapshots for logs in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [X] T023 [US1] Implement paged list/detail query logic in src/GameBot.Domain/Logging/FileExecutionLogRepository.cs

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Trace sequence hierarchy (Priority: P2)

**Goal**: Preserve parent-child execution context and dual navigation (direct object + parent hierarchy path).

**Independent Test**: Execute the same command standalone and within a sequence, then verify logs differentiate both contexts and include direct + parent navigation context when nested.

### Tests for User Story 2

- [X] T024 [P] [US2] Add unit tests for hierarchy context construction in tests/unit/ExecutionLogs/ExecutionHierarchyContextTests.cs
- [X] T025 [P] [US2] Add integration tests for nested sequence parent-child linkage in tests/integration/ExecutionLogs/ExecutionHierarchyIntegrationTests.cs
- [X] T026 [P] [US2] Add integration tests for direct and parent navigation path persistence in tests/integration/ExecutionLogs/ExecutionNavigationIntegrationTests.cs

### Implementation for User Story 2

- [X] T027 [US2] Add hierarchy context builder for root/parent/depth/order fields in src/GameBot.Service/Services/ExecutionLog/ExecutionHierarchyBuilder.cs
- [X] T028 [US2] Add navigation context builder for directPath and parentPath in src/GameBot.Service/Services/ExecutionLog/ExecutionNavigationBuilder.cs
- [X] T029 [US2] Apply hierarchy + navigation builders during command logging in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T030 [US2] Apply hierarchy + navigation builders for nested sequence logging in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [X] T031 [US2] Extend execution-log response mapping for hierarchy and navigation fields in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs

**Checkpoint**: User Stories 1 and 2 are independently functional and testable.

---

## Phase 5: User Story 3 - Understand what actually happened (Priority: P3)

**Goal**: Log concise step-level outcome details, support `executed/not_executed`, mask sensitive values, and make retention configurable.

**Independent Test**: Execute parameterized flow with one detected-and-executed step and one detection-below-threshold step, then verify user-facing details, masked values, and `executed/not_executed` outcomes are persisted; update retention config and verify policy persistence/cleanup behavior.

### Tests for User Story 3

- [X] T032 [P] [US3] Add unit tests for detail masking/redaction rules in tests/unit/ExecutionLogs/ExecutionLogSanitizerTests.cs
- [X] T033 [P] [US3] Add integration tests for executed/not_executed step outcomes in tests/integration/ExecutionLogs/ExecutionStepOutcomeIntegrationTests.cs
- [X] T034 [P] [US3] Add integration tests for retention policy update and cleanup in tests/integration/ExecutionLogs/ExecutionLogRetentionIntegrationTests.cs
- [X] T035 [P] [US3] Add contract tests for retention endpoints in tests/contract/OpenApiContractTests.cs
- [X] T036 [P] [US3] Add integration tests for summary/detail conciseness bounds and truncation marker in tests/integration/ExecutionLogs/ExecutionLogConcisenessIntegrationTests.cs

### Implementation for User Story 3

- [X] T037 [US3] Capture concise outcome detail records (detection/tap/skip reasons) in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T038 [US3] Normalize step outcomes to executed/not_executed in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [X] T039 [US3] Enforce masking/redaction prior to persistence in src/GameBot.Service/Services/ExecutionLog/ExecutionLogSanitizer.cs
- [X] T040 [US3] Enforce summary/detail conciseness bounds and truncation marker in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [X] T041 [US3] Add retention policy repository and persistence in src/GameBot.Domain/Logging/ExecutionLogRetentionPolicyRepository.cs
- [X] T042 [US3] Add retention get/update endpoints in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs
- [X] T043 [US3] Add asynchronous retention cleanup worker in src/GameBot.Service/Hosted/ExecutionLogRetentionCleanupService.cs
- [X] T044 [US3] Register retention cleanup hosted service in src/GameBot.Service/Program.cs

**Checkpoint**: All user stories are independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validate quality gates, documentation, and performance/operational evidence.

- [X] T045 [P] Update execution-log usage and validation steps in specs/028-execution-log/quickstart.md
- [X] T046 [P] Add implementation validation evidence and privacy checks in docs/validation.md
- [x] T047 Run .NET format verification for touched code via GameBot.sln
- [X] T048 Run static analysis build (warnings reviewed/justified) via GameBot.sln
- [X] T049 Run backend tests and analyze results with scripts/analyze-test-results.ps1
- [X] T050 Collect touched-area coverage report (>=80% line, >=70% branch) in tools/coverage/
- [X] T051 Run security/secret scan and record results in docs/validation.md
- [X] T052 Execute p95 write/query performance measurement and record results in docs/perf-checklist.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies, starts immediately.
- **Phase 2 (Foundational)**: depends on Phase 1 and blocks all user stories.
- **Phase 3-5 (User Stories)**: depend on Phase 2 completion; stories can run in parallel if staffed.
- **Phase 6 (Polish)**: depends on completion of desired user stories.

### User Story Dependencies

- **US1 (P1)**: starts after foundational phase; no dependency on other stories.
- **US2 (P2)**: starts after foundational phase and depends on US1 write-path tasks T020-T021; hierarchy/navigation tasks proceed once baseline persistence flow is in place.
- **US3 (P3)**: starts after foundational phase; can run in parallel with US2, but should land after US1 baseline persistence flow.

### Within Each User Story

- Tests are written first and verified failing before implementation.
- Domain/repository changes precede endpoint mapping for that story.
- Endpoint/service integration completes before story checkpoint validation.

### Suggested Story Completion Order

1. Phase 1 → Phase 2
2. MVP: Phase 3 (US1)
3. Phase 4 (US2) and Phase 5 (US3)
4. Phase 6 polish and full verification

---

## Parallel Execution Examples

### User Story 1

- Run T013, T014, T015, T016, and T017 in parallel (separate test files/layers).
- Run T018 and T022 in parallel (endpoint file vs service file) before integrating with T019/T020/T021/T023.

### User Story 2

- Run T024, T025, and T026 in parallel (unit + distinct integration files).
- Run T027 and T028 in parallel (separate builder files).

### User Story 3

- Run T032, T033, T034, T035, and T036 in parallel (separate test files/layers).
- Run T041 and T043 in parallel (repository vs hosted service) before wiring in T044.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Deliver Phase 3 (US1) fully.
3. Validate US1 independent test criteria.
4. Optionally release backend-only MVP for log persistence/retrieval.

### Incremental Delivery

1. Ship US1 for durable execution outcome visibility.
2. Ship US2 for hierarchy-aware diagnostics and navigation context.
3. Ship US3 for detailed user-facing outcomes, masking, conciseness bounds, and retention controls.
4. Complete polish/verification including lint/format/static analysis, coverage, security scan, and performance evidence.

### Parallel Team Strategy

1. One engineer owns domain/repository and retention worker tasks.
2. One engineer owns endpoint/contracts and API mapping tasks.
3. One engineer owns test suites (unit/integration/contract) in parallel with implementation.

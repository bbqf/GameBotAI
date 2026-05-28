# Tasks: Wait for Image Primitive Action

**Input**: Design documents from `/specs/001-wait-for-image/`
**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/`

**Tests**: Tests are REQUIRED for executable logic per the Constitution.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Gate Clearance

**Purpose**: Clear the current verification blocker before any implementation, setup, or test-authoring work begins.

- [x] T001 Resolve the local build/test blocker and update the gate note in specs/001-wait-for-image/plan.md

**Checkpoint**: The red build/test gate is cleared or explicitly waived; all remaining tasks may proceed.

---

## Phase 2: Setup (Shared Infrastructure)

**Purpose**: Prepare fixtures and contract examples after the red build/test gate is cleared.

- [x] T002 [P] Add a wait-for-image sample command fixture in tests/TestAssets/sample-wait-for-image-command.json
- [x] T003 [P] Add a sample wait-for-image sequence fixture in tests/TestAssets/sample-wait-for-image-sequence.json
- [x] T004 [P] Add sample wait-for-image command and sequence definitions in data/commands/sample-wait-for-image-command.json and data/commands/sequences/sample-wait-for-image-sequence.json
- [x] T005 [P] Align wait-for-image command, sequence, and execution-log examples in specs/001-wait-for-image/contracts/wait-for-image.openapi.yaml

---

## Phase 3: Foundational (Blocking Prerequisites)

**Purpose**: Add the core step type, DTOs, and shared plumbing required by all user stories.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T006 Create the wait-step config model in src/GameBot.Domain/Commands/WaitForImageConfig.cs
- [x] T007 Add the `WaitForImage` command step type and payload property in src/GameBot.Domain/Commands/CommandStep.cs
- [x] T008 [P] Add sequence-side wait-step payload support in src/GameBot.Domain/Commands/SequenceStep.cs and src/GameBot.Service/Models/SequenceStepContracts.cs
- [x] T009 Add wait-step DTOs and execution outcome extensions in src/GameBot.Service/Models/Commands.cs
- [x] T010 Add wait-step mapping, normalization, and validation hooks in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [x] T011 [P] Add sequence wait-step validation and mapping hooks in src/GameBot.Domain/Services/SequenceStepValidationService.cs and src/GameBot.Service/Program.cs
- [x] T012 [P] Extend command and sequence client DTOs for `WaitForImage` in src/web-ui/src/services/commands.ts and src/web-ui/src/services/sequences.ts
- [x] T013 [P] Extend execution-log client DTOs for wait-step metadata in src/web-ui/src/services/executionLogsApi.ts
- [x] T014 [P] Add Swagger schema/examples for `WaitForImage` in src/GameBot.Service/Swagger/SwaggerConfig.cs

**Checkpoint**: Foundation ready; user story work can start independently.

---

## Phase 4: User Story 1 - Author a Wait Step (Priority: P1) 🎯 MVP Slice

**Goal**: Let authors create, edit, save, and reload `WaitForImage` steps with optional image, optional certainty, and timeout values.

**Independent Test**: Create a command containing a `WaitForImage` step, save it, reload it, and confirm the authored values remain visible and unchanged in the web UI and API response.

### Tests for User Story 1

- [x] T015 [P] [US1] Add contract tests for the `WaitForImage` command and sequence schema in tests/contract/OpenApiContractTests.cs
- [x] T016 [P] [US1] Add integration tests for command wait-step create/get/update round-tripping in tests/integration/Commands/WaitForImageAuthoringIntegrationTests.cs
- [x] T017 [P] [US1] Add integration tests for sequence wait-step create/get/update round-tripping in tests/integration/Sequences/WaitForImageSequenceAuthoringIntegrationTests.cs
- [x] T018 [P] [US1] Add web UI service tests for wait-step payload mapping in src/web-ui/src/services/__tests__/commands.spec.ts and src/web-ui/src/services/__tests__/sequences.spec.ts
- [x] T019 [P] [US1] Add web UI authoring tests for wait-step create/edit flows in src/web-ui/src/pages/__tests__/CommandsPage.wait-for-image.spec.tsx and src/web-ui/src/pages/__tests__/SequencesPage.wait-for-image.spec.tsx
- [x] T056 [P] [US1] Add command validation tests proving 0 ms timeout is accepted and negative timeout values are rejected in tests/integration/Commands/WaitForImageAuthoringIntegrationTests.cs
- [x] T057 [P] [US1] Add sequence validation tests proving 0 ms timeout is accepted and negative timeout values are rejected in tests/integration/Sequences/WaitForImageSequenceAuthoringIntegrationTests.cs

### Implementation for User Story 1

- [x] T020 [US1] Implement command wait-step serialization, default timeout normalization, and save validation in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [x] T021 [US1] Implement sequence wait-step serialization and save validation in src/GameBot.Service/Models/SequenceStepContracts.cs and src/GameBot.Service/Program.cs
- [x] T022 [US1] Add `WaitForImage` command-step controls and authored summaries in src/web-ui/src/components/commands/CommandForm.tsx and src/web-ui/src/components/commands/CommandForm.css
- [x] T023 [US1] Wire command wait-step form mapping, client validation, and round-trip persistence in src/web-ui/src/pages/CommandsPage.tsx
- [x] T024 [US1] Add sequence wait-step authoring controls and round-trip persistence in src/web-ui/src/pages/SequencesPage.tsx

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 5: User Story 2 - Wait Without Failing Execution (Priority: P1)

**Goal**: Execute `WaitForImage` steps so image-detected, timeout, no-image, and image-unavailable paths all complete without failing command execution.

**Independent Test**: Execute commands and sequences that cover image detected before timeout, no image configured, and image unavailable; verify execution continues according to the spec.

### Tests for User Story 2

- [x] T025 [P] [US2] Add unit tests for command wait-step detection, timeout, and unavailable outcomes in tests/unit/Commands/CommandExecutorWaitForImageTests.cs
- [x] T026 [P] [US2] Add unit tests for sequence wait-step detection, timeout, and unavailable outcomes in tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs
- [x] T027 [P] [US2] Add integration tests for command wait-step execution flows in tests/integration/Commands/WaitForImageExecutionIntegrationTests.cs
- [x] T028 [P] [US2] Add integration tests for sequence wait-step execution flows in tests/integration/Sequences/WaitForImageSequenceExecutionIntegrationTests.cs
- [x] T029 [P] [US2] Add contract tests for wait-step execute response outcomes in tests/contract/OpenApiContractTests.cs
- [x] T030 [P] [US2] Add web UI service tests for wait-step execution outcome parsing in src/web-ui/src/services/__tests__/commands.spec.ts and src/web-ui/src/services/__tests__/sequences.spec.ts

### Implementation for User Story 2

- [x] T031 [US2] Implement command wait-step polling and detection success in src/GameBot.Service/Services/CommandExecutor.cs
- [x] T032 [US2] Implement command no-image and image-unavailable completion semantics in src/GameBot.Service/Services/CommandExecutor.cs
- [x] T033 [US2] Implement sequence wait-step polling and completion semantics in src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T034 [US2] Return wait-step type and completion outcomes from command execute endpoints in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [x] T035 [US2] Return or surface wait-step completion outcomes from sequence execution flow in src/GameBot.Service/Program.cs and related sequence response mapping

**Checkpoint**: User Story 2 is fully functional and independently testable.

---

## Phase 6: User Story 3 - Inspect Wait Outcomes in Logs (Priority: P2)

**Goal**: Show authored wait parameters and the final wait exit condition in persisted execution logs and the execution-log UI.

**Independent Test**: Execute wait steps for image-detected, timeout, and image-unavailable cases, then open execution-log detail and confirm it shows the wait parameters plus the correct terminal exit condition.

### Tests for User Story 3

- [x] T036 [P] [US3] Add integration tests for persisted command wait-step execution-log details in tests/integration/ExecutionLogs/CommandExecutionLoggingIntegrationTests.cs
- [x] T037 [P] [US3] Add integration tests for persisted sequence wait-step execution-log details in tests/integration/ExecutionLogs/SequenceExecutionLoggingIntegrationTests.cs
- [x] T038 [P] [US3] Add contract tests for wait-step execution-log detail schema in tests/contract/OpenApiContractTests.cs
- [x] T039 [P] [US3] Add execution-log API parsing tests for wait-step detail attributes in src/web-ui/src/services/__tests__/executionLogsApi.spec.ts
- [x] T040 [P] [US3] Add UI tests for rendering wait-step parameters and exit conditions in src/web-ui/src/pages/__tests__/ExecutionLogs.test.tsx

### Implementation for User Story 3

- [x] T041 [US3] Extend execution-log domain models for wait-step type and structured detail attributes in src/GameBot.Domain/Logging/ExecutionLogModels.cs
- [x] T042 [US3] Capture command and sequence wait-step parameters and exit conditions during execution in src/GameBot.Service/Services/CommandExecutor.cs and src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T043 [US3] Map wait-step details into persisted log entries in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [x] T044 [US3] Extend execution-log detail DTOs for wait-step metadata in src/GameBot.Service/Models/ExecutionLogs.cs
- [x] T045 [US3] Expose wait-step log detail fields from log endpoints in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs
- [x] T046 [US3] Render wait-step parameters and exit conditions in src/web-ui/src/pages/ExecutionLogs.tsx

**Checkpoint**: User Story 3 is fully functional and independently testable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Complete final verification, documentation, release notes, and quality evidence.

- [x] T047 [P] Update final verification steps and examples in specs/001-wait-for-image/quickstart.md
- [x] T048 [P] Add a user-visible release note for `WaitForImage` in CHANGELOG.md
- [x] T049 Run backend build/test verification and failure analysis with scripts/analyze-test-results.ps1
- [x] T050 Run web UI test verification for command, sequence, and execution-log changes via src/web-ui/package.json
- [ ] T051 Run .NET format and analyzer verification for touched backend code via GameBot.sln
- [x] T052 Run repository security or secret scanning and record results in docs/validation.md
- [x] T053 [P] Record authoring/runtime/logging validation evidence in docs/validation.md
- [x] T054 [P] Record wait polling and execution-log latency evidence in docs/perf-checklist.md
- [x] T055 [P] Collect touched-area coverage evidence for wait-for-image changes in tools/coverage/
- [x] T058 [P] Add public API documentation comments for WaitForImageConfig and the new public DTOs/contracts touched by this feature

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Gate Clearance)**: Starts immediately and is a hard prerequisite for every other task in this document.
- **Phase 2 (Setup)**: Depends on Phase 1.
- **Phase 3 (Foundational)**: Depends on Phase 2 and blocks all user story work.
- **Phase 4 (US1)**: Depends on Phase 3.
- **Phase 5 (US2)**: Depends on Phase 3 and can proceed after the foundational command and sequence type or payload work is complete.
- **Phase 6 (US3)**: Depends on Phase 3 and on wait-step runtime outcomes from Phase 5 being available for logging.
- **Phase 7 (Polish)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Starts after foundational work; no dependency on other user stories.
- **US2 (P1)**: Starts after foundational work; independent of US1 in runtime terms, but shares the same foundational DTO, sequence contract, and endpoint plumbing.
- **US3 (P2)**: Starts after foundational work and depends on US2 runtime outcome generation so wait-step log details can be populated.

### Within Each User Story

- Write tests first and confirm they fail before implementing.
- Complete backend/domain contract changes before UI mapping that depends on them.
- Re-run story-specific tests before moving to the next story.

### Suggested Completion Order

1. Phase 1 → Phase 2 → Phase 3
2. Phase 4 (US1) to unlock authored wait steps
3. Phase 5 (US2) to make wait execution behavior real
4. Phase 6 (US3) to surface the behavior in logs
5. Phase 7 for full verification and documentation

---

## Parallel Execution Examples

### User Story 1

- T015, T016, T017, T018, T019, T056, and T057 can run in parallel after T001-T014 because they touch different test layers and files.
- T022 and T024 can run in parallel after T020-T021 because they touch separate command and sequence authoring files.

### User Story 2

- T025, T026, T027, T028, T029, and T030 can run in parallel after T001-T014 because they cover separate unit, integration, contract, and web service layers.

### User Story 3

- T036, T037, T038, T039, and T040 can run in parallel after T001-T014 and the Phase 5 runtime work because they touch separate integration, contract, service-test, and UI-test files.
- T044 and T046 can run in parallel after T041-T043 because they touch separate backend DTO and frontend rendering files.

---

## Implementation Strategy

### MVP First

1. Complete Phase 1, Phase 2, and Phase 3.
2. Deliver Phase 4 so wait steps can be authored and persisted in both commands and sequences.
3. Deliver Phase 5 so the authored step has working execution semantics in both commands and sequences.
4. Validate the two P1 user stories together as the first usable MVP.

### Incremental Delivery

1. Ship US1 + US2 for end-to-end authoring and non-failing wait execution.
2. Add US3 for diagnostics and execution-log visibility.
3. Finish with Phase 7 verification, documentation, and evidence capture.

### Parallel Team Strategy

1. One engineer clears T001 first, then owns domain/service/endpoint tasks for Phases 3 and 5.
2. One engineer owns web UI authoring and log rendering tasks for Phases 4 and 6 after T001-T014 complete.
3. One engineer owns contract/integration/unit verification across all phases after T001 clears the gate.

---

## Notes

- `[P]` tasks touch different files and are safe to parallelize.
- `[US1]`, `[US2]`, and `[US3]` labels preserve story traceability.
- T001 is a hard prerequisite for every other task in this document because the current local build/test gate is blocked by a running service process.
- Security or secret scan completion is required before the feature can be considered done.
- T056 and T057 provide explicit coverage for the 0 ms and negative-timeout validation rules.
- T058 satisfies the constitution requirement to document newly introduced public APIs.
- Stop at each checkpoint and validate the story independently before widening scope.
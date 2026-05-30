# Tasks: Conditional Sequence Steps (Minimal)

**Input**: Design documents from `/specs/031-sequence-conditional-steps/`  
**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Included. This feature changes runtime execution behavior and API contracts.  
**Organization**: Tasks are grouped by user story for independent implementation and validation.

## Format: `[ID] [P?] [Story] Description with file path`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare fixtures, contract baseline, and shared scaffolding.

- [X] T001 Create mixed-step sample payload in samples/sample-sequence-conditional-steps.json
- [X] T002 [P] Create empty-repository test fixture in tests/TestAssets/sequences/empty-repository.json
- [X] T003 [P] Create unsupported-action fixture in tests/TestAssets/sequences/unsupported-action-payload.json
- [X] T004 Add OpenAPI snapshot baseline test file in tests/contract/Sequences/SequenceConditionalStepsOpenApiTests.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build core schema, validation, and runtime wiring used by all user stories.

**⚠️ CRITICAL**: No user story work starts before this phase completes.

- [X] T005 Implement discriminated step DTOs (`action`/`conditional`) in src/GameBot.Service/Models/SequenceStepContracts.cs
- [X] T006 Implement domain step union model in src/GameBot.Domain/Commands/SequenceStep.cs
- [X] T007 Implement `imageVisible` condition model in src/GameBot.Domain/Commands/ImageVisibleCondition.cs
- [X] T008 Implement schema validation service for required/unknown fields in src/GameBot.Domain/Services/SequenceStepValidationService.cs
- [X] T009 Implement action payload validator integration and supported-action-type source resolver using existing action execution infrastructure contract in src/GameBot.Domain/Services/ActionPayloadValidationService.cs
- [X] T010 Implement empty-state first-save repository behavior in src/GameBot.Domain/Repositories/SequenceRepository.cs
- [X] T011 Implement sequence endpoint schema enforcement (`stepType` required) in src/GameBot.Service/Endpoints/SequencesEndpoints.cs
- [X] T012 Register conditional-step services and validators in src/GameBot.Service/Program.cs

**Checkpoint**: Core schema + runtime infrastructure is ready for story work.

---

## Phase 3: User Story 1 - Execute action only when image is visible (Priority: P1) 🎯 MVP

**Goal**: Conditional steps execute on true, skip on false, and fail-stop on evaluation errors.

**Independent Test**: One sequence with one conditional and one unconditional step behaves correctly under true/false/error evaluation outcomes.

### Tests for User Story 1

- [X] T013 [P] [US1] Add unit tests for conditional true/false/error outcomes in tests/unit/Sequences/ConditionalStepEvaluationTests.cs
- [X] T014 [P] [US1] Add unit tests for default vs explicit `minSimilarity` behavior in tests/unit/Sequences/ImageVisibleThresholdTests.cs
- [X] T015 [P] [US1] Add integration tests for execute/skip/fail-stop flow in tests/integration/Sequences/ConditionalStepExecutionIntegrationTests.cs
- [X] T016 [P] [US1] Add contract tests for conditional step request/response schema in tests/contract/Sequences/SequenceConditionalStepsContractTests.cs

### Implementation for User Story 1

- [X] T017 [US1] Implement conditional evaluation in runtime sequence runner in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T018 [US1] Implement image detection adapter for `imageVisible` in src/GameBot.Service/Services/Conditions/ImageVisibleConditionAdapter.cs
- [X] T019 [US1] Enforce ordered execution for mixed `action` and `conditional` steps in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T020 [US1] Implement save-time image reference validation in src/GameBot.Service/Endpoints/SequencesEndpoints.cs
- [X] T021 [US1] Extend step execution logging fields in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs

**Checkpoint**: US1 fully functional and independently testable.

---

## Phase 4: User Story 2 - Mix conditional and unconditional steps in one sequence (Priority: P1)

**Goal**: Author and execute A/B conditional taps plus final unconditional action in one sequence.

**Independent Test**: Validate all 4 A/B visibility permutations produce expected outcomes.

### Tests for User Story 2

- [X] T022 [P] [US2] Add integration tests for A/B permutation outcomes in tests/integration/Sequences/ConditionalPermutationIntegrationTests.cs
- [X] T023 [P] [US2] Add UI tests for mixed step authoring flow in src/web-ui/src/pages/__tests__/SequencesPage.conditionalSteps.spec.tsx
- [X] T024 [P] [US2] Add UI validation tests for conditional fields in src/web-ui/src/lib/__tests__/sequenceConditionalValidation.spec.ts

### Implementation for User Story 2

- [X] T025 [US2] Implement sequence API client mappings for conditional steps in src/web-ui/src/services/sequences.ts
- [X] T026 [US2] Implement minimal conditional-step editor controls in src/web-ui/src/pages/SequencesPage.tsx
- [X] T027 [US2] Implement client-side validation for `imageId`, condition type, and action payload in src/web-ui/src/lib/validation.ts
- [X] T028 [US2] Implement mixed-step serialization/deserialization mappings in src/web-ui/src/lib/sequenceMapping.ts

**Checkpoint**: US2 fully functional and independently testable.

---

## Phase 5: User Story 3 - Author from empty state (Priority: P2)

**Goal**: First sequence creation/execution works from empty repository state.

**Independent Test**: Start empty, create one mixed sequence, save, execute, and verify deterministic outcomes.

### Tests for User Story 3

- [X] T029 [P] [US3] Add integration test for first-sequence create/save in tests/integration/Sequences/EmptyStateCreateSequenceIntegrationTests.cs
- [X] T030 [P] [US3] Add integration test for first-sequence execute flow in tests/integration/Sequences/EmptyStateExecuteSequenceIntegrationTests.cs
- [X] T031 [P] [US3] Add integration test for deterministic repeated outcomes using ordered (`conditionResult`, `actionOutcome`) tuple comparison in tests/integration/Sequences/DeterministicSequenceOutcomeIntegrationTests.cs
- [X] T032 [P] [US3] Add contract tests for unsupported/malformed action payload rejection in tests/contract/Sequences/SequenceActionPayloadValidationContractTests.cs

### Implementation for User Story 3

- [X] T033 [US3] Implement create-first-sequence endpoint flow for empty repository in src/GameBot.Service/Endpoints/SequencesEndpoints.cs
- [X] T034 [US3] Enforce supported action payload validation on persistence path in src/GameBot.Domain/Repositories/SequenceRepository.cs
- [X] T035 [US3] Add empty-state authoring UX handling/message in src/web-ui/src/pages/SequencesPage.tsx
- [X] T036 [US3] Document clean-slate setup and first sequence workflow in docs/validation.md

**Checkpoint**: US3 fully functional and independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Complete performance and quality gates required by constitution.

- [X] T037 [P] Add performance integration test for NFR-003 profile in tests/integration/Sequences/ConditionalStepPerformanceIntegrationTests.cs
- [X] T038 [P] Update OpenAPI assertions for conditional-step contracts in tests/contract/Sequences/SequenceConditionalStepsOpenApiTests.cs
- [X] T039 Add quality gate step for coverage thresholds (>=80% line, >=70% branch on touched areas) in scripts/analyze-test-results.ps1
- [X] T040 Add security gate verification step (SAST + secret scan evidence) in scripts/analyze-test-results.ps1
- [X] T041 Add explicit lint/format verification step in scripts/analyze-test-results.ps1
- [X] T042 Add explicit static-analysis verification step (no new high/critical issues) in scripts/analyze-test-results.ps1
- [X] T043 Update quickstart verification evidence in specs/031-sequence-conditional-steps/quickstart.md
- [X] T044 Run full verify pass and record outcomes in specs/031-sequence-conditional-steps/plan.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all stories.
- **Phase 3 (US1)**: Depends on Phase 2.
- **Phase 4 (US2)**: Depends on Phase 2 and US1 runtime/API shape.
- **Phase 5 (US3)**: Depends on Phase 2 and validates empty-state + determinism.
- **Phase 6 (Polish)**: Depends on all selected user stories.

### User Story Dependencies

- **US1 (P1)**: First MVP slice; independent after foundational phase.
- **US2 (P1)**: Depends on US1 schema/runtime shape.
- **US3 (P2)**: Depends on foundational schema/persistence and validates clean-slate startup behavior.

### Within Each User Story

- Tests first (must fail before implementation).
- Domain/services before endpoints and UI wiring.
- Story verification before proceeding.

---

## Parallel Execution Examples

### User Story 1

```bash
# Parallel tests:
T013, T014, T015, T016

# After runtime core is in progress:
T018, T021
```

### User Story 2

```bash
# Parallel tests:
T022, T023, T024

# Parallel implementation (different files):
T025, T027, T028
```

### User Story 3

```bash
# Parallel tests:
T029, T030, T031, T032

# Parallel implementation/docs:
T034, T035, T036
```

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate US1 independently.
4. Demo MVP behavior.

### Incremental Delivery

1. Add US2 for full mixed-step authoring/execution use case.
2. Add US3 for empty-state first-run and determinism guarantees.
3. Finish with Phase 6 quality/performance gates.

### Parallel Team Strategy

1. Team completes Setup + Foundational.
2. After foundation:
   - Developer A: US1 runtime/API
   - Developer B: US2 UI authoring
   - Developer C: US3 empty-state + determinism
3. Integrate by phase checkpoints with contract/integration gates.

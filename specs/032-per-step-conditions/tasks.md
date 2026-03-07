# Tasks: Per-Step Optional Conditions

**Input**: Design documents from `/specs/032-per-step-conditions/`  
**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Included. Runtime behavior, API contracts, and UI authoring behavior are all changed by this feature.  
**Organization**: Tasks are grouped by user story for independent implementation and validation.

## Format: `[ID] [P?] [Story] Description with file path`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add fixtures and baseline scaffolding for per-step condition model tests.

- [X] T001 Create per-step sample payload fixture in samples/sample-sequence-per-step-conditions.json
- [X] T002 [P] Create invalid forward-reference fixture for commandOutcome in tests/TestAssets/sequences/command-outcome-forward-ref-invalid.json
- [X] T003 [P] Create invalid expectedState fixture for commandOutcome in tests/TestAssets/sequences/command-outcome-invalid-state.json
- [X] T004 Add OpenAPI baseline test scaffold in tests/contract/Sequences/SequencePerStepConditionsOpenApiTests.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement shared per-step condition schema and validation foundations used by all user stories.

**⚠️ CRITICAL**: No user story implementation starts before this phase completes.

- [X] T005 Implement per-step condition domain union (`imageVisible`, `commandOutcome`) in src/GameBot.Domain/Commands/SequenceStepCondition.cs
- [X] T006 Implement per-step condition fields on sequence step model in src/GameBot.Domain/Commands/SequenceStep.cs
- [X] T007 Implement commandOutcome validation rules (prior-step reference and expectedState enum) in src/GameBot.Domain/Services/SequenceStepValidationService.cs
- [X] T008 Implement API request/response contracts for per-step optional condition in src/GameBot.Service/Models/SequenceStepContracts.cs
- [X] T009 Implement endpoint payload mapping and validation wiring for per-step conditions in src/GameBot.Service/Endpoints/SequencesEndpoints.cs
- [X] T010 Implement repository persistence shape for per-step conditions in src/GameBot.Domain/Commands/FileSequenceRepository.cs
- [X] T011 Remove entry-step/links contract dependency from sequence service pipeline in src/GameBot.Service/Program.cs

**Checkpoint**: Foundational schema and validation pipeline ready for independent story implementation.

---

## Phase 3: User Story 1 - Add Optional Condition Per Step (Priority: P1) 🎯 MVP

**Goal**: Authors can configure mixed conditional/unconditional linear steps and persist/reload them accurately.

**Independent Test**: Create a mixed sequence, save, reload, edit one step condition, and verify unchanged conditions on other steps.

### Tests for User Story 1

- [X] T012 [P] [US1] Add contract tests for sequence create/update/get with optional per-step conditions in tests/contract/Sequences/SequencePerStepConditionsContractTests.cs
- [X] T013 [P] [US1] Add integration test for mixed-step create/reload round trip in tests/integration/Sequences/PerStepConditionAuthoringRoundTripIntegrationTests.cs
- [X] T014 [P] [US1] Add integration test for first-sequence empty-state save with per-step conditions in tests/integration/Sequences/PerStepConditionEmptyStateCreateIntegrationTests.cs
- [X] T015 [P] [US1] Add web UI authoring test for per-step condition controls and persistence mapping in src/web-ui/src/pages/__tests__/SequencesPage.perStepConditions.spec.tsx

### Implementation for User Story 1

- [X] T016 [US1] Implement per-step condition DTO-to-domain mapping in src/GameBot.Service/Endpoints/SequencesEndpoints.cs
- [X] T017 [US1] Implement save-time condition shape validation messages per step in src/GameBot.Domain/Services/SequenceStepValidationService.cs
- [X] T018 [US1] Implement sequence API client types for optional per-step condition payload in src/web-ui/src/services/sequences.ts
- [X] T019 [US1] Implement per-step condition type definitions in src/web-ui/src/types/sequenceFlow.ts
- [X] T020 [US1] Implement per-step sequence mapping serialize/deserialize logic in src/web-ui/src/lib/sequenceMapping.ts
- [X] T021 [US1] Implement authoring controls for optional step conditions in src/web-ui/src/pages/SequencesPage.tsx
- [X] T022 [US1] Implement client-side per-step condition validation in src/web-ui/src/lib/validation.ts

**Checkpoint**: US1 is fully functional and independently testable in create/edit/save/reload flows.

---

## Phase 4: User Story 2 - Execute Linear Step Conditions (Priority: P1)

**Goal**: Runtime evaluates each step condition immediately before the step and applies execute/skip/fail-stop semantics.

**Independent Test**: Execute one mixed sequence across visibility and commandOutcome permutations and verify ordered step outcomes.

### Tests for User Story 2

- [X] T023 [P] [US2] Add unit tests for per-step condition true/false/error execution outcomes in tests/unit/Sequences/PerStepConditionRunnerTests.cs
- [X] T024 [P] [US2] Add integration tests for map/bag visibility permutations in tests/integration/Sequences/PerStepConditionExecutionPermutationIntegrationTests.cs
- [X] T025 [P] [US2] Add integration tests for commandOutcome prior-step reference behavior in tests/integration/Sequences/PerStepCommandOutcomeExecutionIntegrationTests.cs

### Implementation for User Story 2

- [X] T026 [US2] Implement per-step condition evaluation pipeline in sequence runner in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T027 [US2] Implement commandOutcome state tracking for prior-step references in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T028 [US2] Implement imageVisible and commandOutcome evaluator routing in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T029 [US2] Implement fail-stop behavior for condition evaluation errors in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T030 [US2] Extend step execution outcome fields for conditionType/conditionResult/actionOutcome in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [X] T031 [US2] Implement execution response mapping for per-step outcomes in src/GameBot.Service/Endpoints/SequencesEndpoints.cs

**Checkpoint**: US2 runtime semantics are deterministic and independently testable.

---

## Phase 5: User Story 3 - Remove Entry-Step Branching Model (Priority: P2)

**Goal**: Authors no longer interact with entry-step or branch-link controls; only step-level condition controls are exposed.

**Independent Test**: Verify sequence UI contains no entry-step/true-target/false-target controls and still supports full per-step condition authoring.

### Tests for User Story 3

- [ ] T032 [P] [US3] Add web UI test ensuring entry-step and branch-link controls are absent in src/web-ui/src/pages/__tests__/SequencesPage.noBranching.spec.tsx
- [ ] T033 [P] [US3] Add contract test asserting sequence payload excludes entry-step and links in tests/contract/Sequences/SequencePerStepNoBranchingContractTests.cs
- [ ] T034 [P] [US3] Add integration test for loading/saving linear per-step schema without graph fields in tests/integration/Sequences/PerStepNoBranchingSchemaIntegrationTests.cs

### Implementation for User Story 3

- [ ] T035 [US3] Remove entry-step and branch-link controls from sequence authoring UI in src/web-ui/src/pages/SequencesPage.tsx
- [ ] T036 [US3] Remove obsolete branch graph builders from sequence mapping utilities in src/web-ui/src/lib/sequenceFlowGraph.ts
- [ ] T037 [US3] Remove entry-step/links endpoint contract handling in src/GameBot.Service/Models/SequenceStepContracts.cs
- [ ] T038 [US3] Update sequence API contract model and OpenAPI annotations for no-branch payload in src/GameBot.Service/Endpoints/SequencesEndpoints.cs

**Checkpoint**: US3 UX simplification is complete and independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final quality gates, documentation, and verification evidence.

- [ ] T039 [P] Add OpenAPI assertions for per-step condition schema and commandOutcome constraints in tests/contract/Sequences/SequencePerStepConditionsOpenApiTests.cs
- [ ] T040 [P] Add performance non-regression integration test for mixed per-step conditions in tests/integration/Sequences/PerStepConditionPerformanceIntegrationTests.cs
- [ ] T041 Add explicit coverage gate verification (>=80% line, >=70% branch on touched areas) in scripts/analyze-test-results.ps1
- [ ] T042 Add explicit security scan gate verification (SAST + secret scan evidence) in scripts/analyze-test-results.ps1
- [ ] T043 Add explicit lint/format and static-analysis gate verification in scripts/analyze-test-results.ps1
- [ ] T044 Update quickstart verification evidence for final behavior in specs/032-per-step-conditions/quickstart.md
- [ ] T045 Run full verification pass and record outcomes in specs/032-per-step-conditions/plan.md
- [ ] T046 Add final validation summary for per-step condition feature in docs/validation.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1; blocks all user story work.
- **Phase 3 (US1)**: Depends on Phase 2.
- **Phase 4 (US2)**: Depends on Phase 2 and US1 schema shape.
- **Phase 5 (US3)**: Depends on Phase 2 and aligns with US1 UI authoring model.
- **Phase 6 (Polish)**: Depends on completed user stories.

### User Story Dependencies

- **US1 (P1)**: MVP and first deliverable after foundation.
- **US2 (P1)**: Depends on US1 payload/schema to execute authored conditions.
- **US3 (P2)**: Depends on US1 authoring direction; removes obsolete branching UI/contracts.

### Within Each User Story

- Tests first (must fail before implementation).
- Models/contracts before endpoint/runtime/UI wiring.
- Story verification before moving to next phase.

---

## Parallel Execution Examples

### User Story 1

```bash
# Parallel tests
T012, T013, T014, T015

# Parallel implementation after mapping foundations exist
T018, T019, T022
```

### User Story 2

```bash
# Parallel tests
T023, T024, T025

# Parallel implementation components
T030, T031
```

### User Story 3

```bash
# Parallel tests
T032, T033, T034

# Parallel implementation
T036, T037
```

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate create/edit/save/reload independently.
4. Demo authoring workflow.

### Incremental Delivery

1. Add US2 runtime semantics after US1 payload stability.
2. Add US3 simplification to fully remove branch-oriented authoring concepts.
3. Complete polish/perf/verification evidence.

### Parallel Team Strategy

1. Team completes Setup + Foundational together.
2. After foundation:
   - Developer A: backend runtime (US2)
   - Developer B: web authoring UX (US1/US3)
   - Developer C: contract/integration/perf validation (US1/US2/Polish)
3. Integrate at each story checkpoint with full verification.

# Tasks: Visual Conditional Sequence Logic

**Input**: Design documents from `/specs/030-sequence-conditional-logic/`
**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Tests are included because this feature specification and plan define explicit independent test criteria and validation scenarios.
**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description with file path`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare baseline contracts, fixtures, and shared type scaffolding.

- [x] T001 Add conditional-flow OpenAPI contract snapshot test fixture in tests/contract/Sequences/SequenceConditionalFlowOpenApiTests.cs
- [x] T002 [P] Add backend conditional-flow DTO scaffolding in src/GameBot.Service/Contracts/Sequences/ConditionalFlowDtos.cs
- [x] T003 [P] Add frontend conditional-flow type scaffolding in src/web-ui/src/types/sequenceFlow.ts
- [x] T004 Add conditional-sequence sample payload in samples/sample-sequence-conditional-flow.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build core graph, validation, and evaluator foundations that all user stories depend on.

**⚠️ CRITICAL**: No user story implementation starts before this phase is complete.

- [x] T005 Implement sequence-flow graph domain model in src/GameBot.Domain/Commands/SequenceFlowGraph.cs
- [x] T006 Implement condition expression/operand domain model in src/GameBot.Domain/Commands/ConditionExpression.cs
- [x] T007 Implement branch-link domain model and invariants in src/GameBot.Domain/Commands/BranchLink.cs
- [x] T008 Implement flow graph validator (branch-target integrity and cycle detection) in src/GameBot.Domain/Services/SequenceFlowValidator.cs
- [x] T009 Implement cycle-limit validation/enforcement helpers in src/GameBot.Domain/Services/CycleIterationLimiter.cs
- [x] T010 Implement condition evaluator interface and baseline implementation in src/GameBot.Domain/Services/ConditionEvaluator.cs
- [x] T011 [P] Register conditional-flow services in src/GameBot.Service/Program.cs
- [x] T012 [P] Register conditional-flow schemas in specs/openapi.json generation path in src/GameBot.Service/Program.cs

**Checkpoint**: Graph modeling, validation, and evaluation primitives are ready.

---

## Phase 3: User Story 1 - Branch sequence paths by condition (Priority: P1) 🎯 MVP

**Goal**: Execute true/false branch routing from command-outcome and image-detection conditions with deterministic evaluation and fail-stop behavior.

**Independent Test**: Create a sequence with one conditional step and two branches, run once with true conditions and once with false/failed conditions, and verify expected branch routing and fail-stop outcomes.

### Tests for User Story 1

- [x] T013 [P] [US1] Add unit tests for nested AND/OR/NOT evaluation order in tests/unit/Sequences/ConditionExpressionEvaluatorTests.cs
- [x] T014 [P] [US1] Add unit tests for command-outcome operands in tests/unit/Sequences/CommandOutcomeConditionTests.cs
- [x] T015 [P] [US1] Add unit tests for image-detection threshold operands in tests/unit/Sequences/ImageDetectionConditionTests.cs
- [x] T016 [P] [US1] Add integration tests for branch routing and unevaluable-condition fail-stop in tests/integration/Sequences/ConditionalExecutionIntegrationTests.cs
- [x] T017 [P] [US1] Add contract tests for sequence create/update/validate/execute conditional payloads in tests/contract/Sequences/SequenceConditionalContractsTests.cs

### Implementation for User Story 1

- [x] T018 [US1] Extend sequence aggregate for conditional flow persistence in src/GameBot.Domain/Commands/CommandSequence.cs
- [x] T019 [US1] Implement recursive condition evaluation in src/GameBot.Domain/Services/ConditionEvaluator.cs
- [x] T020 [US1] Implement command-outcome condition adapter in src/GameBot.Service/Services/Conditions/CommandOutcomeConditionAdapter.cs
- [x] T021 [P] [US1] Implement image-detection condition adapter in src/GameBot.Service/Services/Conditions/ImageDetectionConditionAdapter.cs
- [x] T022 [US1] Integrate condition result routing into sequence runner in src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T023 [US1] Enforce unevaluable-condition failure reason + immediate stop in src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T024 [US1] Enforce cycle iteration-limit stop and per-run counter reset in src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T025 [US1] Implement sequence flow validate endpoint behavior in src/GameBot.Service/Program.cs
- [x] T026 [US1] Implement stale-version save conflict response (`409` + sequenceId/currentVersion) in src/GameBot.Service/Program.cs
- [x] T027 [US1] Update sequence API client methods for conditional payloads and conflict handling in src/web-ui/src/services/sequences.ts

**Checkpoint**: US1 is independently functional and testable.

---

## Phase 4: User Story 2 - Compose complex logic visually (Priority: P2)

**Goal**: Provide visual flow authoring for conditional graphs and nested logic with clear true/false branch semantics and stable save/reload behavior.

**Independent Test**: Build nested condition logic in the visual editor, save, reload, and verify visual structure and execution semantics remain consistent.

### Tests for User Story 2

- [ ] T028 [P] [US2] Add UI tests for conditional flow authoring interactions in src/web-ui/src/pages/__tests__/SequencesPage.conditionalFlow.spec.tsx
- [ ] T029 [P] [US2] Add UI tests for nested expression builder semantics in src/web-ui/src/components/authoring/__tests__/ConditionExpressionBuilder.spec.tsx
- [ ] T030 [P] [US2] Add frontend validation tests for missing branch targets and invalid references in src/web-ui/src/lib/__tests__/sequenceFlowValidation.spec.ts
- [ ] T031 [P] [US2] Add integration test for visual round-trip save/reload parity in tests/integration/Sequences/ConditionalAuthoringRoundTripIntegrationTests.cs

### Implementation for User Story 2

- [ ] T032 [US2] Implement sequence flow graph state utilities in src/web-ui/src/lib/sequenceFlowGraph.ts
- [ ] T033 [P] [US2] Implement visual condition expression builder component in src/web-ui/src/components/authoring/ConditionExpressionBuilder.tsx
- [ ] T034 [P] [US2] Implement true/false branch connector component in src/web-ui/src/components/authoring/SequenceBranchConnector.tsx
- [ ] T035 [US2] Integrate conditional visual editor into sequence authoring page in src/web-ui/src/pages/SequencesPage.tsx
- [ ] T036 [US2] Implement client-side flow validation messages for unresolved targets in src/web-ui/src/lib/validation.ts
- [ ] T037 [US2] Preserve semantic equivalence on save/reload mapping in src/web-ui/src/services/sequences.ts
- [ ] T038 [US2] Implement optimistic concurrency conflict UX prompt/reload flow in src/web-ui/src/pages/SequencesPage.tsx

**Checkpoint**: US2 is independently functional and testable.

---

## Phase 5: User Story 3 - Trace execution with precise context (Priority: P3)

**Goal**: Emit enriched execution logs with sequence/step context, deep-link metadata, and debug-level condition traces.

**Independent Test**: Execute a conditional sequence and verify log entries include sequence+step IDs/labels, deep-link metadata, and condition trace details.

### Tests for User Story 3

- [ ] T039 [P] [US3] Add unit tests for condition trace envelope projection in tests/unit/ExecutionLogs/ConditionTraceLoggingTests.cs
- [ ] T040 [P] [US3] Add unit tests for deep-link metadata shaping in tests/unit/ExecutionLogs/SequenceStepDeepLinkTests.cs
- [ ] T041 [P] [US3] Add integration tests for enriched step-level execution logs in tests/integration/ExecutionLogs/ConditionalSequenceStepLoggingIntegrationTests.cs
- [ ] T042 [P] [US3] Add contract tests for enriched execution log response schema in tests/contract/ExecutionLogs/ConditionalExecutionLogsContractTests.cs
- [ ] T043 [P] [US3] Add integration test asserting no additional deep-link-specific authorization check is introduced in tests/integration/ExecutionLogs/DeepLinkRoutingAuthorizationIntegrationTests.cs

### Implementation for User Story 3

- [ ] T044 [US3] Add condition evaluation trace model and mapping in src/GameBot.Domain/Logging/ConditionEvaluationTrace.cs
- [ ] T045 [US3] Extend execution log context with sequence/step immutable IDs and readable labels in src/GameBot.Service/Services/ExecutionLog/ExecutionLogContext.cs
- [ ] T046 [US3] Emit debug-level operand/operator/final-result traces in src/GameBot.Domain/Services/SequenceRunner.cs
- [ ] T047 [US3] Extend execution log service to persist enriched step entries in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [ ] T048 [US3] Return enriched execution log payloads from endpoint in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs
- [ ] T049 [US3] Implement deep-link missing-step fallback handling in execution log UI in src/web-ui/src/pages/ExecutionLogs.tsx

**Checkpoint**: US3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final regression, performance, and documentation alignment across all stories.

- [ ] T050 [P] Add regression integration test for legacy non-conditional sequence compatibility in tests/integration/Sequences/LegacySequenceCompatibilityIntegrationTests.cs
- [ ] T051 [P] Add performance validation test for conditional-step p95 latency target using NFR-002 normal-load profile in tests/integration/Sequences/ConditionalExecutionPerformanceIntegrationTests.cs
- [ ] T052 Update OpenAPI/contract regression assertions for sequence conditional flow in tests/contract/OpenApiBackwardCompatTests.cs
- [ ] T053 Add coverage gate verification step (>=80% line, >=70% branch on touched areas) in scripts/analyze-test-results.ps1
- [ ] T054 Add security scan verification step (SAST + secret scan evidence capture) in scripts/analyze-test-results.ps1
- [ ] T055 Update validation and rollout notes for conditional flow in docs/validation.md
- [ ] T056 Execute full verification run and record outcomes in specs/030-sequence-conditional-logic/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 → Phase 2 → User Story phases → Phase 6.
- User story implementation starts only after Phase 2 checkpoint.

### User Story Dependencies

- **US1 (P1)**: Depends only on foundational phase and defines MVP delivery.
- **US2 (P2)**: Depends on foundational phase and US1 API payload shape.
- **US3 (P3)**: Depends on foundational phase and US1 execution/runtime hooks.

### Within-Story Ordering

- Tests before implementation tasks.
- Domain/service work before endpoint/UI wiring.
- Story closes only when independent test criteria pass.

---

## Parallel Execution Examples

### US1

- Run T013, T014, T015, T016, T017 in parallel.
- After evaluator core (T019), run T020 and T021 in parallel.

### US2

- Run T028, T029, T030, T031 in parallel.
- After graph utilities (T032), run T033 and T034 in parallel.

### US3

- Run T039, T040, T041, T042 in parallel.
- After context extension (T044), continue T045 and T046 in parallel.

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1) and validate independent test criteria.
3. Demo/ship MVP behavior for branching.

### Incremental Delivery

1. Deliver US2 visual authoring after US1 contract stabilization.
2. Deliver US3 observability enhancements after US1 runtime hooks.
3. Finish polish/performance and full verification.

### Team Parallelization

1. Backend domain/runtime stream: Phases 2, 3, and backend portions of 5.
2. Frontend authoring/log UX stream: Phases 4 and UI portions of 5.
3. Test/contract stream: cross-story contract + integration coverage and performance checks.

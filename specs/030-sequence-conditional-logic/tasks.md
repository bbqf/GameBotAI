# Tasks: Visual Conditional Sequence Logic

**Input**: Design documents from `C:\src\GameBot\specs\030-sequence-conditional-logic\`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/sequence-conditional-flow.openapi.yaml`, `quickstart.md`

**Tests**: Tests are REQUIRED for executable logic per constitution.
**Organization**: Tasks are grouped by user story for independent implementation and validation.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare shared scaffolding and contract baselines for conditional-flow work.

- [ ] T001 Create sequence-conditional API contract fixture in tests/contract/Sequences/SequenceConditionalFlowOpenApiTests.cs
- [ ] T002 [P] Add sequence conditional DTO shell types in src/GameBot.Service/Contracts/Sequences/ConditionalFlowDtos.cs
- [ ] T003 [P] Add web UI conditional flow type definitions in src/web-ui/src/types/sequenceFlow.ts
- [ ] T004 Add sequence conditional feature sample payload in samples/sample-sequence-conditional-flow.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build shared domain/service foundations that all stories depend on.

- [ ] T005 Implement condition expression domain models in src/GameBot.Domain/Commands/ConditionExpression.cs
- [ ] T006 Implement condition operand domain models in src/GameBot.Domain/Commands/ConditionOperand.cs
- [ ] T007 Implement branch link and flow-node models in src/GameBot.Domain/Commands/SequenceFlowGraph.cs
- [ ] T008 Implement flow graph validator (branch integrity + cycle detection) in src/GameBot.Domain/Services/SequenceFlowValidator.cs
- [ ] T009 Implement condition evaluation trace model in src/GameBot.Domain/Logging/ConditionEvaluationTrace.cs
- [ ] T010 Implement condition evaluator service interface in src/GameBot.Domain/Services/IConditionEvaluator.cs
- [ ] T011 [P] Wire conditional-flow contract schema registration in src/GameBot.Service/Swagger/SwaggerConfig.cs
- [ ] T012 [P] Register validator/evaluator services in src/GameBot.Service/Program.cs

**Checkpoint**: Foundational graph, validation, and evaluator abstractions are in place.

---

## Phase 3: User Story 1 - Branch sequence paths by condition (Priority: P1) 🎯 MVP

**Goal**: Execute true/false branching based on command outcome and image-detection operands with deterministic AND/OR/NOT evaluation.

**Independent Test**: Create one conditional sequence; verify success/failure command outcomes and image detection states route to expected branches, including hard-fail on unevaluable conditions.

### Tests for User Story 1

- [ ] T013 [P] [US1] Add unit tests for boolean expression evaluation with explicit left-to-right nested AND/OR/NOT ordering assertions in tests/unit/Sequences/ConditionExpressionEvaluatorTests.cs
- [ ] T014 [P] [US1] Add unit tests for command-outcome operand evaluation in tests/unit/Sequences/CommandOutcomeConditionTests.cs
- [ ] T015 [P] [US1] Add unit tests for image-detection operand evaluation in tests/unit/Sequences/ImageDetectionConditionTests.cs
- [ ] T016 [P] [US1] Add integration tests for conditional execute routing and cycle-limit fail-stop behavior in tests/integration/Sequences/ConditionalExecutionIntegrationTests.cs
- [ ] T017 [P] [US1] Add contract tests for conditional sequence API payloads in tests/contract/Sequences/SequenceConditionalContractsTests.cs

### Implementation for User Story 1

- [ ] T018 [US1] Extend sequence model to persist flow graph and condition nodes in src/GameBot.Domain/Commands/CommandSequence.cs
- [ ] T019 [US1] Implement recursive AND/OR/NOT evaluation in src/GameBot.Domain/Services/ConditionEvaluator.cs
- [ ] T020 [US1] Implement command-outcome operand adapter in src/GameBot.Service/Services/Conditions/CommandOutcomeConditionAdapter.cs
- [ ] T021 [P] [US1] Implement image-detection operand adapter in src/GameBot.Service/Services/Conditions/ImageDetectionConditionAdapter.cs
- [ ] T022 [US1] Integrate conditional branch routing into sequence execution in src/GameBot.Domain/Services/SequenceRunner.cs
- [ ] T023 [US1] Enforce unevaluable-condition fail-stop behavior in src/GameBot.Domain/Services/SequenceRunner.cs
- [ ] T024 [US1] Add sequence flow validate endpoint in src/GameBot.Service/Program.cs
- [ ] T025 [US1] Update sequence create/update endpoints for conditional graph payloads and enforce stale-version conflict responses (optimistic concurrency) in src/GameBot.Service/Program.cs
- [ ] T026 [US1] Update frontend sequence service contract methods in src/web-ui/src/services/sequences.ts

**Checkpoint**: Conditional execution logic is functional and independently testable.

---

## Phase 4: User Story 2 - Compose complex logic visually (Priority: P2)

**Goal**: Provide visual authoring for conditional flow graph editing with clear true/false branches and nested condition groups.

**Independent Test**: Author a nested conditional flow visually, save/reload, and verify graph structure + semantics remain intact.

### Tests for User Story 2

- [ ] T027 [P] [US2] Add SequencesPage visual-flow authoring tests in src/web-ui/src/pages/__tests__/SequencesPage.conditionalFlow.spec.tsx
- [ ] T028 [P] [US2] Add client-side flow validation tests in src/web-ui/src/lib/__tests__/sequenceFlowValidation.spec.ts
- [ ] T029 [P] [US2] Add integration test for save/reload visual parity in tests/integration/Sequences/ConditionalAuthoringRoundTripIntegrationTests.cs

### Implementation for User Story 2

- [ ] T030 [US2] Implement sequence flow graph state helpers in src/web-ui/src/lib/sequenceFlowGraph.ts
- [ ] T031 [P] [US2] Implement condition builder component (AND/OR/NOT) in src/web-ui/src/components/authoring/ConditionExpressionBuilder.tsx
- [ ] T032 [P] [US2] Implement branch connector component with true/false labels in src/web-ui/src/components/authoring/SequenceBranchConnector.tsx
- [ ] T033 [US2] Add visual flow editor section to sequence authoring page in src/web-ui/src/pages/SequencesPage.tsx
- [ ] T034 [US2] Add frontend validation for unresolved branch targets in src/web-ui/src/lib/validation.ts
- [ ] T035 [US2] Persist and restore visual graph payload mapping with optimistic-version conflict handling in src/web-ui/src/services/sequences.ts

**Checkpoint**: Visual authoring for conditional flow is independently functional.

---

## Phase 5: User Story 3 - Trace execution with precise context (Priority: P3)

**Goal**: Emit step-level logs with sequence/step context, deep links, and debug traces for condition evaluation.

**Independent Test**: Run conditional sequence and verify each log entry identifies sequence+step, contains deep-link IDs/labels, and includes debug trace details for condition evaluation.

### Tests for User Story 3

- [ ] T036 [P] [US3] Add unit tests for execution navigation deep-link payloads in tests/unit/ExecutionLogs/SequenceStepDeepLinkTests.cs
- [ ] T037 [P] [US3] Add unit tests for debug condition trace projection in tests/unit/ExecutionLogs/ConditionTraceLoggingTests.cs
- [ ] T038 [P] [US3] Add integration test for step-context execution logs in tests/integration/ExecutionLogs/ConditionalSequenceStepLoggingIntegrationTests.cs
- [ ] T039 [P] [US3] Add contract test for enriched execution log response in tests/contract/ExecutionLogs/ConditionalExecutionLogsContractTests.cs

### Implementation for User Story 3

- [ ] T040 [US3] Extend execution log context with immutable step identifiers and labels in src/GameBot.Service/Services/ExecutionLog/ExecutionLogContext.cs
- [ ] T041 [US3] Extend execution navigation builder for sequence-step deep links in src/GameBot.Service/Services/ExecutionLog/ExecutionNavigationBuilder.cs
- [ ] T042 [US3] Enrich sequence execution log records with step-level context in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [ ] T043 [US3] Emit debug-level condition trace envelopes in src/GameBot.Domain/Services/SequenceRunner.cs
- [ ] T044 [US3] Return enriched step log payloads in execution logs endpoint in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs
- [ ] T045 [US3] Render deep-link aware sequence-step log details in src/web-ui/src/pages/ExecutionLogs.tsx

**Checkpoint**: Observability requirements are independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening, regression protection, and documentation updates across stories.

- [ ] T046 [P] Update conditional flow OpenAPI snapshot compatibility tests in tests/contract/OpenApiBackwardCompatTests.cs
- [ ] T047 [P] Add regression tests for legacy linear sequence compatibility in tests/integration/Sequences/LegacySequenceCompatibilityIntegrationTests.cs
- [ ] T048 Add performance verification tests for conditional execution overhead in tests/integration/Sequences/ConditionalExecutionPerformanceIntegrationTests.cs
- [ ] T049 Update authoring and execution docs in docs/validation.md
- [ ] T050 Run quickstart verification and capture evidence in specs/030-sequence-conditional-logic/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: starts immediately.
- **Phase 2 (Foundational)**: depends on Phase 1; blocks all user stories.
- **Phase 3 (US1)**: depends on Phase 2; defines MVP.
- **Phase 4 (US2)**: depends on Phase 2 and consumes US1 contracts.
- **Phase 5 (US3)**: depends on Phase 2 and consumes US1 runtime hooks.
- **Phase 6 (Polish)**: depends on completion of targeted stories.

### User Story Dependencies

- **US1 (P1)**: no story dependency after foundational completion.
- **US2 (P2)**: can run after foundational + US1 API shape availability.
- **US3 (P3)**: can run after foundational + US1 execution hooks; independent of US2.

### Within Each User Story

- Tests first (contract/integration/unit), then implementation.
- Domain and service changes precede endpoint/UI wiring.
- Story complete only when independent test criteria pass.

---

## Parallel Execution Examples

### User Story 1

- Run in parallel: T013, T014, T015, T016, T017
- Run in parallel: T020 and T021 after T019

### User Story 2

- Run in parallel: T027, T028, T029
- Run in parallel: T031 and T032 after T030

### User Story 3

- Run in parallel: T036, T037, T038, T039
- Run in parallel: T041 and T043 after T040

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 and Phase 2.
2. Deliver Phase 3 (US1).
3. Validate independent test criteria for US1.
4. Demo/ship MVP.

### Incremental Delivery

1. Add US2 visual flow authoring after US1 contracts stabilize.
2. Add US3 observability enrichment after execution hooks are in place.
3. Complete Phase 6 polish and regression/performance validations.

### Team Parallelization

1. One stream handles domain/service execution (US1/US3 backend).
2. One stream handles visual authoring UI (US2).
3. One stream handles cross-cutting contract and integration tests.

---

## Notes

- `[P]` marks tasks that can be executed in parallel (different files, no blocking dependency).
- `[US#]` labels map directly to user stories from `spec.md`.
- Each user story phase is independently testable by design.

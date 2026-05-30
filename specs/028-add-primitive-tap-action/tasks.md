# Tasks: Primitive Tap in Commands

**Input**: Design documents from /specs/027-add-primitive-tap-action/
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are REQUIRED for executable logic per the Constitution.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Align contracts, sample data, and test scaffolding for this feature.

- [X] T001 Add primitive tap backend test fixture payload in tests/TestAssets/sample-primitive-tap-command.json
- [X] T002 [P] Add primitive tap sample command JSON in data/commands/sample-primitive-tap-command.json
- [X] T003 [P] Add primitive tap API contract documentation updates in specs/027-add-primitive-tap-action/contracts/primitive-tap.openapi.yaml

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add core types and shared plumbing required by all user stories.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T004 Add PrimitiveTap step type to domain command step enum in src/GameBot.Domain/Commands/CommandStep.cs
- [X] T005 [P] Add primitive tap DTO models and outcome DTOs in src/GameBot.Service/Models/Commands.cs
- [X] T006 [P] Add primitive tap mapping helpers and validation hooks in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [X] T007 Add execution outcome response model updates for execute endpoints in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [X] T008 [P] Extend web UI command DTO/service types for PrimitiveTap and step outcomes in src/web-ui/src/services/commands.ts

**Checkpoint**: Foundation ready for independent user story implementation.

---

## Phase 3: User Story 1 - Add Detection-Gated Primitive Tap (Priority: P1) 🎯 MVP

**Goal**: Allow command authors to add primitive tap steps that execute only when detection succeeds, with x/y offsets and highest-confidence match behavior.

**Independent Test**: Create a command containing a PrimitiveTap step with valid detection, execute it, and verify one tap occurs at detected point + offsets with outcome status recorded as executed.

### Tests for User Story 1

- [X] T009 [P] [US1] Add unit tests for primitive tap coordinate selection and highest-confidence behavior in tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs
- [X] T010 [P] [US1] Add integration tests for primitive tap success and detection-failed skip in tests/integration/Commands/PrimitiveTapExecutionIntegrationTests.cs
- [X] T011 [P] [US1] Add contract test coverage for PrimitiveTap schema and execute response outcomes in tests/contract/OpenApiContractTests.cs
- [X] T012 [P] [US1] Add web UI service tests for primitive tap payload and stepOutcomes parsing in src/web-ui/src/services/__tests__/commands.spec.ts

### Implementation for User Story 1

- [X] T013 [US1] Implement PrimitiveTap step deserialization/serialization in command create/get/update endpoints in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [X] T014 [US1] Implement primitive tap detection resolution and highest-confidence selection in executor flow in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T015 [US1] Implement primitive tap tap-dispatch conversion and accepted count updates in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T016 [US1] Return per-step primitive execution outcomes from force-execute and evaluate-and-execute responses in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [X] T017 [US1] Add PrimitiveTap authoring controls in command form step editor in src/web-ui/src/components/commands/CommandForm.tsx
- [X] T018 [US1] Wire PrimitiveTap create/edit persistence and mapping in commands page in src/web-ui/src/pages/CommandsPage.tsx
- [X] T019 [P] [US1] Add UI tests for creating/editing primitive tap steps in src/web-ui/src/pages/__tests__/CommandsPage.spec.tsx

**Checkpoint**: User Story 1 independently functional and testable.

---

## Phase 4: User Story 2 - Preserve Existing Action-Based Behavior (Priority: P2)

**Goal**: Keep existing action-referenced command behavior unchanged while introducing primitive tap support.

**Independent Test**: Execute existing action-only commands before/after changes and verify accepted input counts and behavior remain consistent.

### Tests for User Story 2

- [X] T020 [P] [US2] Add regression unit tests for action-only command execution path in tests/unit/Commands/CommandExecutorTests.cs
- [X] T021 [P] [US2] Add integration regression tests for action-only execute/evaluate flows in tests/integration/CommandEvaluateAndExecuteTests.cs
- [X] T022 [P] [US2] Add web UI regression tests ensuring legacy Action/Command step authoring still works in src/web-ui/src/pages/__tests__/CommandsPage.spec.tsx

### Implementation for User Story 2

- [X] T023 [US2] Ensure Action and Command step mapping remains backward compatible with PrimitiveTap additions in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [X] T024 [US2] Ensure executor preserves existing behavior for non-PrimitiveTap steps in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T025 [US2] Ensure command service and form defaults preserve legacy step rendering in src/web-ui/src/services/commands.ts

**Checkpoint**: User Stories 1 and 2 both pass independently.

---

## Phase 5: User Story 3 - Prevent Unsafe Default Taps (Priority: P3)

**Goal**: Reject primitive tap steps without detection at save/validation time and skip out-of-bounds targets with explicit status.

**Independent Test**: Attempt to save a primitive tap step without detection and confirm 400 validation; execute a primitive tap with out-of-bounds computed target and confirm skipped/invalid-target with zero accepted tap inputs.

### Tests for User Story 3

- [X] T026 [P] [US3] Add integration tests for missing-detection validation rejection in tests/integration/Commands/PrimitiveTapValidationIntegrationTests.cs
- [X] T027 [P] [US3] Add integration tests for out-of-bounds skipped/invalid-target outcome in tests/integration/Commands/PrimitiveTapExecutionIntegrationTests.cs
- [X] T028 [P] [US3] Add UI validation tests for required primitive detection fields in src/web-ui/src/pages/__tests__/CommandsPage.detection.spec.tsx

### Implementation for User Story 3

- [X] T029 [US3] Enforce primitive tap detection-required validation in command create/update endpoints in src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [X] T030 [US3] Enforce out-of-bounds skip behavior and invalid-target outcome in command executor in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T031 [US3] Add authoring form validation messages for primitive tap detection requirements in src/web-ui/src/pages/CommandsPage.tsx

**Checkpoint**: All user stories independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, documentation alignment, and quality gates.

- [X] T032 [P] Update quick verification steps with final request/response examples in specs/027-add-primitive-tap-action/quickstart.md
- [X] T033 [P] Add/refresh performance notes for primitive tap detection overhead in specs/027-add-primitive-tap-action/plan.md
- [X] T034 Run full backend tests and analyze failures using repo script in scripts/analyze-test-results.ps1
- [X] T035 Run web UI command-related tests and fix any contract mismatches in src/web-ui/src/services/__tests__/commands.spec.ts
- [X] T036 Run .NET formatting and analyzer checks for touched backend code in src/GameBot.Service/GameBot.Service.csproj
- [X] T037 Run frontend lint checks for command authoring/service changes in src/web-ui/package.json
- [X] T038 Run repository security/secret scanning and document pass status in docs/validation.md
- [X] T039 Collect and verify touched-area coverage thresholds (>=80% line, >=70% branch) in tools/coverage/
- [X] T040 Execute primitive tap performance validation and record p95/regression results in docs/perf-checklist.md
- [X] T041 Measure command authoring completion time for primitive tap flow and document SC-004 evidence in docs/validation.md

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): starts immediately.
- Foundational (Phase 2): depends on Setup and blocks all user story implementation.
- User Stories (Phases 3-5): all depend on Phase 2 completion.
- Polish (Phase 6): depends on desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: starts after Phase 2; no dependency on US2/US3.
- **US2 (P2)**: starts after Phase 2; regression-focused and can run in parallel with US1 after foundational work.
- **US3 (P3)**: starts after Phase 2; can run in parallel with US1/US2 but should be completed before final polish.

### Within Each User Story

- Add tests first and confirm they fail.
- Implement endpoint/model/executor changes.
- Implement web UI mapping/validation.
- Re-run story-specific tests.

### Suggested Completion Order

1. Phase 1 → Phase 2
2. MVP: Phase 3 (US1)
3. Phase 4 (US2) and Phase 5 (US3) in parallel where staffing allows
4. Phase 6

---

## Parallel Execution Examples

### User Story 1

- T009 and T010 can run in parallel (unit/integration test files differ).
- T011 and T012 can run in parallel (contract and web service tests differ).
- T017 and T019 can run in parallel after T008 (different UI files).

### User Story 2

- T020, T021, and T022 can run in parallel (distinct test layers/files).

### User Story 3

- T026 and T028 can run in parallel (integration vs UI test files).
- T029 and T031 can run in parallel after foundational DTO/mapping support exists.

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Deliver Phase 3 end-to-end.
3. Validate independent test criteria for US1.

### Incremental Delivery

1. Ship US1 for core primitive tap capability.
2. Add US2 regression hardening.
3. Add US3 validation safety controls.
4. Finish polish and full-suite verification.

### Team Parallel Strategy

1. One engineer owns endpoint/executor tasks.
2. One engineer owns web UI authoring tasks.
3. One engineer owns integration/contract tests.

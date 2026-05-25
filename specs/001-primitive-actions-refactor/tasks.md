# Tasks: Primitive Actions Data Model Refactor

**Input**: Design documents from /specs/001-primitive-actions-refactor/
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/primitive-actions-api.md, quickstart.md

**Tests**: Included because spec requires automated test updates across domain, service endpoints, and UI flows.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare shared scaffolding and test baselines for the refactor.

- [X] T001 Create refactor tracking notes in specs/001-primitive-actions-refactor/plan.md and specs/001-primitive-actions-refactor/quickstart.md with final implementation checkpoints
- [ ] T002 [P] Add temporary migration fixture data and removal fixtures for legacy Action references in tests/TestAssets/legacy-action-migration/
- [ ] T003 [P] Add baseline API contract snapshot for affected routes in tests/contract/ApiContractSnapshots/
- [ ] T004 [P] Add frontend primitive-action test fixtures in src/web-ui/src/test/fixtures/primitiveActions.ts

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build core model, validation, and contract infrastructure required before user-story work.

**CRITICAL**: No user story work starts before this phase is complete.

- [X] T005 Define discriminated primitive action contracts in src/GameBot.Domain/Actions/PrimitiveActionBase.cs and src/GameBot.Domain/Actions/PrimitiveActionVariants.cs
- [X] T006 [P] Add shared primitive selection value object for inline persistence in src/GameBot.Domain/Actions/PrimitiveActionSelection.cs
- [X] T007 Implement discriminator/payload schema validator in src/GameBot.Domain/Actions/PrimitiveActionValidationService.cs
- [X] T008 [P] Add startup cutover validation model in src/GameBot.Service/StartupValidation/CutoverValidationReport.cs
- [X] T009 Implement legacy Action reference scanner for data stores in src/GameBot.Service/StartupValidation/LegacyActionReferenceScanner.cs
- [X] T010 Wire fail-fast startup/readiness gating in src/GameBot.Service/Program.cs and src/GameBot.Service/Middleware/ErrorHandlingMiddleware.cs
- [X] T011 Remove Action repository registration and Action endpoint mapping in src/GameBot.Service/Program.cs
- [X] T012 Update shared OpenAPI route grouping for removed Action CRUD surface in src/GameBot.Service/Swagger/SwaggerConfig.cs

**Checkpoint**: Foundation complete; user stories can proceed.

---

## Phase 3: User Story 1 - Author Flows With Primitive Actions (Priority: P1)

**Goal**: Authors create/edit commands and sequences by selecting primitive actions directly, with inline discriminated payloads and no Action entity usage.

**Independent Test**: Create and edit a command and sequence in the authoring UI, persist them, reload them, and confirm no Action CRUD calls or Action ID references are required.

### Tests for User Story 1

- [X] T013 [P] [US1] Add domain tests for primitive selection validation in tests/unit/Domain/PrimitiveActionValidationServiceTests.cs
- [ ] T014 [P] [US1] Add contract tests for command and sequence inline primitive payloads in tests/contract/PrimitiveActionContractsTests.cs
- [ ] T015 [P] [US1] Add integration tests for command/sequence create-read-update with inline primitives in tests/integration/PrimitiveAuthoringFlowTests.cs
- [ ] T016 [P] [US1] Add web UI tests for command and sequence authoring primitive selectors in src/web-ui/src/pages/__tests__/CommandsAndSequencesPrimitiveAuthoring.spec.tsx

### Implementation for User Story 1

- [ ] T017 [US1] Replace sequence action contract mapping with primitive selection mapping in src/GameBot.Service/Models/SequenceStepContracts.cs and src/GameBot.Service/Program.cs
- [ ] T018 [US1] Replace command step Action-ID mapping with inline primitive selection mapping in src/GameBot.Service/Models/Commands.cs and src/GameBot.Service/Endpoints/CommandsEndpoints.cs
- [X] T019 [US1] Remove Action endpoint implementation in src/GameBot.Service/Endpoints/ActionsEndpoints.cs and remove related route constants in src/GameBot.Service/ApiRoutes.cs
- [X] T020 [US1] Remove Action repository domain interface and file repository implementation in src/GameBot.Domain/Actions/IActionRepository.cs and src/GameBot.Domain/Actions/FileActionRepository.cs
- [ ] T021 [US1] Update sequence validation for inline primitive payload semantics in src/GameBot.Domain/Services/SequenceStepValidationService.cs and src/GameBot.Domain/Services/ActionPayloadValidationService.cs
- [X] T022 [US1] Refactor command execution to consume inline primitive selections instead of Action lookup in src/GameBot.Service/Services/CommandExecutor.cs
- [ ] T023 [US1] Replace web UI action services with primitive-catalog plus inline payload services in src/web-ui/src/services/actionsApi.ts and src/web-ui/src/services/commands.ts
- [ ] T024 [US1] Remove action authoring pages/components and rewire navigation to primitive-based authoring in src/web-ui/src/pages/actions/ActionsListPage.tsx and src/web-ui/src/components/actions/ActionForm.tsx
- [ ] T025 [US1] Update sequence editor mapping/validation utilities for the shared primitive selection model in src/web-ui/src/lib/sequenceMapping.ts and src/web-ui/src/lib/validation.ts

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Start Sessions With Connect Primitive Action (Priority: P2)

**Goal**: Execution flow uses connect-to-game primitive selection with required parameters and preserved session-reuse behavior.

**Independent Test**: Select connect primitive in execution UI, provide required fields, start session, then force-execute command without manual sessionId and verify reuse works.

### Tests for User Story 2

- [ ] T026 [P] [US2] Add integration tests for connect primitive parameter validation and session start in tests/integration/ConnectPrimitiveSessionStartTests.cs
- [ ] T027 [P] [US2] Add integration tests for cached session reuse from connect primitive context in tests/integration/ConnectPrimitiveSessionReuseTests.cs
- [ ] T028 [P] [US2] Add web UI tests for execution connect primitive parameter UX in src/web-ui/src/pages/__tests__/ExecutionConnectPrimitive.spec.tsx

### Implementation for User Story 2

- [X] T029 [US2] Add connect primitive request contract and validation path in src/GameBot.Service/Models/Sessions.cs and src/GameBot.Service/Endpoints/SessionsEndpoints.cs
- [X] T030 [US2] Refactor command executor session resolution away from Action repository to primitive selection sources in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T031 [US2] Update connect-to-game typed payload mapping helpers in src/GameBot.Domain/Actions/ConnectToGameArgs.cs and src/GameBot.Domain/Actions/PrimitiveActionVariants.cs
- [ ] T032 [US2] Update execution page to select connect primitive and require parameters in src/web-ui/src/pages/Execution.tsx
- [ ] T033 [US2] Update execution client APIs for connect primitive payload submission in src/web-ui/src/services/sessions.ts and src/web-ui/src/types/actions.ts

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Preserve Existing Automation Behavior (Priority: P3)

**Goal**: Provide deterministic migration and startup fail-fast diagnostics so legacy persisted data is converted safely before rollout.

**Independent Test**: Run migration on legacy fixtures, start service successfully with migrated corpus, then verify startup/readiness fails with deterministic diagnostics when unmigrated references are reintroduced.

### Tests for User Story 3

- [ ] T034 [P] [US3] Add migration unit tests for Action reference conversion in tests/unit/Migration/LegacyActionMigrationTests.cs
- [ ] T035 [P] [US3] Add startup validation failure integration tests in tests/integration/CutoverStartupValidationTests.cs
- [ ] T036 [P] [US3] Add contract tests asserting Action CRUD removal from OpenAPI surface in tests/contract/RemovedActionRoutesContractTests.cs

### Implementation for User Story 3

- [ ] T037 [US3] Implement deterministic migration command/tooling for Action references in scripts/migrate-actions-to-primitives.ps1 and src/GameBot.Service/Migration/LegacyActionMigrationService.cs
- [ ] T038 [US3] Implement startup diagnostics emission for blocking references in src/GameBot.Service/StartupValidation/LegacyActionReferenceScanner.cs and src/GameBot.Service/StartupValidation/CutoverValidationReport.cs
- [ ] T039 [US3] Add readiness failure integration in service startup path in src/GameBot.Service/Program.cs
- [X] T040 [US3] Remove obsolete Action sample data and fixtures after migration in data/commands/ and data/actions/
- [ ] T041 [US3] Update execution-log projection compatibility for primitive outcomes in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story completion, documentation, and full verification.

- [ ] T042 [P] Update public docs for removed Action model and primitive authoring in docs/validation.md and README.md
- [ ] T043 [P] Regenerate and verify OpenAPI spec consistency in specs/openapi.json and src/GameBot.Service/Swagger/SwaggerConfig.cs
- [ ] T044 Run end-to-end verification from quickstart in specs/001-primitive-actions-refactor/quickstart.md
- [ ] T045 Run explicit startup cutover and execution latency benchmarks from specs/001-primitive-actions-refactor/quickstart.md and record results in specs/001-primitive-actions-refactor/plan.md
- [X] T046 Run full backend and frontend test suites and record results in specs/001-primitive-actions-refactor/plan.md
- [ ] T047 Perform cleanup of dead Action references across codebase in src/ and tests/

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1: No dependencies; starts immediately.
- Phase 2: Depends on Phase 1; blocks all user stories.
- Phase 3, Phase 4, Phase 5: Depend on Phase 2 completion.
- Phase 6: Depends on completion of selected user stories.

### User Story Dependencies

- US1 (P1): Depends only on Phase 2.
- US2 (P2): Depends on Phase 2 and shared primitives from US1 mapping/model work (T017-T018, T022).
- US3 (P3): Depends on Phase 2 and benefits from US1/US2 contract stabilization.

### Within Each User Story

- Tests first and failing before implementation.
- Model/contract changes before service mapping.
- Service mapping before UI integration.
- Story-specific verification before declaring checkpoint complete.

### Parallel Opportunities

- Phase 1: T002-T004 in parallel.
- Phase 2: T006, T008, T009 can run in parallel after T005.
- US1: T013-T016 in parallel; T023 and T025 in parallel after backend contract stabilization.
- US2: T026-T028 in parallel; T032 and T033 in parallel after T029.
- US3: T034-T036 in parallel; T038 and T041 in parallel after T037.
- Polish: T042 and T043 in parallel, with T045 for perf validation before broad cleanup.

---

## Parallel Example: User Story 1

- Parallel test batch: T013, T014, T015, T016
- Parallel implementation batch after API/domain shape lands: T023, T025

## Parallel Example: User Story 2

- Parallel test batch: T026, T027, T028
- Parallel UI/service batch after backend contract update: T032, T033

## Parallel Example: User Story 3

- Parallel test batch: T034, T035, T036
- Parallel diagnostics/logging batch after migration service exists: T038, T041

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Deliver Phase 3 (US1) with tests green.
3. Validate authoring paths work without Action entity.

### Incremental Delivery

1. Add US2 to restore/verify connect-to-game execution UX and session reuse.
2. Add US3 migration plus startup fail-fast validation.
3. Finish with Phase 6 cross-cutting verification and documentation.

### Team Parallelization

1. Team A: Domain/service mapping and startup validation core.
2. Team B: Web UI authoring/execution refactor.
3. Team C: Migration tooling and regression/contract coverage.

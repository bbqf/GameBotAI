# Tasks: Runtime Logging Control

**Input**: Design documents from `/specs/001-runtime-logging-control/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: All executable logic requires accompanying tests per the GameBot Constitution. User story phases list the mandatory contract/integration coverage ahead of implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare seed data and docs referenced across all user stories.

- [ ] T001 Seed baseline logging policy file with Warning/enabled defaults for known components in `data/config/logging-policy.json`
- [ ] T002 Document the new logging policy file lifecycle and required auth token in `ENVIRONMENT.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain + service infrastructure that every user story depends on.

- [ ] T003 Define `LoggingComponentSetting`, `LoggingPolicySnapshot`, and `LoggingChangeAudit` models in `src/GameBot.Domain/Logging/LoggingPolicyModels.cs`
- [ ] T004 Implement `LoggingPolicyRepository` to load/save snapshots at `data/config/logging-policy.json` inside `src/GameBot.Domain/Services/Logging/LoggingPolicyRepository.cs`
- [ ] T005 Implement `RuntimeLoggingPolicyService` that coordinates repository persistence, audit emission, and `LoggerFilterRule` updates in `src/GameBot.Domain/Services/Logging/RuntimeLoggingPolicyService.cs`
- [ ] T006 Add unit tests covering repository read/write validation in `tests/unit/Logging/LoggingPolicyRepositoryTests.cs`
- [ ] T007 Add unit tests covering runtime policy application + audit metadata in `tests/unit/Logging/RuntimeLoggingPolicyServiceTests.cs`

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Adjust live logging level (Priority: P1) 🎯 MVP

**Goal**: Operators can raise/lower a component’s logging level via REST and see the effect immediately without restarting the app.

**Independent Test**: `PUT /config/logging/components/{component}` updates `GameBot.Domain.Triggers` to Debug and the next log emitted from that component appears at Debug level while other components stay at Warning.

### Tests for User Story 1

- [ ] T008 [P] [US1] Add contract test for `PUT /config/logging/components/{componentName}` request/response in `tests/contract/LoggingConfigContractTests.cs`
- [ ] T009 [P] [US1] Add integration test verifying live level change propagation in `tests/integration/Config/LoggingComponentLevelTests.cs`

### Implementation for User Story 1

- [ ] T010 [US1] Create `LoggingComponentPatchDto` with level validation in `src/GameBot.Service/Models/Logging/LoggingComponentPatchDto.cs`
- [ ] T011 [US1] Add `MapComponentLoggingEndpoints` handler for the component-level PUT route in `src/GameBot.Service/Endpoints/ConfigLoggingEndpoints.cs`
- [ ] T012 [US1] Extend `RuntimeLoggingPolicyService` to update component level + persist snapshot in `src/GameBot.Domain/Services/Logging/RuntimeLoggingPolicyService.cs`
- [ ] T013 [US1] Wire immediate level switch updates into the logging builder in `src/GameBot.Service/Logging/LoggingBuilderExtensions.cs`

**Checkpoint**: User Story 1 independently delivers runtime level changes.

---

## Phase 4: User Story 2 - Toggle component logging (Priority: P2)

**Goal**: Operators can disable/enable a component’s log emission to reduce noise during investigations.

**Independent Test**: Calling the component endpoint with `"enabled": false` silences `Microsoft.AspNetCore` logs immediately and re-enabling resumes output without restarting.

### Tests for User Story 2

- [ ] T014 [P] [US2] Extend contract tests to cover the `enabled` flag payload in `tests/contract/LoggingConfigContractTests.cs`
- [ ] T015 [P] [US2] Add integration test ensuring disabled components emit no logs until re-enabled in `tests/integration/Config/LoggingComponentToggleTests.cs`

### Implementation for User Story 2

- [ ] T016 [US2] Persist and validate the `enabled` flag inside `RuntimeLoggingPolicyService` in `src/GameBot.Domain/Services/Logging/RuntimeLoggingPolicyService.cs`
- [ ] T017 [US2] Update the component endpoint handler to accept `enabled` toggles and return the resulting state in `src/GameBot.Service/Endpoints/ConfigLoggingEndpoints.cs`
- [ ] T018 [US2] Enforce disabled components at the logging pipeline level (short-circuit emission) in `src/GameBot.Service/Logging/LoggingGateMiddleware.cs`

**Checkpoint**: User Story 1 + 2 together allow both level and enablement control.

---

## Phase 5: User Story 3 - Review effective logging policy (Priority: P3)

**Goal**: Operators can fetch current policy state and reset all components back to Warning/enabled from a single endpoint.

**Independent Test**: `GET /config/logging` returns the accurate snapshot (including overrides) and `POST /config/logging/reset` reverts all entries to Warning/enabled within 10 seconds.

### Tests for User Story 3

- [ ] T019 [P] [US3] Add contract test for `GET /config/logging` and `/config/logging/reset` in `tests/contract/LoggingConfigContractTests.cs`
- [ ] T020 [P] [US3] Add integration test that fetches the policy, applies overrides, resets, and verifies defaults in `tests/integration/Config/LoggingPolicySnapshotTests.cs`

### Implementation for User Story 3

- [ ] T021 [US3] Implement the GET snapshot endpoint in `src/GameBot.Service/Endpoints/ConfigLoggingEndpoints.cs`
- [ ] T022 [US3] Implement the reset endpoint that reverts all components and persists the snapshot in `src/GameBot.Service/Endpoints/ConfigLoggingEndpoints.cs`
- [ ] T023 [US3] Emit aggregated reset audit entries in `src/GameBot.Domain/Services/Logging/RuntimeLoggingPolicyService.cs`

**Checkpoint**: All user stories independently testable; operators can inspect, mutate, and reset logging policies.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, validation, and observability improvements spanning all user stories.

- [ ] T024 [P] Add API/quickstart documentation for the new endpoints in `README.md` and `specs/001-runtime-logging-control/quickstart.md`
- [ ] T025 Harden error handling + validation responses (unknown component, invalid level, store unavailable) in `src/GameBot.Service/Middleware/ErrorHandlingMiddleware.cs`
- [ ] T026 Run the quickstart workflow end-to-end and capture evidence in `data/coverage/latest.json`

---

## Dependencies & Execution Order

### Phase Dependencies
- **Phase 1 → Phase 2**: Foundational work depends on setup artifacts (baseline file + docs) being present.
- **Phase 2 → Phase 3/4/5**: All user stories rely on shared domain models, repository, and runtime service created in Phase 2.
- **Phase 6**: Starts after desired user stories are complete to avoid documenting stale behaviors.

### User Story Dependencies
- **US1 (P1)**: No dependency on other stories once Phase 2 completes (forms MVP).
- **US2 (P2)**: Depends on US1 endpoint scaffolding but can be implemented concurrently once the handler structure exists.
- **US3 (P3)**: Depends on Phase 2 repository/service outputs but not on US2 (it only needs accurate snapshots + reset logic).

## Parallel Opportunities
- Setup tasks T001–T002 can be performed in parallel since they touch different files.
- Foundational tests T006–T007 can run in parallel after models/services exist because they live in separate test files.
- Within each user story:
  - Contract vs integration tests (e.g., T008 vs T009) can be authored simultaneously.
  - Service updates vs endpoint wiring (e.g., T012 vs T011) can proceed in tandem once DTOs exist.
- Different user stories (US2, US3) may proceed in parallel once Phase 2 completes, provided teams coordinate on shared files like `RuntimeLoggingPolicyService`.

## Implementation Strategy

1. **MVP First**: Complete Phases 1–2, then deliver US1 (Phase 3) to ship adjustable logging levels quickly.
2. **Incremental Enhancements**: Layer US2 (toggle) and US3 (snapshot/reset) afterward, ensuring each phase ends in a deployable state.
3. **Parallel Teaming**: After Phase 2, one developer can focus on US1 while another prepares US3 read/reset functionality; merge frequently to avoid drift.
4. **Validation**: After each phase, run the relevant integration tests plus the quickstart workflow before progressing.

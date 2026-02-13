# Tasks: Backend and Web UI Installer

**Input**: Design documents from `/specs/024-backend-webui-installer/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/installer.openapi.yaml`

**Tests**: Tests are REQUIRED for executable logic per constitution; this task list includes unit, integration, and contract coverage for installer flows.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: User story label (`[US1]`, `[US2]`, `[US3]`) for story-phase tasks only
- Every task includes explicit file path(s)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish installer module structure and baseline scaffolding.

- [ ] T001 Create installer folder structure and placeholder files in `src/GameBot.Service/Services/Installer/` and `src/GameBot.Domain/Installer/`
- [ ] T002 Create installer endpoint route scaffold in `src/GameBot.Service/Endpoints/InstallerEndpoints.cs`
- [ ] T003 [P] Add installer options model scaffold in `src/GameBot.Service/Models/Installer/InstallerOptions.cs`
- [ ] T004 [P] Add installer constants and defaults (`8080,8088,8888,80`) in `src/GameBot.Service/Services/Installer/InstallerDefaults.cs`
- [ ] T005 [P] Add initial installer config file scaffold in `data/config/installer-config.json`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core installer infrastructure required before any user story work.

**⚠️ CRITICAL**: User story implementation starts only after this phase.

- [ ] T006 Implement `InstallationProfile` domain model in `src/GameBot.Domain/Installer/InstallationProfile.cs`
- [ ] T007 [P] Implement `PrerequisiteStatus` domain model in `src/GameBot.Domain/Installer/PrerequisiteStatus.cs`
- [ ] T008 [P] Implement `EndpointConfiguration` and `PortProbeResult` models in `src/GameBot.Domain/Installer/EndpointConfiguration.cs` and `src/GameBot.Domain/Installer/PortProbeResult.cs`
- [ ] T009 Implement installer result aggregate model in `src/GameBot.Domain/Installer/InstallerExecutionResult.cs`
- [ ] T010 Implement installer config repository in `src/GameBot.Domain/Installer/InstallerConfigurationRepository.cs`
- [ ] T011 Implement shared installer validation service (mode/port/protocol/startup policy) in `src/GameBot.Service/Services/Installer/InstallerValidationService.cs`
- [ ] T012 [P] Register installer services and repository wiring in `src/GameBot.Service/Program.cs`
- [ ] T013 [P] Add installer test fixtures/helpers in `tests/integration/Helpers/InstallerTestEnvironment.cs` and `tests/unit/Helpers/InstallerTestBuilder.cs`

**Checkpoint**: Foundation complete — each user story can now proceed independently.

---

## Phase 3: User Story 1 - Complete Installation on Target Machine (Priority: P1) 🎯 MVP

**Goal**: Deploy backend + web UI with prerequisite detection/installation and endpoint announcement.

**Independent Test**: On a clean machine/profile, run installer and verify missing prerequisites are installed, files are deployed, and final web UI URL/port is announced.

### Tests for User Story 1

- [ ] T014 [P] [US1] Add contract tests for `/api/installer/preflight`, `/api/installer/execute`, and `/api/installer/status/{runId}` in `tests/contract/Installer/InstallerContractTests.cs`
- [ ] T015 [P] [US1] Add integration test for prerequisite detection/install skip behavior in `tests/integration/Installer/InstallerPrerequisiteFlowTests.cs`
- [ ] T016 [P] [US1] Add integration test for endpoint announcement payload in `tests/integration/Installer/InstallerEndpointAnnouncementTests.cs`
- [ ] T017 [P] [US1] Add unit tests for prerequisite state transitions in `tests/unit/Installer/PrerequisiteStatusEvaluatorTests.cs`

### Implementation for User Story 1

- [ ] T018 [US1] Implement prerequisite scanner service in `src/GameBot.Service/Services/Installer/PrerequisiteScanner.cs`
- [ ] T019 [US1] Implement prerequisite installer service (bundled/online fallback) in `src/GameBot.Service/Services/Installer/PrerequisiteInstaller.cs`
- [ ] T020 [US1] Implement installation file deployment orchestrator in `src/GameBot.Service/Services/Installer/InstallExecutionService.cs`
- [ ] T021 [US1] Implement web UI API endpoint preconfiguration writer in `src/GameBot.Service/Services/Installer/WebUiApiConfigWriter.cs`
- [ ] T022 [US1] Implement endpoint announcement formatter in `src/GameBot.Service/Services/Installer/EndpointAnnouncementBuilder.cs`
- [ ] T023 [US1] Implement installer API endpoints in `src/GameBot.Service/Endpoints/InstallerEndpoints.cs`
- [ ] T024 [US1] Register installer endpoints in `src/GameBot.Service/ApiRoutes.cs`

**Checkpoint**: User Story 1 is fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 - Select Runtime Mode and Network Settings (Priority: P2)

**Goal**: Support service/background modes with correct privilege handling, network exposure defaults, firewall policy, and port conflict suggestions.

**Independent Test**: Run install once in service mode and once in background mode; verify privilege rules, startup policy defaults, non-loopback backend binding, and deterministic port fallback.

### Tests for User Story 2

- [ ] T025 [P] [US2] Add integration tests for service mode elevation and boot auto-start in `tests/integration/Installer/InstallerServiceModeTests.cs`
- [ ] T026 [P] [US2] Add integration tests for background-app mode non-admin and login-start behavior in `tests/integration/Installer/InstallerBackgroundModeTests.cs`
- [ ] T027 [P] [US2] Add integration tests for firewall fallback warning/confirmation flow in `tests/integration/Installer/InstallerFirewallPolicyTests.cs`
- [ ] T028 [P] [US2] Add unit tests for deterministic Web UI port resolution order in `tests/unit/Installer/PortSelectionOrderTests.cs`
- [ ] T029 [P] [US2] Add unit tests for port conflict alternative suggestion logic in `tests/unit/Installer/PortProbeServiceTests.cs`

### Implementation for User Story 2

- [ ] T030 [US2] Implement service-mode registration and boot startup policy in `src/GameBot.Service/Services/Installer/ServiceModeRegistrar.cs`
- [ ] T031 [US2] Implement background-app mode registration with optional login-start in `src/GameBot.Service/Services/Installer/BackgroundAppRegistrar.cs`
- [ ] T032 [US2] Implement backend binding/network exposure configurator in `src/GameBot.Service/Services/Installer/BackendNetworkConfigurator.cs`
- [ ] T033 [US2] Implement firewall policy applier and confirmation warning generation in `src/GameBot.Service/Services/Installer/FirewallPolicyService.cs`
- [ ] T034 [US2] Implement port probe and alternatives engine in `src/GameBot.Service/Services/Installer/PortProbeService.cs`
- [ ] T035 [US2] Update installer orchestration to enforce mode/network policy decisions in `src/GameBot.Service/Services/Installer/InstallExecutionService.cs`

**Checkpoint**: User Stories 1 and 2 are independently testable and operational.

---

## Phase 5: User Story 3 - Unattended CLI Installation (Priority: P3)

**Goal**: Provide no-UI installation with complete CLI switch coverage and fail-fast validation.

**Independent Test**: Execute unattended install with valid switches (no prompts) and with invalid/missing switches (actionable failure).

### Tests for User Story 3

- [ ] T036 [P] [US3] Add unit tests for CLI argument parsing/validation in `tests/unit/Installer/InstallerCliArgumentParserTests.cs`
- [ ] T037 [P] [US3] Add integration tests for unattended success path in `tests/integration/Installer/InstallerUnattendedModeTests.cs`
- [ ] T038 [P] [US3] Add integration tests for unattended invalid-argument remediation messages in `tests/integration/Installer/InstallerCliValidationTests.cs`
- [ ] T039 [P] [US3] Add contract tests for preflight/execute request validation failures in `tests/contract/Installer/InstallerValidationContractTests.cs`

### Implementation for User Story 3

- [ ] T040 [US3] Implement installer CLI entry script with full switch surface in `scripts/install-gamebot.ps1`
- [ ] T041 [US3] Implement CLI argument parser and request mapper in `src/GameBot.Service/Services/Installer/InstallerCliArgumentParser.cs`
- [ ] T042 [US3] Implement unattended orchestration coordinator in `src/GameBot.Service/Services/Installer/InstallerCliCoordinator.cs`
- [ ] T043 [US3] Implement actionable CLI error/remediation formatter in `src/GameBot.Service/Services/Installer/InstallerCliErrorFormatter.cs`
- [ ] T044 [US3] Add installer CLI usage/help content in `scripts/install-gamebot.ps1`

**Checkpoint**: All user stories are independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening, docs, and validation across all stories.

- [ ] T045 [P] Add installer observability logs and correlation fields in `src/GameBot.Service/Services/Installer/InstallerTelemetry.cs` and `src/GameBot.Service/Services/Installer/InstallExecutionService.cs`
- [ ] T046 [P] Add performance-focused integration tests for preflight/announcement timing budgets in `tests/integration/Installer/InstallerPerformanceBudgetTests.cs`
- [ ] T047 [P] Update installer/operator documentation in `README.md` and `ENVIRONMENT.md`
- [ ] T048 Run `dotnet build -c Debug`, `dotnet test -c Debug --logger trx`, and `scripts/analyze-test-results.ps1`; document outcomes/remediation in `specs/024-backend-webui-installer/quickstart.md`
- [ ] T049 Add integration test validating first-launch web UI to backend API reachability in `tests/integration/Installer/InstallerFirstLaunchReachabilityTests.cs`
- [ ] T050 Add integration test validating persisted install mode/network config reload after restart in `tests/integration/Installer/InstallerConfigPersistenceRestartTests.cs`
- [ ] T051 Execute `dotnet format --verify-no-changes`, `dotnet build -c Debug -warnaserror` (SAST/static analysis gate), `dotnet list src/GameBot.Service/GameBot.Service.csproj package --vulnerable --include-transitive`, and `gitleaks detect --source . --no-git` (secret-scan gate); record results/remediation in `specs/024-backend-webui-installer/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: starts immediately
- **Phase 2 (Foundational)**: depends on Phase 1; blocks all story work
- **Phase 3 (US1)**: depends on Phase 2; MVP slice
- **Phase 4 (US2)**: depends on Phase 2; can run after US1 or in parallel staffing mode once foundation is complete
- **Phase 5 (US3)**: depends on Phase 2; uses shared installer orchestration services and remains independently testable from US1 delivery sequence
- **Phase 6 (Polish)**: depends on completion of selected stories

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories
- **US2 (P2)**: depends on foundational installer services; integrates with US1 execution flow
- **US3 (P3)**: independent from US1/US2 feature completion; depends only on foundational installer services and CLI surface

### Within Each User Story

- Write tests first and verify they fail before implementation.
- Implement domain/service logic before endpoint/CLI integration.
- Complete story checkpoint before broad cross-story polish.

---

## Parallel Opportunities

- **Setup**: `T003`, `T004`, `T005` can run concurrently after `T001`/`T002`.
- **Foundational**: `T007`, `T008`, `T012`, `T013` can run in parallel once `T006` starts entity baselines.
- **US1**: tests `T014-T017` parallel; implementation `T018` and `T019` parallel, then converge at `T020-T024`.
- **US2**: tests `T025-T029` parallel; implementation `T030-T034` parallel in pairs, then converge at `T035`.
- **US3**: tests `T036-T039` parallel; implementation `T040` and `T041` parallel, then converge at `T042-T044`.
- **Polish**: `T045-T047` can run in parallel; `T048` then `T049-T051` complete final validation, quality gates, and documentation updates.

---

## Parallel Example: User Story 2

```bash
# Execute tests in parallel:
T025, T026, T027, T028, T029

# Implement independent services in parallel:
T030, T031, T032, T033, T034
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 (Setup)
2. Complete Phase 2 (Foundational)
3. Complete Phase 3 (US1)
4. Validate independent test criteria for US1
5. Demo/deploy MVP installer baseline

### Incremental Delivery

1. Deliver US1 (complete install + prerequisites + endpoint announcement)
2. Add US2 (mode + network + firewall + port conflict handling)
3. Add US3 (unattended CLI automation)
4. Finish with cross-cutting polish and performance/documentation hardening

### Team Parallelization

- Developer A: US1 services/endpoints
- Developer B: US2 mode/network services
- Developer C: US3 CLI surface and validation
- Shared rotation for final polish tasks

---

## Notes

- All tasks strictly follow checklist format and include explicit paths.
- `[P]` tasks are file-isolated and dependency-safe for parallel execution.
- Story labels appear only in user story phases.
- This plan is executable in priority order with MVP at US1.

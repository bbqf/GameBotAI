# Tasks: Standalone Windows Installer (EXE/MSI)

**Input**: Design documents from `/specs/025-standalone-windows-installer/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/installer.openapi.yaml`, `quickstart.md`

**Tests**: Tests are REQUIRED for executable logic per constitution; this plan includes unit, contract, integration, and smoke test tasks.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: Story label (`[US1]`, `[US2]`, `[US3]`) for user story phases only
- Every task includes an explicit file path

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish installer workspace, scripts, and baseline packaging scaffold.

- [X] T001 Create installer payload folder conventions in `installer/wix/payload/README.md`
- [X] T002 Create WiX fragment scaffold for directory and component groups in `installer/wix/Fragments/Directories.wxs` and `installer/wix/Fragments/Components.wxs`
- [X] T003 [P] Add installer build variable props in `installer/wix/Installer.Build.props`
- [X] T004 [P] Add shared installer PowerShell helper module in `scripts/installer/common.psm1`
- [X] T005 [P] Add installer smoke script placeholders in `scripts/installer/install-smoke.ps1` and `scripts/installer/uninstall-smoke.ps1`
- [X] T006 Add installer README usage alignment in `installer/README.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that blocks all user story implementation.

**⚠️ CRITICAL**: User story work begins only after this phase is complete.

- [X] T007 Implement payload manifest generator with version metadata in `scripts/package-installer-payload.ps1`
- [X] T008 Implement runtime data root default resolver (`perMachine`/`perUser`) in `scripts/installer/common.psm1`
- [X] T009 [P] Implement canonical installer property map constants in `installer/wix/Fragments/InstallerProperties.wxs`
- [X] T010 [P] Implement WiX include for shared constants and exit code definitions in `installer/wix/Fragments/Constants.wxi`
- [X] T011 Implement bootstrapper-to-MSI property forwarding baseline in `installer/wix/Bundle.wxs`
- [X] T012 Implement MSI log path and retention custom action scaffold in `installer/wix/Fragments/Logging.wxs`
- [X] T013 [P] Add unit tests for property and scope validation helpers in `tests/unit/Installer/InstallerPropertyValidationTests.cs`
- [X] T014 [P] Add contract tests for canonical install request schema in `tests/contract/Installer/InstallerPropertyContractTests.cs`

**Checkpoint**: Foundation complete — each user story can proceed independently.

---

## Phase 3: User Story 1 - First-time install on clean machine (Priority: P1) 🎯 MVP

**Goal**: Deliver downloadable EXE/MSI flow that installs backend + web UI and leaves app ready to run.

**Independent Test**: Run installer on clean Windows environment and verify installed binaries, data root creation, and successful startup reachability.

### Tests for User Story 1

- [X] T015 [P] [US1] Add integration test for payload presence validation in `tests/integration/Installer/PayloadValidationTests.cs`
- [X] T016 [P] [US1] Add integration test for first-install success with defaults in `tests/integration/Installer/FirstInstallFlowTests.cs`
- [X] T017 [P] [US1] Add smoke test assertions for app install roots and data roots in `scripts/installer/install-smoke.ps1`
- [X] T052 [P] [US1] Add integration test for mode-specific startup registration in `tests/integration/Installer/StartupRegistrationTests.cs`

### Implementation for User Story 1

- [X] T018 [US1] Implement backend publish packaging into payload service folder in `scripts/package-installer-payload.ps1`
- [X] T019 [US1] Implement web-ui build artifact packaging into payload web-ui folder in `scripts/package-installer-payload.ps1`
- [X] T020 [US1] Implement MSI component harvesting for backend/web-ui payload files in `installer/wix/Fragments/Components.wxs`
- [X] T021 [US1] Implement install directory tree and shortcuts in `installer/wix/Fragments/Directories.wxs`
- [X] T022 [US1] Implement default runtime data directory creation custom action in `installer/wix/Fragments/DataDirectories.wxs`
- [X] T023 [US1] Implement startup smoke verification script for installed binaries in `scripts/installer/install-smoke.ps1`
- [X] T024 [US1] Wire baseline install/uninstall sequence in `installer/wix/Product.wxs`
- [X] T053 [US1] Implement mode-specific startup registration logic in `installer/wix/Fragments/StartupRegistration.wxs`
- [X] T054 [US1] Wire startup registration actions into installer execution sequence in `installer/wix/Product.wxs`

**Checkpoint**: US1 is fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 - Select mode, scope, network settings (Priority: P2)

**Goal**: Support service/background mode, scope constraints, deterministic port behavior, and data directory override in UI.

**Independent Test**: Validate mode/scope combinations, `DATA_ROOT` defaults and UI override behavior, and deterministic port fallback in installer validation step.

### Tests for User Story 2

- [X] T025 [P] [US2] Add unit tests for mode-scope rule matrix in `tests/unit/Installer/ModeScopeRulesTests.cs`
- [X] T026 [P] [US2] Add unit tests for deterministic web port selection order in `tests/unit/Installer/PortSelectionRulesTests.cs`
- [X] T027 [P] [US2] Add integration test for interactive data root override validation in `tests/integration/Installer/DataRootOverrideTests.cs`
- [X] T028 [P] [US2] Add integration test for service mode requiring per-machine scope in `tests/integration/Installer/ServiceScopeEnforcementTests.cs`
- [X] T055 [P] [US2] Add integration test for optional HTTPS configuration path in `tests/integration/Installer/HttpsConfigurationFlowTests.cs`

### Implementation for User Story 2

- [X] T029 [US2] Implement service-mode/per-machine enforcement launch condition in `installer/wix/Fragments/Validation.wxs`
- [X] T030 [US2] Implement background mode scope support properties in `installer/wix/Fragments/InstallerProperties.wxs`
- [X] T031 [US2] Implement scope-based default `DATA_ROOT` resolution in `installer/wix/Fragments/DataDirectories.wxs`
- [X] T032 [US2] Implement interactive UI editable data directory binding to `DATA_ROOT` in `installer/wix/Bundle.wxs`
- [X] T033 [US2] Implement `DATA_ROOT` writeability preflight check in `installer/wix/Fragments/Validation.wxs`
- [X] T034 [US2] Implement backend/web-ui port validation and fallback policy custom action in `installer/wix/Fragments/PortValidation.wxs`
- [X] T035 [US2] Persist selected mode/scope/data root and endpoints in installed config in `installer/wix/Fragments/ConfigTemplates.wxs`
- [X] T056 [US2] Implement explicit HTTPS option binding and validation flow in `installer/wix/Fragments/HttpsConfiguration.wxs`
- [X] T057 [US2] Document HTTPS enablement and remediation path in `specs/025-standalone-windows-installer/quickstart.md`

**Checkpoint**: US1 and US2 are independently testable and operational.

---

## Phase 5: User Story 3 - Automated enterprise rollout (Priority: P3)

**Goal**: Provide robust silent install behavior with canonical properties, standardized exit codes, and deterministic logs.

**Independent Test**: Execute unattended install via EXE properties and verify exit codes, logging path/retention, and policy enforcement including allowlisted prerequisite sources.

### Tests for User Story 3

- [X] T036 [P] [US3] Add contract test for `/installer/validate` request/response schema in `tests/contract/Installer/InstallerValidateEndpointContractTests.cs`
- [X] T037 [P] [US3] Add contract test for `/installer/execute` result and exit code mappings in `tests/contract/Installer/InstallerExecuteEndpointContractTests.cs`
- [X] T038 [P] [US3] Add integration test for silent install with explicit `DATA_ROOT` in `tests/integration/Installer/SilentInstallDataRootTests.cs`
- [X] T039 [P] [US3] Add integration test for non-allowlisted prerequisite source rejection in `tests/integration/Installer/AllowlistSourcePolicyTests.cs`
- [X] T040 [P] [US3] Add smoke test for log retention policy (10 files max) in `scripts/installer/install-smoke.ps1`

### Implementation for User Story 3

- [X] T041 [US3] Implement canonical silent property mapping (`MODE`, `SCOPE`, `DATA_ROOT`, ports, protocol) in `installer/wix/Bundle.wxs`
- [X] T042 [US3] Implement standardized exit code mapping (`0/3010/1603/1618/2`) in `installer/wix/Fragments/ExitCodes.wxs`
- [X] T043 [US3] Implement allowlisted prerequisite source policy enforcement in `installer/wix/Fragments/PrerequisitePolicy.wxs`
- [X] T044 [US3] Implement deterministic installer logging path and rollover in `installer/wix/Fragments/Logging.wxs`
- [X] T045 [US3] Implement unattended execution example scripts in `scripts/installer/silent-install-examples.ps1`

**Checkpoint**: All user stories are independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening, CI matrix, and release packaging quality gates.

- [X] T046 [P] Add GitHub Actions fast-tier checks for installer logic in `.github/workflows/ci-installer-fast.yml`
- [X] T047 [P] Add conditional installer build/test workflow for installer file changes in `.github/workflows/ci-installer-logic.yml`
- [X] T048 [P] Add release packaging/signing verification workflow in `.github/workflows/release-installer.yml`
- [X] T049 Add quickstart documentation for CI runner differences and self-hosted requirements in `specs/025-standalone-windows-installer/quickstart.md`
- [X] T050 Add uninstall cleanup validation coverage in `scripts/installer/uninstall-smoke.ps1`
- [X] T058 [P] Add static analysis enforcement for installer workflows in `.github/workflows/ci-installer-logic.yml`
- [X] T059 [P] Add security/secret-scan enforcement for installer workflows in `.github/workflows/ci-installer-fast.yml`
- [X] T060 Add install-duration timing capture for interactive and silent runs in `scripts/installer/install-smoke.ps1`
- [X] T061 Add SLO evidence recording checklist for timing validation in `specs/025-standalone-windows-installer/quickstart.md`
- [X] T062 [P] Add explicit static analysis script for installer validation in `scripts/installer/run-static-analysis.ps1`
- [X] T063 [P] Add explicit security and secret scanning script for installer validation in `scripts/installer/run-security-scans.ps1`
- [X] T051 Run quality gates (`dotnet format --verify-no-changes`, `dotnet format analyzers --verify-no-changes`, `dotnet build -c Debug -warnaserror`, `dotnet test -c Debug`, `powershell -NoProfile -File scripts/installer/run-static-analysis.ps1`, `powershell -NoProfile -File scripts/installer/run-security-scans.ps1`) and record outcomes in `specs/025-standalone-windows-installer/quickstart.md` (depends on `T062` and `T063`)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: starts immediately
- **Phase 2 (Foundational)**: depends on Phase 1 and blocks all user stories
- **Phase 3 (US1)**: depends on Phase 2 (MVP)
- **Phase 4 (US2)**: depends on Phase 2; can proceed after or alongside US1 staffing-wise
- **Phase 5 (US3)**: depends on Phase 2; independent rollout automation slice
- **Phase 6 (Polish)**: depends on completion of selected stories

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories
- **US2 (P2)**: depends on foundational installer property and validation infrastructure
- **US3 (P3)**: depends on foundational property/exit/log framework; independent from US1/US2 feature completion

### Within Each User Story

- Tests must be created first and fail before implementation changes.
- For stories that include UI/property wiring tasks, complete validation/policy logic tasks before any UI/property wiring tasks.
- Complete story checkpoint before cross-story polish.

### Parallel Opportunities

- Setup: `T003`, `T004`, `T005` parallel after initial scaffolding.
- Foundational: `T009`, `T010`, `T013`, `T014` parallel.
- US1 tests `T015-T017`, `T052` parallel; packaging/component tasks `T018-T022`, `T053` partially parallel.
- US2 tests `T025-T028`, `T055` parallel; rule/policy tasks `T029-T034`, `T056` parallel in pairs.
- US3 tests `T036-T040` parallel; execution policy tasks `T041-T045` parallel by file.
- Polish workflows `T046-T048`, `T058`, `T059`, `T062`, `T063` parallel.

---

## Parallel Example: User Story 2

```bash
# Run US2 tests in parallel:
T025, T026, T027, T028

# Implement independent US2 components in parallel:
T030, T031, T034
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 (Setup)
2. Complete Phase 2 (Foundational)
3. Complete Phase 3 (US1)
4. Validate US1 independently in clean-machine smoke flow
5. Demo/deploy MVP installer baseline

### Incremental Delivery

1. Deliver US1: first-time install path and payload deployment
2. Deliver US2: mode/scope/network/data-root UI behavior
3. Deliver US3: unattended rollout, exit codes, logging, allowlist policy
4. Finalize CI and release hardening in Polish phase

### Team Parallelization

- Developer A: MSI/component authoring and payload packaging
- Developer B: mode/scope/validation policies and UI binding
- Developer C: silent mode, exit code mapping, CI/release workflows

---

## Notes

- All tasks follow strict checklist format with IDs and explicit paths.
- `[P]` tasks are file-isolated and dependency-safe for parallel execution.
- User story labels appear only in story phases.
- This task list is immediately executable and supports independent testing per story.

# Tasks: Installer Semantic Version Upgrade Flow

**Input**: Design documents from `/specs/026-installer-semver-upgrade/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/versioning-installer.openapi.yaml, quickstart.md

**Tests**: Tests are required for executable logic per constitution and included in each story phase.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare checked-in versioning artifacts and build-script wiring points.

- [ ] T001 Create versioning state directory in installer/versioning/
- [ ] T002 Create checked-in override file in installer/versioning/version.override.json
- [ ] T003 Create dedicated release-line marker file in installer/versioning/release-line.marker.json
- [ ] T004 Create CI build counter seed file in installer/versioning/ci-build-counter.json
- [ ] T005 [P] Add versioning file include notes to docs/validation.md and docs/regression-pass.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build shared versioning primitives consumed by all user stories.

**⚠️ CRITICAL**: No user story implementation starts before this phase completes.

- [ ] T006 Implement semantic version value object and parser in src/GameBot.Domain/Versioning/SemanticVersion.cs
- [ ] T007 [P] Implement lexicographic comparer for Major.Minor.Patch.Build in src/GameBot.Domain/Versioning/SemanticVersionComparer.cs
- [ ] T008 [P] Implement version source loader for override/marker/counter files in src/GameBot.Domain/Versioning/VersionSourceLoader.cs
- [ ] T009 Implement resolution service for CI/local build behavior in src/GameBot.Domain/Versioning/VersionResolutionService.cs
- [ ] T010 [P] Register versioning services in src/GameBot.Service/Program.cs
- [ ] T011 [P] Add shared installer/versioning config models in src/GameBot.Service/Models/InstallerVersioningModels.cs
- [ ] T012 Add foundational unit tests for parser/comparer/resolution in tests/unit/Installer/SemanticVersionFoundationTests.cs

**Checkpoint**: Foundation complete; user story phases can proceed.

---

## Phase 3: User Story 1 - Safe upgrade path (Priority: P1) 🎯 MVP

**Goal**: Allow higher-version upgrade while preserving all previously persisted installer/runtime properties.

**Independent Test**: Install version A with non-default persisted properties, install higher version B, verify install succeeds and properties are retained.

### Tests for User Story 1

- [ ] T013 [P] [US1] Add integration test for upgrade property retention in tests/integration/Installer/UpgradePropertyRetentionTests.cs
- [ ] T014 [P] [US1] Add contract test for compare outcome preserve-properties flag in tests/contract/Installer/InstallerVersionCompareContractTests.cs

### Implementation for User Story 1

- [ ] T015 [US1] Implement persisted-property snapshot load/apply logic in installer/wix/Fragments/ConfigTemplates.wxs
- [ ] T016 [US1] Ensure upgrade flow carries persisted properties in installer/wix/Fragments/InstallerProperties.wxs
- [ ] T017 [US1] Wire preserve-properties behavior into install decision endpoint in src/GameBot.Service/Program.cs
- [ ] T018 [US1] Add retention-aware comparison result model updates in src/GameBot.Service/Models/InstallerVersioningModels.cs
- [ ] T019 [US1] Add upgrade-retention smoke steps in scripts/installer/install-smoke.ps1

**Checkpoint**: US1 is independently functional and testable.

---

## Phase 4: User Story 2 - Downgrade protection (Priority: P1)

**Goal**: Block installation when candidate version is lower than installed version using full four-component comparison.

**Independent Test**: Install version B, attempt to install lower version A, verify install is blocked with downgrade-specific message.

### Tests for User Story 2

- [ ] T020 [P] [US2] Add unit tests for downgrade/upgrade/same-build comparison matrix in tests/unit/Installer/VersionComparisonDecisionTests.cs
- [ ] T021 [P] [US2] Add integration test for downgrade block flow in tests/integration/Installer/DowngradeBlockFlowTests.cs

### Implementation for User Story 2

- [ ] T022 [US2] Add downgrade gate condition and message mapping in installer/wix/Product.wxs
- [ ] T023 [US2] Add compare endpoint for installed vs candidate versions in src/GameBot.Service/Program.cs
- [ ] T024 [US2] Enforce full 4-part comparison in src/GameBot.Domain/Versioning/SemanticVersionComparer.cs
- [ ] T025 [US2] Add downgrade diagnostics and remediation hints in scripts/installer/common.psm1

**Checkpoint**: US2 is independently functional and testable.

---

## Phase 5: User Story 3 - Same-build reinstall decision (Priority: P2)

**Goal**: Prompt user on same-build reinstall interactively, and skip-with-dedicated-code in unattended mode.

**Independent Test**: Re-run same build interactively and verify choice prompt; re-run in unattended mode and verify skip with dedicated same-build status code.

### Tests for User Story 3

- [ ] T026 [P] [US3] Add integration test for interactive same-build decision flow in tests/integration/Installer/SameBuildInteractiveDecisionTests.cs
- [ ] T027 [P] [US3] Add integration test for unattended same-build skip/status in tests/integration/Installer/SameBuildUnattendedStatusTests.cs

### Implementation for User Story 3

- [ ] T028 [US3] Add same-build decision UI/state plumbing in installer/wix/Fragments/NetworkConfigUi.wxs
- [ ] T029 [US3] Add same-build unattended skip behavior and status mapping in installer/wix/Product.wxs
- [ ] T030 [US3] Implement same-build decision endpoint behavior in src/GameBot.Service/Program.cs
- [ ] T031 [US3] Add dedicated same-build status handling in scripts/installer/silent-install-examples.ps1

**Checkpoint**: US3 is independently functional and testable.

---

## Phase 6: User Story 4 - Controlled automatic version progression (Priority: P2)

**Goal**: Resolve semantic version from override + release marker + CI counter with CI-authoritative persistence and local non-persist behavior.

**Independent Test**: Trigger CI build and verify persisted +1 build; run local build and verify derived N+1 without persisting repository state.

### Tests for User Story 4

- [ ] T032 [P] [US4] Add unit tests for override precedence and patch-reset behavior in tests/unit/Installer/VersionResolutionPolicyTests.cs
- [ ] T033 [P] [US4] Add integration test for CI-authoritative counter persistence in tests/integration/Installer/CiBuildCounterPersistenceTests.cs
- [ ] T034 [P] [US4] Add integration test for local non-persisted build derivation in tests/integration/Installer/LocalBuildDerivationTests.cs

### Implementation for User Story 4

- [ ] T035 [US4] Implement version resolve endpoint and source provenance in src/GameBot.Service/Program.cs
- [ ] T036 [US4] Implement release-line marker transition handling in src/GameBot.Domain/Versioning/VersionResolutionService.cs
- [ ] T037 [US4] Wire build-version resolution into installer build pipeline in scripts/build-installer.ps1
- [ ] T038 [US4] Add CI-only counter write/update script logic in scripts/installer/common.psm1
- [ ] T039 [US4] Add version metadata injection for MSI/Bundle in installer/wix/Installer.Build.props

**Checkpoint**: US4 is independently functional and testable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Finalize documentation, quality gates, and end-to-end verification.

- [ ] T040 [P] Update quickstart validation evidence steps in specs/026-installer-semver-upgrade/quickstart.md
- [ ] T041 [P] Update release notes/changelog entry in CHANGELOG.md
- [ ] T042 Run full verification suite (`dotnet test -c Debug` + installer scripts) and capture outcomes in docs/validation.md
- [ ] T043 [P] Run security/static scans for installer scripts via scripts/installer/run-security-scans.ps1 and scripts/installer/run-static-analysis.ps1
- [ ] T044 Run lint/format quality gates (`dotnet format --verify-no-changes` and `dotnet format analyzers --verify-no-changes`) and capture results in docs/validation.md
- [ ] T045 Run coverage validation for touched areas (>=80% line, >=70% branch) and record threshold evidence in docs/validation.md
- [ ] T046 Measure version-resolution and install-decision path timing (<=1s) and verify no installer execution regression >2%; document evidence in docs/validation.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies.
- **Phase 2 (Foundational)**: depends on Phase 1; blocks all user stories.
- **Phases 3–6 (User Stories)**: depend on Phase 2; default execution is sequential by priority order.
- **Phase 7 (Polish)**: depends on completion of targeted user stories.

### User Story Dependencies

- **US1 (P1)**: starts after Phase 2; no dependency on other stories.
- **US2 (P1)**: starts after Phase 2; uses shared comparison primitives from Phase 2.
- **US3 (P2)**: starts after Phase 2; can follow US2 for reuse of comparison outcomes.
- **US4 (P2)**: starts after Phase 2; independent from US1/US3, but should land before final polish evidence.

Suggested completion order: `US1 -> US2 -> US3 -> US4`.

---

## Parallel Execution Opportunities

- **Setup**: T005 can run alongside T001–T004.
- **Foundational**: T007, T008, T010, T011 can run in parallel after T006 starts.
- **US1**: T013 and T014 can run in parallel; T015 and T016 can run in parallel.
- **US2**: T020 and T021 can run in parallel.
- **US3**: T026 and T027 can run in parallel.
- **US4**: T032, T033, and T034 can run in parallel; T037 and T039 can run in parallel after T036.
- **Polish**: T040, T041, and T043 can run in parallel before T042 closes.

### Parallel Example: User Story 1

```bash
Task: "T013 [US1] Add integration test in tests/integration/Installer/UpgradePropertyRetentionTests.cs"
Task: "T014 [US1] Add contract test in tests/contract/Installer/InstallerVersionCompareContractTests.cs"
Task: "T015 [US1] Implement snapshot logic in installer/wix/Fragments/ConfigTemplates.wxs"
Task: "T016 [US1] Carry persisted properties in installer/wix/Fragments/InstallerProperties.wxs"
```

### Parallel Example: User Story 4

```bash
Task: "T032 [US4] Add unit tests in tests/unit/Installer/VersionResolutionPolicyTests.cs"
Task: "T033 [US4] Add CI persistence test in tests/integration/Installer/CiBuildCounterPersistenceTests.cs"
Task: "T034 [US4] Add local derivation test in tests/integration/Installer/LocalBuildDerivationTests.cs"
```

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Deliver Phase 3 (US1) end-to-end.
3. Validate US1 independently before moving on.

### Incremental Delivery

1. Foundation complete.
2. Deliver US1 (upgrade retention), then US2 (downgrade block).
3. Add US3 (same-build behavior), then US4 (automatic progression).
4. Finish polish/validation.

### Optional Team Parallel Strategy

1. One stream completes Phase 1–2.
2. After Phase 2, split by story:
   - Stream A: US1/US2 installer behavior.
   - Stream B: US3 same-build decision paths.
   - Stream C: US4 version progression and CI/local build semantics.
3. Converge on Phase 7 verification and release evidence.

This parallel mode is optional and applies only when team capacity is available; if not, follow the default sequential priority order.

---

## Notes

- All tasks follow required checklist format with IDs, optional `[P]`, and `[US#]` labels for story phases.
- File paths are explicit to keep tasks executable by an LLM without extra context.
- Tests are included per constitution and story-level independent validation requirements.

# Feature Specification: Installer Semantic Version Upgrade Flow

**Feature Branch**: `026-installer-semver-upgrade`  
**Created**: 2026-02-18  
**Status**: Draft  
**Input**: User description: "I want the installer to be able to process upgrades properly. While it already has the basic functionality, I'd like to introduce the automatic build versioning following semantic versioning scheme. The versions have to follow the scheme: <Major>.<Minor>.<Patch>.<Build> Major will be set explicitly by me. Minor will increase with every new feature and/or branch Patch is reset automatically with every new Minor all of them must be possible to overwrite manually from some checked in file Build version has to be increased by 1 every time a local or CI build is triggered. It also means, it has to be persisted somewhere in the repository, however I don't need to set it manually in normal cases. Installer shoud use this versioning scheme and should a) prohibit downgrades; b) install the upgrades without changing the properties and c) ask what to do if reinstall of the same build is requested."

## Clarifications

### Session 2026-02-18

- Q: For the repository-persisted `Build` counter, what should happen if two builds try to increment at nearly the same time? → A: CI-only authoritative counter; CI persists increments and local builds do not persist the global counter.
- Q: When the installed version equals the candidate version (including build) and the run is unattended/silent, what should the installer do by default? → A: Exit without changes and return a distinct same-build status code.
- Q: What should count as a “new feature/branch release line” that auto-increments `Minor`? → A: Only when a checked-in release-line marker is created or updated.
- Q: Which properties must be preserved during a higher-version upgrade by default? → A: All previously persisted installer/runtime properties.
- Q: How should the checked-in release-line marker be represented? → A: Separate dedicated marker file in the repository as the single authoritative value.
- Q: When deciding downgrade vs upgrade vs same-build, which version components should be compared? → A: Compare full `Major.Minor.Patch.Build` lexicographically by numeric component.
- Q: If local builds must not persist the global counter, how should a local build compute its `Build` component? → A: Read latest persisted CI counter and use `+1` for the local artifact only, with no write-back.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Safe upgrade path (Priority: P1)

As a user installing a newer release, I can run the installer and complete an in-place upgrade that keeps my previously configured installer/runtime properties unchanged.

**Why this priority**: Upgrade reliability and settings retention are core release requirements and directly affect production usability.

**Independent Test**: Can be fully tested by installing version `A`, changing configurable values, then installing higher version `B` and verifying install succeeds with the same effective settings.

**Acceptance Scenarios**:

1. **Given** an existing installation with customized properties, **When** the user runs an installer with a higher version, **Then** the installer completes successfully and preserves all previously persisted installer/runtime properties.
2. **Given** an existing installation with default properties, **When** the user runs an installer with a higher version, **Then** the installer completes successfully without requiring reconfiguration.

---

### User Story 2 - Downgrade protection (Priority: P1)

As a user, I am prevented from accidentally installing an older version over a newer installed version.

**Why this priority**: Preventing downgrades reduces risk of data loss, behavior regressions, and support incidents.

**Independent Test**: Can be fully tested by installing version `B`, then attempting to install lower version `A` and verifying installer blocks the action with clear feedback.

**Acceptance Scenarios**:

1. **Given** a newer version is already installed, **When** the user runs an older installer, **Then** installation is blocked and the user receives a downgrade-not-allowed message.
2. **Given** installed and candidate versions differ only by build component, **When** comparison is evaluated, **Then** install decision is based on the full four-part numeric comparison.

---

### User Story 3 - Same-build reinstall decision (Priority: P2)

As a user running the same installer build again, I am explicitly asked whether to repair/reinstall or cancel.

**Why this priority**: Same-build installs are a common support workflow and require clear user intent to avoid accidental changes.

**Independent Test**: Can be fully tested by installing build `X`, rerunning installer build `X`, and verifying the user must choose a reinstall action or cancel.

**Acceptance Scenarios**:

1. **Given** the same version and build is already installed, **When** the user launches that same installer, **Then** the installer presents a decision prompt with at least one proceed option and one cancel option.
2. **Given** the same version and build is already installed, **When** the user chooses cancel, **Then** no installation changes are applied.
3. **Given** the same version and build is already installed and execution is unattended, **When** the installer is launched, **Then** it exits without changes and returns a distinct same-build status code.

---

### User Story 4 - Controlled automatic version progression (Priority: P2)

As a release owner, I can rely on automatic version progression for branch/feature and build triggers while still being able to override values from a checked-in configuration file.

**Why this priority**: Predictable version progression is required for repeatable release operations and upgrade decisions.

**Independent Test**: Can be fully tested by running local/CI builds and verifying auto-increment behavior, then setting overrides in the checked-in version file and verifying override precedence.

**Acceptance Scenarios**:

1. **Given** a checked-in version file with explicit values, **When** a build runs, **Then** the resulting installer version uses those values according to defined precedence rules.
2. **Given** the current feature/minor baseline, **When** a dedicated checked-in release-line marker file is created or updated, **Then** the minor value increments and patch resets to zero unless overridden.
3. **Given** a CI build trigger, **When** version computation runs, **Then** the build value increments by exactly one and persists in repository-tracked version state.
4. **Given** a local build trigger, **When** version computation runs, **Then** the build value is derived without persisting changes to the global repository-tracked counter.
5. **Given** a local build trigger and persisted CI build counter value `N`, **When** version computation runs, **Then** the local artifact uses build value `N+1` without updating repository state.

### Edge Cases

- Multiple CI builds are triggered concurrently; version state update must remain deterministic and prevent duplicate persisted build numbers.
- A branch is created without a dedicated release-line marker file update; minor version must not auto-increment.
- Multiple potential marker sources exist; only the dedicated marker file must be treated as authoritative.
- Local build computes `N+1` from persisted CI counter while CI concurrently advances to `N+1`; resulting local artifact may duplicate CI build number and must be treated as non-publishable.
- Upgrade encounters deprecated persisted properties; supported persisted properties must be retained and unsupported properties must be handled without resetting supported ones.
- The checked-in version file is missing, malformed, or partially populated; installer generation must fail with actionable error messages.
- Minor is incremented for a new feature/branch while manual overrides specify patch/build values; precedence must remain deterministic.
- An upgrade crosses multiple versions (e.g., installed is much older than target); upgrade must still preserve retained properties.
- In unattended mode for same-build reinstall, installer must exit without changes and return dedicated status code `4090`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST represent installer version as four numeric components in the format `<Major>.<Minor>.<Patch>.<Build>`.
- **FR-002**: System MUST allow `Major`, `Minor`, and `Patch` to be overridden from a checked-in repository file.
- **FR-003**: System MUST treat checked-in override values as the highest-priority source for `Major`, `Minor`, and `Patch` when provided and valid.
- **FR-004**: System MUST increase `Minor` by one only when a dedicated checked-in release-line marker file is created or updated and no manual `Minor` override is provided.
- **FR-004a**: System MUST NOT auto-increment `Minor` based solely on branch creation or branch name changes.
- **FR-004b**: System MUST treat the dedicated release-line marker file as the single authoritative source for automatic release-line transitions.
- **FR-005**: System MUST reset `Patch` to `0` whenever `Minor` increases automatically, unless a manual `Patch` override is provided.
- **FR-006**: System MUST increment `Build` by exactly one for every CI build trigger.
- **FR-007**: System MUST persist `Build` state in a repository-tracked file from CI so subsequent CI builds continue from the previous value.
- **FR-007a**: System MUST NOT persist updates to the global repository-tracked `Build` counter from local builds.
- **FR-007b**: System MUST compute local build `Build` as one greater than the latest persisted CI counter value for local artifact generation only.
- **FR-007c**: System MUST treat locally computed build numbers as non-authoritative and non-publishing values.
- **FR-008**: System MUST block downgrade installation attempts where candidate version is lower than installed version.
- **FR-009**: System MUST allow in-place upgrade installation when candidate version is higher than installed version.
- **FR-009a**: System MUST determine downgrade, upgrade, and same-build outcomes by numeric lexicographic comparison of all four components in `Major.Minor.Patch.Build` order.
- **FR-010**: System MUST preserve all previously persisted installer/runtime properties during successful upgrades unless the user explicitly changes them.
- **FR-011**: System MUST prompt the user for action when the candidate installer version equals the installed version and build.
- **FR-012**: System MUST apply the user’s same-build choice by either performing reinstall/repair flow or exiting without changes.
- **FR-013**: System MUST provide deterministic non-interactive same-build behavior for unattended execution by exiting without changes.
- **FR-013a**: System MUST return dedicated status code `4090` for unattended same-build execution.
- **FR-014**: System MUST emit clear user-facing messages for downgrade-blocked, upgrade-allowed, same-build decision, and version-source validation failures.

### Key Entities *(include if feature involves data)*

- **Version Policy**: Defines version component rules, increment rules, precedence rules, and comparison semantics for install decisions.
- **Version State Record**: Repository-tracked state that stores current and last-issued build-related values used to compute next build output.
- **Version Override File**: Checked-in configuration containing optional manual values for major/minor/patch and related policy flags.
- **Release-Line Marker File**: Dedicated checked-in file whose creation or update indicates an intentional new release line for automatic minor increment logic.
- **Installer Decision Context**: Comparison input composed of installed version, candidate version, install mode, and user choice for same-build cases.

### Assumptions

- Existing installer upgrade detection already works and this feature extends it with stricter version policy behavior.
- Version components are non-negative integers and install decisions use numeric lexicographic comparison across all four components.
- Unattended execution pathways exist and can consume a predefined same-build decision policy without interactive prompts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of attempted installs where candidate version is lower than installed version are blocked before files are changed.
- **SC-002**: At least 95% of upgrade runs (higher candidate version) complete successfully without requiring users to re-enter previously configured properties.
- **SC-003**: In repeated CI build runs, 100% of generated artifacts show a build component that increments by exactly one from the prior recorded persisted value.
- **SC-004**: 100% of same-build reinstall launches present (or deterministically apply, for unattended mode) an explicit decision outcome before changes are made.
- **SC-005**: 100% of release artifacts generated after version-file overrides reflect the override values for the applicable components.

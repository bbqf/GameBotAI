# Feature Specification: Backend and Web UI Installer

**Feature Branch**: `[024-backend-webui-installer]`  
**Created**: 2026-02-13  
**Status**: Draft  
**Input**: User description: "I need an installer for the backend and web-ui..."

## Clarifications

### Session 2026-02-13

- Q: By default, how widely should the backend be reachable after installation? → A: Bind to all interfaces, but create firewall rules limited to local/private network ranges by default.
- Q: What should be the default protocol for backend/web UI endpoints after installation? → A: Default to HTTP on private-network scope, with optional HTTPS configuration during install.
- Q: Which default order should the installer use for Web UI port selection? → A: Use fixed order 8080 → 8088 → 8888 → 80, first available wins.
- Q: What should be the default startup policy after installation? → A: Service mode auto-starts on boot; background-app mode starts only on user login (if enabled).

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Complete Installation on Target Machine (Priority: P1)

As an operator, I can run one installer that deploys the backend and web UI with all required application files and prerequisites so the system works on a new machine without manual dependency setup.

**Why this priority**: Without complete installation and dependency handling, the product cannot be started or used.

**Independent Test**: Can be fully tested by installing on a clean target machine and confirming backend and web UI both launch successfully with no manual prerequisite installation.

**Acceptance Scenarios**:

1. **Given** a target machine missing one or more required runtimes, **When** the installer runs, **Then** it detects missing prerequisites and installs them before finalizing setup.
2. **Given** a target machine that already has required runtimes, **When** the installer runs, **Then** it skips reinstalling those prerequisites and completes successfully.
3. **Given** installation completes, **When** the operator reviews the final output, **Then** the installer shows the web UI URL and port to access the backend-enabled UI.

---

### User Story 2 - Select Runtime Mode and Network Settings (Priority: P2)

As an operator, I can choose service mode (admin-required) or background-app mode (non-admin) and receive port guidance so deployment matches machine permissions and network constraints.

**Why this priority**: Different environments require different privilege levels and startup behavior; incorrect mode blocks deployment.

**Independent Test**: Can be fully tested by running one install in service mode and one in background-app mode, verifying privilege handling, startup behavior, and network accessibility in each case.

**Acceptance Scenarios**:

1. **Given** the operator chooses service mode, **When** installation starts, **Then** the installer enforces administrator privileges before applying service-mode setup.
2. **Given** the operator chooses background-app mode, **When** installation starts, **Then** installation proceeds without requiring administrator privileges.
3. **Given** a requested port is already in use, **When** the installer validates network settings, **Then** it warns the operator and suggests available alternative ports before continuing.
4. **Given** backend installation is complete, **When** networking is configured, **Then** backend listening is configured for real network interfaces and not limited to localhost.

---

### User Story 3 - Unattended CLI Installation (Priority: P3)

As an operator, I can run the installer without a graphical UI and provide all configuration through CLI switches so installation can be automated.

**Why this priority**: Automation and scripted rollout are important for repeatable deployment across multiple machines.

**Independent Test**: Can be fully tested by running the installer in non-interactive mode with CLI arguments only and verifying the deployment matches provided parameters.

**Acceptance Scenarios**:

1. **Given** the installer is launched in no-UI mode, **When** all required CLI switches are supplied, **Then** installation completes without interactive prompts.
2. **Given** the installer is launched in no-UI mode with invalid or missing required switches, **When** validation runs, **Then** installation fails with clear corrective guidance.

---

### Edge Cases

- Requested web UI port is privileged (for example, 80) but selected mode/user context cannot bind to it.
- Backend preferred port is in use by another process at install time.
- Multiple preferred web ports are occupied; installer must continue offering additional valid alternatives.
- Prerequisite download or install fails due to network restriction or policy controls.
- Service-mode install is selected but elevation is denied by the user.
- Existing prior installation is detected with conflicting settings.
- CLI mode receives contradictory parameters (for example, both service and background mode switches).
- CLI mode runs without a required parameter for network or mode selection.
- Installer runs in a context that cannot apply firewall configuration; installer must explicitly warn about network exposure and request confirmation to continue with current host firewall behavior.

## Requirements *(mandatory)*

### Assumptions

- The target deployment platform is Windows.
- Installer supports first-time installation; upgrade and rollback behavior are out of current scope.
- If several acceptable web ports are available, installer uses the first available from a standard preferred list unless user overrides it.
- The operator can provide required installer files and has network access when prerequisite retrieval is needed.

### Functional Requirements

- **FR-001**: Installer MUST deploy backend and web UI application files required for execution on the target machine.
- **FR-002**: Installer MUST evaluate required prerequisite software before installation and determine whether each prerequisite is already present.
- **FR-003**: Installer MUST install or activate missing prerequisites required for backend and web UI operation according to the selected prerequisite policy.
- **FR-004**: Installer MUST allow the operator to choose one of two runtime modes: service mode or background-application mode.
- **FR-005**: Installer MUST require administrator privileges when service mode is selected.
- **FR-006**: Installer MUST support background-application mode without requiring administrator privileges.
- **FR-007**: Installer MUST validate requested backend and web UI ports before finalizing installation.
- **FR-008**: Installer MUST detect port conflicts and provide at least one alternative available port suggestion for each conflicted endpoint.
- **FR-009**: Installer MUST prefer standard web-facing ports for web UI exposure from a configurable preferred set that includes 80, 8080, 8088, and 8888.
- **FR-010**: Installer MUST configure backend listening so it is reachable through non-loopback network interfaces.
- **FR-011**: Installer MUST configure the web UI backend API target to the installed backend host and port that is reachable from the target network context.
- **FR-012**: Installer MUST display final connection details at completion, including web UI URL and port.
- **FR-013**: Installer MUST support an unattended no-UI execution mode.
- **FR-014**: Installer MUST expose all configurable installation parameters through CLI arguments/switches in no-UI mode.
- **FR-015**: Installer MUST validate CLI arguments before making system changes and return actionable error messages when inputs are invalid.
- **FR-016**: Installer MUST persist selected installation mode and network configuration so behavior remains consistent after restart.
- **FR-017**: Installer MUST fail safely when prerequisite installation does not succeed and report which prerequisite blocked completion.
- **FR-018**: Installer MUST provide a clear summary of selected mode, resolved ports, and backend/web UI endpoints before completion.
- **FR-019**: Installer distribution and runtime acquisition policy MUST guarantee prerequisite payload availability (bundled and/or approved online retrieval) without requiring manual dependency setup outside installer workflow.
- **FR-020**: Default backend exposure MUST bind to all interfaces while limiting inbound access to local/private network ranges when installer-managed firewall configuration is available.
- **FR-021**: If installer-managed firewall configuration is unavailable due to privilege or policy constraints, installer MUST warn the operator and require explicit confirmation before proceeding.
- **FR-022**: Installer MUST default announced backend and web UI endpoints to HTTP within the selected private-network exposure scope.
- **FR-023**: Installer MUST provide an optional HTTPS configuration path during installation and, when selected, validate required certificate inputs before completing HTTPS endpoint setup.
- **FR-024**: Unless explicitly overridden by the operator, installer MUST select the web UI port by trying preferred ports in this exact order and choosing the first available: 8080, then 8088, then 8888, then 80.
- **FR-025**: Installer MUST default service-mode deployment to automatic start at system boot, and default background-application mode to start at user login only when that option is enabled by the operator.

### Key Entities *(include if feature involves data)*

- **Installation Profile**: Captures operator-selected mode, install location, and run preferences for backend and web UI.
- **Prerequisite Status**: Represents each required external dependency and its detection/install outcome.
- **Network Endpoint Configuration**: Represents backend bind address scope, backend port, web UI port, and resolved public URL values.
- **Installer Execution Result**: Represents final installer outcome including success/failure state, warnings, and endpoint announcement details.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 95% of clean-machine installations complete successfully without manual prerequisite setup steps.
- **SC-002**: 100% of successful installations display the final web UI access URL and port at completion.
- **SC-003**: In validation testing, at least 95% of installations correctly detect occupied ports and provide at least one valid alternative suggestion.
- **SC-004**: In acceptance testing, 100% of successful installations result in a web UI that can reach the configured backend endpoint on first launch.
- **SC-005**: At least 90% of unattended CLI installations complete without interactive prompts when valid switches are provided.

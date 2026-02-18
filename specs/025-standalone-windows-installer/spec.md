# Feature Specification: Standalone Windows Installer (EXE/MSI)

**Feature Branch**: `025-standalone-windows-installer`
**Created**: 2026-02-16
**Status**: Draft

## Clarifications

### Session 2026-02-16

- Q: What installation scope should v1 support? → A: Support both per-machine and per-user scopes in v1.
 - Q: What installation scope should v1 support? → A: Support per-user scope only in v1.
- Q: What prerequisite distribution strategy should installer v1 use? → A: Hybrid model: bundle critical prerequisites with optional approved online fallback.
- Q: Should installer v1 define a single canonical property schema across EXE and MSI flows? → A: Yes, define one canonical property schema for interactive defaults and silent installs.
- Q: What deterministic installer log location and retention should v1 use? → A: `%ProgramData%\GameBot\Installer\logs` with rolling retention of the last 10 log files.
- Q: What should HTTPS behavior be in v1 installer flow? → A: Default to HTTP, with optional HTTPS configuration path documented and supported.
- Q: What code-signing policy should installer v1 enforce? → A: Require signing for release artifacts; allow unsigned or test-signed development builds.
- Q: What should the default install root be? → A: Use `%LocalAppData%\GameBot`.
- Q: What silent install exit code contract should v1 standardize? → A: `0=success`, `3010=reboot required`, `1603=fatal error`, `1618=install already running`, `2=validation error`.
- Q: What install duration target should v1 enforce? → A: Interactive clean-machine install in <=10 minutes; silent install in <=8 minutes, excluding reboot time.
- Q: How should online prerequisite fallback sources be controlled? → A: Allow online prerequisite retrieval only from a maintained allowlist of approved vendor URLs.

## Goal

Provide a downloadable Windows installer that sets up GameBot backend + web UI on a target machine without requiring a pre-running GameBot service.

## Product Shape

- Deliverable: signed installer EXE bootstrapper that installs MSI package(s)
- Platform: Windows x64
- Modes:
  - Interactive install wizard
  - Silent/unattended install (`/quiet`)

## User Stories

### US1 (P1): First-time install on a clean machine
As an operator, I download and run one installer executable that deploys GameBot backend and web UI, registers runtime startup behavior, and leaves the app ready to use.

### US2 (P2): Operator-selectable install mode and ports
As an operator, I run background-app mode with network/port settings during install, with deterministic conflict handling.

### US3 (P3): Automated enterprise rollout
As an operator, I run the installer silently with command-line properties and get predictable exit codes and logs.

## Functional Requirements

- Installer is distributed as executable bootstrapper and installs MSI package(s)
- Installer supports per-user installation scope only in v1
- Installer includes bundled payloads for backend, web UI, and required runtime dependencies
- Installer uses a hybrid prerequisite strategy: critical prerequisites are bundled; optional approved online retrieval is allowed as fallback when enabled
- Installer restricts optional online prerequisite retrieval to a maintained allowlist of approved vendor URLs
- Installer supports background-app mode with non-admin runtime execution
- Installer validates/suggests ports using deterministic order: `8080 -> 8088 -> 8888 -> 80`
- Installer configures backend reachability and web UI API endpoint on target machine
- Installer defaults endpoint configuration to HTTP and supports optional HTTPS configuration path
- Installer exposes local installer validation/execute automation APIs for operator tooling, while EXE/MSI remains the primary installation entrypoint
- Installer supports silent mode with documented canonical properties
- Installer defines one canonical property schema used consistently by EXE bootstrapper and MSI execution paths
- Installer writes install logs to `%ProgramData%\GameBot\Installer\logs` and enforces rolling retention of the last 10 log files
- Installer enforces code-signing for release installer artifacts; development builds may be unsigned or test-signed
- Installer uses default install root `%LocalAppData%\GameBot`
- Installer uses runtime data root `%LocalAppData%\GameBot\data`
- Installer exposes runtime data directory in canonical silent property schema as `DATA_ROOT`, defaulted to `%LocalAppData%\GameBot\data`
- Installer interactive UI includes a user-editable data directory field pre-populated from scope default and validated before execution
- Installer standardizes silent install exit codes: `0`, `3010`, `1603`, `1618`, and `2`, with documented meanings
- Installer targets install duration SLOs: interactive <=10 minutes and silent <=8 minutes on clean machines, excluding reboot time
- Installer registers runtime startup behavior for background-app mode using per-user startup entry

## Non-Goals

- In-app installer HTTP endpoints as the primary end-user installation entrypoint
- Linux/macOS packaging
- Upgrade and rollback implementation in this first slice

## Acceptance Criteria

- EXE installer can be launched on clean Windows machine and complete setup
- Silent install can run without prompts and produce logs/exit code
- Canonical installer properties resolve identically between interactive defaults and silent mode inputs
- Installer logs are generated in the configured deterministic path and retention policy is enforced after repeated install attempts
- HTTPS configuration can be enabled during or after installation and is documented for operators
- Installer reports clear remediation when an optional online prerequisite retrieval attempt fails
- Installed app starts and web UI can reach backend on first launch
- Startup registration matches selected mode and is verifiable post-install
- Installer package artifacts are reproducible via CI build steps
- Release installer artifacts are signed and verifiable; development artifacts may be unsigned or test-signed
- Default install location is applied to `%LocalAppData%\GameBot` unless operator overrides it
- Runtime data directory is created at `%LocalAppData%\GameBot\data` and is writable by the current user context
- Silent install accepts valid `DATA_ROOT` override and applies it
- Interactive installer allows operator to change data directory and persists selected value on successful install
- Silent install returns standardized exit codes with documented remediation guidance per code
- Install duration SLOs are measurable in validation runs: interactive <=10 minutes and silent <=8 minutes, excluding reboot time
- Online prerequisite fallback succeeds only for allowlisted sources and rejects non-allowlisted sources with actionable remediation messaging
- HTTPS enablement path is verifiable with explicit installer flow validation and operator documentation

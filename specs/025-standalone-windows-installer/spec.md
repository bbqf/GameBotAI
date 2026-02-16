# Feature Specification: Standalone Windows Installer (EXE/MSI)

**Feature Branch**: `025-standalone-windows-installer`
**Created**: 2026-02-16
**Status**: Draft

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
As an operator, I select service/background mode and network/port settings during install, with deterministic conflict handling.

### US3 (P3): Automated enterprise rollout
As an operator, I run the installer silently with command-line properties and get predictable exit codes and logs.

## Functional Requirements

- Installer is distributed as executable bootstrapper and installs MSI package(s)
- Installer includes bundled payloads for backend, web UI, and required runtime dependencies
- Installer supports service mode requiring elevation
- Installer supports background-app mode with non-admin execution where applicable
- Installer validates/suggests ports using deterministic order: `8080 -> 8088 -> 8888 -> 80`
- Installer configures backend reachability and web UI API endpoint on target machine
- Installer supports silent mode with documented properties and exit codes
- Installer writes install logs to deterministic filesystem location

## Non-Goals

- In-app installer HTTP endpoints as primary installation mechanism
- Linux/macOS packaging
- Upgrade and rollback implementation in this first slice

## Acceptance Criteria

- EXE installer can be launched on clean Windows machine and complete setup
- Silent install can run without prompts and produce logs/exit code
- Installed app starts and web UI can reach backend on first launch
- Installer package artifacts are reproducible via CI build steps

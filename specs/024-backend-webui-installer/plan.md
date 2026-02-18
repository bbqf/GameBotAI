# Implementation Plan: Backend and Web UI Installer

**Branch**: `024-backend-webui-installer` | **Date**: 2026-02-13 | **Spec**: [specs/024-backend-webui-installer/spec.md](specs/024-backend-webui-installer/spec.md)
**Input**: Feature specification from `/specs/024-backend-webui-installer/spec.md`

## Summary

Deliver a Windows installer that deploys GameBot backend and web UI, handles prerequisite detection/installation, supports service and background-application install modes, validates/suggests ports, configures backend listening on non-loopback interfaces, preconfigures web UI backend endpoint, and supports both interactive and fully unattended CLI installation.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend and installer tooling), TypeScript (web UI runtime config)

**Primary Dependencies**: Existing ASP.NET Core Minimal API (`src/GameBot.Service`), existing web UI build artifacts (`src/web-ui`), Windows service control APIs, Windows firewall/port probing commands, existing JSON config repositories under `data/config`

**Storage**: File-backed configuration (`data/config`) for persisted installation mode, endpoint settings, and startup behavior; no new database

**Testing**: `dotnet test -c Debug` for unit/integration/contract suites plus installer-focused integration tests (prerequisite detection, mode selection, port conflict handling, CLI parsing)

**Target Platform**: Windows (installer host and runtime target)

**Project Type**: Web application deployment (backend service + web UI)

**Performance Goals**:
- Interactive installer preflight (prerequisite + port scan) completes in ≤30 seconds on baseline target machine.
- CLI validation errors return in ≤3 seconds.
- Endpoint announcement generated in ≤2 seconds after successful install finalization.

**Constraints**:
- Must support two install modes: service (admin-required) and background application (non-admin).
- Backend must be reachable on non-loopback interfaces.
- Default endpoint policy: HTTP on private-network scope, optional HTTPS path.
- Web UI port selection order defaults to 8080 → 8088 → 8888 → 80 unless overridden.
- No manual prerequisite setup required outside installer workflow.

**Scale/Scope**:
- Single-machine installer workflow for first-time install.
- Covers backend + web UI deployment, runtime dependencies, startup registration, and endpoint configuration.
- Upgrade/migration/rollback flows intentionally out of scope.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate

- **Code Quality Discipline**: PASS — plan keeps implementation in existing modules (`GameBot.Service`, installer scripts/tooling) with explicit validation/error paths and no new external framework obligations.
- **Testing Standards**: PASS — plan requires installer-focused tests plus existing .NET test suites; coverage targets for touched areas remain constitution baseline (≥80% line, ≥70% branch).
- **UX Consistency**: PASS — interactive and CLI modes both include actionable remediation messages, stable switches, and explicit final endpoint output.
- **Performance Requirements**: PASS — explicit preflight and response budgets included above.

No constitutional violations identified.

## Phase 0: Research Plan

Research tasks executed and documented in `research.md`:
1. Determine prerequisite detection/install strategy suitable for offline/online install scenarios.
2. Determine service vs background mode registration and startup strategy on Windows.
3. Determine secure default network exposure/firewall policy aligned with clarified spec decisions.
4. Determine deterministic port probing and fallback recommendation strategy.
5. Determine unattended CLI argument contract and validation pattern.

## Phase 1: Design Plan

Design artifacts produced:
- `data-model.md`: installer domain entities, fields, validation rules, and state transitions.
- `contracts/installer.openapi.yaml`: API/command contract for install orchestration and status reporting endpoints.
- `quickstart.md`: local validation steps for interactive and unattended installation scenarios.

Agent context update:
- Run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType copilot` after artifact generation.

### Post-Phase 1 Constitution Re-Check

- **Code Quality Discipline**: PASS — design keeps bounded entities and explicit validation surfaces.
- **Testing Standards**: PASS — contract + integration test surfaces defined for install flows.
- **UX Consistency**: PASS — endpoint announcement format and CLI/help behavior standardized.
- **Performance Requirements**: PASS — measurable installer preflight and completion-response budgets retained.

No violations introduced by design.

## Project Structure

### Documentation (this feature)

```text
specs/024-backend-webui-installer/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── installer.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Service/        # backend host, install/runtime config application
├── GameBot.Domain/         # configuration/domain models for install settings
├── GameBot.Emulator/       # unchanged by installer feature
└── web-ui/                 # preconfigured API base endpoint consumption

scripts/
└── *.ps1                   # installer orchestration and validation scripts (as needed)

data/
└── config/                 # persisted runtime/install configuration

tests/
├── unit/
├── integration/
└── contract/
```

**Structure Decision**: Reuse existing service + web-ui monorepo layout; add installer-specific orchestration and configuration logic without introducing new top-level projects or persistence stores.

## Complexity Tracking

No constitution violations or exceptional complexity exemptions required.

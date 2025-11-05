# Implementation Plan: GameBot Android Emulator Service

**Branch**: `001-android-emulator-service` | **Date**: 2025-11-05 | **Spec**: ./spec.md
**Input**: Feature specification from `/specs/001-android-emulator-service/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Deliver a Windows-hosted web service that can start, control, and monitor Android-based emulator sessions via a REST API. The service supports registering game artifacts, defining deterministic "learning" (automation) profiles, delivering periodic visual snapshots for UI consumption, and token-based authentication. The UI is a separate deployment consuming only the REST API.

Emulator/ADB strategy: When LDPlayer is installed, detect its installation and execute the bundled adb from that install (to avoid version conflicts). If LDPlayer is not present, fall back to system adb on PATH (and optionally AVD).

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: .NET 8 (C#)  
**Primary Dependencies**: ASP.NET Core Minimal API; SharpAdbClient (ADB integration); System.Drawing/Imaging or Windows Graphics Capture for snapshots  
**Storage**: File-based registry for game artifacts and profiles (JSON + filesystem paths) for MVP  
**Testing**: xUnit + FluentAssertions; coverlet for coverage; WebApplicationFactory for integration; API contract tests via OpenAPI snapshot tests  
**Target Platform**: Windows 10/11 with LDPlayer (preferred) or Android Emulator (AVD) available  
**Project Type**: single project (backend service) with separate UI deployment  
**Performance Goals**: p95 REST non-session ops < 200 ms; snapshot generation p95 < 500 ms; sustain ≥3 concurrent sessions on a standard workstation  
**Constraints**: Token-auth required for non-health endpoints; idle session timeout default 30 minutes; per-session memory budget target ≤ 1.5 GB incremental; CPU utilization cap via concurrency controls  
**Scale/Scope**: Single host; 3–5 concurrent sessions typical; tens of registered games and profiles

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- Code Quality: enable analyzers; enforce dotnet format; EditorConfig; secret scanning; modular structure (Service, Emulator wrapper, Domain); API docs and error handling guidelines.
- Testing: unit + integration + contract tests; coverage thresholds ≥80% line and ≥70% branch for changed areas; deterministic fixtures; bug-fix tests mandatory.
- UX Consistency: REST naming conventions; actionable errors with remediation hints; stable JSON schemas with versioning for breaking changes; consistent logging levels and message formats.
- Performance: defined budgets (above); hot paths profiled; perf note required on PRs touching emulator control loops; benchmarks for critical routines where feasible.

[Validated against `.specify/memory/constitution.md`; CI will gate merges on these checks.]

### Emulator/ADB Resolution (LDPlayer-first)

To ensure compatibility and avoid adb version conflicts, resolve adb path in this order:

1. Configuration override: `Service:Emulator:AdbPath` (appsettings or env var `GAMEBOT_ADB_PATH`).
2. LDPlayer detection:
   - Check env var `LDPLAYER_HOME` (or `LDP_HOME`) → use `<LDPLAYER_HOME>\adb.exe` if present.
   - Probe common install paths (64-bit Windows):
     - `C:\Program Files\LDPlayer\LDPlayer9\adb.exe`
     - `C:\Program Files\LDPlayer\LDPlayer8\adb.exe`
     - `C:\Program Files\LDPlayer\adb.exe`
     - `C:\LDPlayer\adb.exe`
   - Registry scan (if needed): HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall for DisplayName containing "LDPlayer"; use InstallLocation\adb.exe when found.
3. Fallback: system `adb` on PATH.

If LDPlayer is detected, prefer its bundled adb for all emulator interactions. When using LDPlayer, optional integration via `LDConsole.exe` (same install directory) may be added later for multi-instance control.

## Project Structure

### Documentation (this feature)

```text
specs/001-android-emulator-service/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
src/
├── GameBot.Domain/           # Core entities and contracts
├── GameBot.Emulator/         # Emulator/ADB wrapper and session control (LDPlayer-first adb resolution)
└── GameBot.Service/          # ASP.NET Core Web API (REST endpoints, auth)

tests/
├── unit/
├── integration/
└── contract/
```

**Structure Decision**: Single project (backend service) with three assemblies for separation of concerns; UI is a separate deployment not included in this repository feature.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| N/A |  |  |

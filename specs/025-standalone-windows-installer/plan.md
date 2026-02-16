# Implementation Plan: Standalone Windows Installer (EXE/MSI)

**Branch**: `025-standalone-windows-installer` | **Date**: 2026-02-16 | **Spec**: [spec.md](specs/025-standalone-windows-installer/spec.md)
**Input**: Feature specification from `/specs/025-standalone-windows-installer/spec.md`

## Summary

Deliver a standalone Windows installer distribution (bootstrapper EXE + MSI) that installs GameBot backend and web UI with dual scope support (per-machine/per-user), deterministic network/port policy, scoped prerequisite acquisition (bundled + allowlisted fallback), standardized silent install contract, local automation APIs for validation/execution tooling, and repeatable packaging outputs for CI.

## Technical Context

**Language/Version**: C# 13 / .NET 9, PowerShell 5.1+, WiX authoring (XML)  
**Primary Dependencies**: `WixToolset.Sdk` (v4), existing `GameBot.Service` publish output, existing web UI build output  
**Storage**: Installer runtime logs under `%ProgramData%\GameBot\Installer\logs`; installed app files in `%ProgramFiles%\GameBot` (per-machine) or `%LocalAppData%\GameBot` (per-user); runtime app data in `%ProgramData%\GameBot\data` (per-machine) or `%LocalAppData%\GameBot\data` (per-user)  
**Testing**: `dotnet test -c Debug` (unit/integration/contract), installer smoke scripts (interactive + silent), packaging verification checks, startup-registration checks, HTTPS-path checks, and install-duration SLO timing validation  
**Target Platform**: Windows x64  
**Project Type**: Desktop installer packaging for existing backend + web UI system  
**Performance Goals**: Interactive install <=10 minutes; silent install <=8 minutes on clean machine (excluding reboot)  
**Constraints**: Service mode must be per-machine and elevated; prerequisite online fallback restricted to allowlisted vendor URLs; release artifacts must be code-signed; silent exit code contract fixed to `0/3010/1603/1618/2`; runtime data must not be stored under `%ProgramFiles%`; EXE/MSI remains the primary end-user entrypoint  
**Scale/Scope**: Single-node installer deployment for first-time setup; enterprise automation capable through unattended property schema

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate

- **Code Quality Discipline**: PASS — scoped installer modules/scripts with explicit quality and security gates (format/lint/static analysis/signing/secret scan).
- **Testing Standards**: PASS — includes unit/integration/contract plus installer smoke validation and deterministic silent-mode checks.
- **UX Consistency**: PASS — canonical property schema, deterministic log location, standardized exit codes, actionable remediation messages.
- **Performance Requirements**: PASS — explicit install-time SLOs defined and testable.

No constitutional violations identified.

## Phase 0: Outline & Research

Research tasks completed and consolidated in [research.md](specs/025-standalone-windows-installer/research.md):
- Installer technology selection and packaging strategy
- Scope/mode validation rules
- Prerequisite policy and source allowlisting
- Silent property and exit-code contract
- Logging, signing, and performance target decisions

All prior `NEEDS CLARIFICATION` items are resolved.

## Phase 1: Design & Contracts

Design artifacts generated:
- [data-model.md](specs/025-standalone-windows-installer/data-model.md)
- [contracts/installer.openapi.yaml](specs/025-standalone-windows-installer/contracts/installer.openapi.yaml)
- [quickstart.md](specs/025-standalone-windows-installer/quickstart.md)

Agent context update command executed in this phase:
- `.specify/scripts/powershell/update-agent-context.ps1 -AgentType copilot`

### Post-Design Constitution Re-Check

- **Code Quality Discipline**: PASS — modular entities and constrained installer contracts reduce ambiguity and implementation drift.
- **Testing Standards**: PASS — contract-defined validation/execution paths and smoke workflow included.
- **UX Consistency**: PASS — properties, logs, and exit-code semantics are standardized across interactive and silent flows.
- **Performance Requirements**: PASS — measurable SLO acceptance criteria captured in spec and quickstart validation flow.

No constitutional violations introduced by design.

## Project Structure

### Documentation (this feature)

```text
specs/025-standalone-windows-installer/
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
installer/
└── wix/
    ├── GameBot.Installer.wixproj
    ├── Bundle.wxs
    ├── Product.wxs
    └── payload/

scripts/
├── package-installer-payload.ps1
└── build-installer.ps1

src/
├── GameBot.Service/
├── GameBot.Domain/
└── web-ui/

tests/
├── unit/
├── integration/
└── contract/
```

**Structure Decision**: Use a dedicated installer packaging area (`installer/wix`) plus repository-level packaging scripts, while reusing existing backend/web-ui projects as payload producers.

## Complexity Tracking

No constitution violations requiring exception tracking.

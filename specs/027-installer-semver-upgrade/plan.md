# Implementation Plan: Installer Semantic Version Upgrade Flow

**Branch**: `026-installer-semver-upgrade` | **Date**: 2026-02-18 | **Spec**: [spec.md](specs/026-installer-semver-upgrade/spec.md)
**Input**: Feature specification from `/specs/026-installer-semver-upgrade/spec.md`

## Summary

Implement deterministic installer version lifecycle management using `Major.Minor.Patch.Build`, with explicit checked-in overrides, dedicated checked-in release-line marker transitions for minor increments, CI-authoritative persisted build counter progression, local non-persisting build derivation, full four-component upgrade comparison, downgrade prevention, same-build decision handling (including unattended exit-code behavior), and upgrade-time property preservation.

## Technical Context

**Language/Version**: C# / .NET 9, PowerShell 5.1+, WiX authoring (XML)  
**Primary Dependencies**: `WixToolset.Sdk` (v6), ASP.NET Core minimal API host configuration pipeline, existing JSON config/file repositories, PowerShell build scripts  
**Storage**: Checked-in repository version files for override/counter/marker state; existing installer/runtime persisted properties under user scope (`%LocalAppData%`)  
**Testing**: `dotnet test -c Debug` (unit/integration/contract), installer behavior tests for upgrade/downgrade/same-build paths, script-level validation tests for version progression logic  
**Target Platform**: Windows (local and CI build agents)  
**Project Type**: Installer and build/versioning enhancement for existing backend + web UI solution  
**Performance Goals**: Version computation and decision logic complete within 1 second per build/install invocation; no >2% regression on existing installer execution path timing  
**Constraints**: Full version comparison is `Major.Minor.Patch.Build`; CI is authoritative for persisted build counter; local builds never write global build counter; upgrade preserves all persisted installer/runtime properties; silent same-build exits with dedicated status code `4090`  
**Scale/Scope**: Single-repo, single-product version stream with concurrent CI runs and local developer builds

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate

- **Code Quality Discipline**: PASS — plan uses existing modular installer/build/service boundaries, with static/secret scan gates retained.
- **Testing Standards**: PASS — includes unit/integration/contract plus deterministic versioning and installer decision-path validation.
- **UX Consistency**: PASS — explicit downgrade/same-build messaging and stable status code behavior for unattended workflows.
- **Performance Requirements**: PASS — bounded version/dependency evaluation timing and regression guardrail documented.

No constitutional violations identified.

## Phase 0: Outline & Research

Research tasks completed and consolidated in [research.md](specs/026-installer-semver-upgrade/research.md):
- Authoritative source-of-truth for release-line transition
- CI-vs-local build counter behavior and conflict strategy
- Same-build unattended status semantics
- Four-component version comparison strategy
- Override precedence and failure behavior

All technical unknowns from planning are resolved in research decisions.

## Phase 1: Design & Contracts

Design artifacts generated:
- [data-model.md](specs/026-installer-semver-upgrade/data-model.md)
- [contracts/versioning-installer.openapi.yaml](specs/026-installer-semver-upgrade/contracts/versioning-installer.openapi.yaml)
- [quickstart.md](specs/026-installer-semver-upgrade/quickstart.md)

Agent context update command executed in this phase:
- `.specify/scripts/powershell/update-agent-context.ps1 -AgentType copilot`

### Post-Design Constitution Re-Check

- **Code Quality Discipline**: PASS — entities/contracts define strict validation boundaries and preserve existing security scan requirements.
- **Testing Standards**: PASS — design includes deterministic state transitions and contract-verifiable install decisions.
- **UX Consistency**: PASS — decision outcomes/messages/statuses standardized across interactive and unattended flows.
- **Performance Requirements**: PASS — data model and quickstart include bounded execution checks and regression verification.

No constitutional violations introduced by design.

## Project Structure

### Documentation (this feature)

```text
specs/026-installer-semver-upgrade/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── versioning-installer.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
installer/
└── wix/
    ├── Product.wxs
    ├── Bundle.wxs
    ├── Fragments/
    └── Scripts/

scripts/
└── installer/

src/
├── GameBot.Service/
│   └── Services/
├── GameBot.Domain/
└── web-ui/

tests/
├── unit/
├── integration/
└── contract/
```

**Structure Decision**: Reuse existing installer, script, backend, and test directories; introduce no new top-level projects.

## Complexity Tracking

No constitution violations requiring exception tracking.

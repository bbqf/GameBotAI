# Implementation Plan: Primitive Actions Data Model Refactor

**Branch**: `[001-primitive-actions-refactor]` | **Date**: 2026-05-25 | **Spec**: `/specs/001-primitive-actions-refactor/spec.md`
**Input**: Feature specification from `/specs/001-primitive-actions-refactor/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Refactor GameBot to remove Action as an authored backend/frontend/test entity and replace usage with inline-by-value primitive action selections modeled as discriminated typed variants. The implementation performs a hard cutover: Action compatibility paths are removed, persisted legacy references are migrated pre-rollout, and service startup/readiness fails fast when legacy references remain. Sequence and command flows continue to support existing behavior, including connect-to-game session reuse, with connect parameters surfaced only where required in execution UX.

## Technical Context

**Language/Version**: Backend C# 13 / .NET 9; Frontend TypeScript ES2020 / React 18 (Vite 5)  
**Primary Dependencies**: ASP.NET Core Minimal API, GameBot.Domain repositories/services, System.Text.Json, existing OpenCvSharp/ADB/session services, React + existing web-ui toolchain  
**Storage**: File-backed JSON under `data/` (notably `data/commands`, `data/commands/sequences`, `data/config`); Action storage removed from authored model  
**Testing**: dotnet xUnit suites (unit/integration/contract), web-ui Jest/RTL suites, existing Playwright UI coverage where applicable  
**Target Platform**: Windows-hosted .NET service and browser-based web UI
**Project Type**: Full-stack web application (service + web UI)  
**Performance Goals**: 
- Startup cutover validation completes in <= 5s for a 10k-record persisted corpus on reference dev hardware.
- No >2% regression in command/sequence execution p95 latency versus baseline for unchanged scenarios.
**Constraints**: 
- Hard cutover with no Action compatibility bridge.
- Fail-fast startup/readiness when legacy Action references remain.
- Primitive selections must persist inline by value with typed discriminator + matching payload.
- Connect-to-game parameters shown in execution UX; non-required primitive contexts remain parameterless.
**Scale/Scope**: 
- Cross-cutting backend + frontend + tests refactor across `src/GameBot.Domain`, `src/GameBot.Service`, `src/web-ui`, and all test projects.
- 500+ existing automated tests must remain green after refactor.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

Pre-Phase Gate Check (current baseline): PASS
- `dotnet build -c Debug`: PASS
- `dotnet test -c Debug`: PASS (506 passed, 0 failed)
- Result: implementation planning can proceed.

Post-Design Gate Check (after Phase 1 design artifacts): PASS
- Design includes deterministic migration + startup fail-fast validation path.
- Interface contract deltas and verification flow are documented in feature artifacts.
- No constitution violations requiring waiver identified.

## Project Structure

### Documentation (this feature)

```text
specs/001-primitive-actions-refactor/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── primitive-actions-api.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
├── GameBot.Emulator/
├── GameBot.Service/
└── web-ui/

tests/
├── unit/
├── integration/
└── contract/

data/
├── commands/
├── triggers/
├── games/
├── config/
└── images/
```

**Structure Decision**: Use the existing monorepo service + web-ui structure and perform an in-place refactor of current domain/service/UI/test modules. No new top-level projects or persistence stores are introduced.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations or waivers identified at planning time.

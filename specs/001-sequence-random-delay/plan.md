# Implementation Plan: Randomized Sequence Step Delays

**Branch**: `001-sequence-random-delay` | **Date**: 2026-05-25 | **Spec**: `C:\src\GameBot\specs\001-sequence-random-delay\spec.md`
**Input**: Feature specification from `C:\src\GameBot\specs\001-sequence-random-delay\spec.md`

## Summary

Introduce sequence-level randomized inter-step delays applied between consecutive executed sequence steps. Default behavior uses a uniform inclusive range of `100..300` ms, and each sequence can optionally override this with integer millisecond `min/max` bounds (`min >= 0`, `min <= max`, no explicit upper bound). The implementation extends existing sequence persistence/contracts and integrates timing behavior consistently across linear and flow execution paths while preserving existing per-step delay semantics.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend service/domain), TypeScript ES2020 / React 18 (authoring consumer)  
**Primary Dependencies**: ASP.NET Core Minimal API, existing `GameBot.Domain` command/sequence models and `SequenceRunner`, existing file-backed repositories, Swagger/OpenAPI generation in `GameBot.Service`  
**Storage**: Existing file-backed JSON under `data/commands/sequences`  
**Testing**: `dotnet test -c Debug` (unit/integration/contract), API contract assertions in contract tests  
**Target Platform**: Windows-hosted backend service and web authoring UI  
**Project Type**: Web app backend + frontend in monorepo  
**Performance Goals**: Added inter-step delay computation overhead remains negligible relative to configured delay; no additional non-configured wait beyond sampled range  
**Constraints**: Uniform random sampling, inclusive bounds, integer milliseconds only, backward compatibility for existing sequences without new field, no explicit upper bound  
**Scale/Scope**: Sequence model + API contract + runner behavior + tests for linear and flow execution

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

### Pre-Design Gate Review

- **Code Quality Discipline**: PASS
  - Changes are scoped to existing sequence domain/service contracts and runner logic; no new framework introduced.
  - Clear modular boundaries: sequence model field, API DTO mapping/validation, runtime application point.
- **Testing Standards**: PASS
  - Baseline verification executed successfully: `dotnet build -c Debug` and `dotnet test -c Debug` passed.
  - Plan includes unit/integration/contract tests for range validation, defaulting, and execution behavior.
- **User Experience Consistency**: PASS
  - Feature preserves existing sequence authoring semantics and adds explicit validation feedback for invalid ranges.
  - Backward compatibility for legacy sequences is maintained via default behavior.
- **Performance Requirements**: PASS
  - Performance budget is explicit: sampled delay must stay within configured/default range with no extra trailing waits.

### Post-Design Gate Review

- **Code Quality Discipline**: PASS (data model and contracts remain additive and bounded).
- **Testing Standards**: PASS (quickstart and planned tests cover deterministic validation and execution-path behavior).
- **User Experience Consistency**: PASS (contracts/documentation specify defaults and error expectations clearly).
- **Performance Requirements**: PASS (design constrains waits strictly to sampled inclusive bounds and transition-only application).

## Project Structure

### Documentation (this feature)

```text
specs/001-sequence-random-delay/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── sequence-random-delay.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/
│   │   ├── CommandSequence.cs
│   │   └── SequenceStep.cs
│   └── Services/
│       └── SequenceRunner.cs
├── GameBot.Service/
│   ├── Models/
│   │   └── SequenceStepContracts.cs
│   └── Program.cs
└── web-ui/
    └── src/
        └── sequence authoring/editing surfaces consuming sequence contracts

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: Keep implementation inside existing Domain/Service/test projects with additive sequence schema changes and no new top-level modules.

## Phase 0: Research Plan

- Confirm inter-step timing semantics and transition points across linear and flow execution.
- Confirm compatibility strategy with existing step-level delay fields.
- Confirm random sampling behavior (uniform + inclusive bounds) and validation rules (integer-only, min/max relationship).
- Confirm persistence and API contract extension shape for per-sequence configuration.
- Confirm deterministic test strategy for correctness and non-regression.

## Phase 1: Design Plan

- Produce `data-model.md` for sequence-level delay configuration and runtime transition behavior.
- Produce `contracts/sequence-random-delay.openapi.yaml` documenting request/response schema additions and execute behavior expectations.
- Produce `quickstart.md` with build/test, validation, and behavior verification steps.
- Update Copilot agent context via `.specify/scripts/powershell/update-agent-context.ps1 -AgentType copilot`.

## Phase 2: Task Planning Approach (for `/speckit.tasks`)

- Slice implementation by vertical capabilities:
  1. Domain model + persistence serialization support for sequence-level delay range.
  2. API contract + mapping + validation updates for create/put/patch paths.
  3. Runner integration for inter-step delay in linear and flow execution.
  4. Backward-compatible defaulting behavior for missing persisted configuration.
  5. Unit/integration/contract tests plus regression checks.
- Include constitution-aligned verification tasks:
  - Re-run `dotnet build -c Debug` and `dotnet test -c Debug` after implementation.
  - Ensure touched-area coverage and deterministic test outcomes.

## Complexity Tracking

No constitution violations identified; no complexity exemptions required.

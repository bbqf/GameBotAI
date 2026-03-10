# Implementation Plan: Per-Step Optional Conditions

**Branch**: `[032-per-step-conditions]` | **Date**: 2026-03-06 | **Spec**: [specs/032-per-step-conditions/spec.md](specs/032-per-step-conditions/spec.md)
**Input**: Feature specification from `/specs/032-per-step-conditions/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Replace entry-step/branch graph authoring with a linear sequence model where each step can optionally define one condition. In v1, conditions support `imageVisible` and `commandOutcome`; runtime evaluates each condition immediately before step execution (`true` executes, `false` skips, evaluation errors fail-stop), with `commandOutcome` references constrained to earlier steps.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend)  
**Primary Dependencies**: ASP.NET Core Minimal API, GameBot.Domain sequence services/repositories, existing condition evaluator stack, React/Vite web-ui authoring modules  
**Storage**: File-backed JSON repositories under `data/commands/sequences` (no new datastore)  
**Testing**: xUnit (unit/integration/contract), Jest for web-ui authoring behavior  
**Target Platform**: Windows runtime and development environment  
**Project Type**: Web application (ASP.NET Core API + React authoring UI)  
**Performance Goals**: Preserve sequence runtime budget for conditional evaluation (no regression beyond existing <=200 ms p95 conditional-evaluation target profile)  
**Constraints**: Linear execution only; max one optional condition per step; supported condition types limited to `imageVisible` and `commandOutcome`; commandOutcome references only prior steps  
**Scale/Scope**: Single active sequence execution profile, mixed conditional/unconditional sequences (typical 5-30 steps), clean-slate per-step schema only

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

### Pre-Phase 0 Gate Review

- Code Quality Discipline: PASS - planned changes reuse existing modules and avoid new external packages.
- Testing Standards: PASS - plan includes unit, integration, contract, and UI coverage for per-step condition behavior and validation constraints.
- User Experience Consistency: PASS - authoring flow simplifies to step-level condition controls and removes branch wiring complexity.
- Performance Requirements: PASS - explicit non-regression target maintained for conditional-evaluation latency.

### Post-Phase 1 Design Re-check

- Code Quality Discipline: PASS - data and contract design remain bounded to linear sequence schema.
- Testing Standards: PASS - design artifacts define independently testable behavior for authoring, execution, and validation.
- User Experience Consistency: PASS - no entry-step or branch-link concepts exposed in v1 authoring contract.
- Performance Requirements: PASS - quickstart includes explicit conditional-evaluation verification profile.

## Phase 0: Research Output

`research.md` resolves:
- Replacement strategy for entry-step/branch graph with step-level optional conditions.
- v1 condition type scope and commandOutcome reference semantics.
- Validation and failure behavior for malformed step conditions.
- Contract shape for linear sequence payloads with optional condition metadata.

## Phase 1: Design & Contracts Output

- `data-model.md`: sequence step model with optional condition, commandOutcome reference constraints, and step outcome logging.
- `contracts/sequence-per-step-conditions.openapi.yaml`: API contracts for create/update/get/validate/execute with per-step optional conditions.
- `quickstart.md`: operator-oriented setup and verification for map/bag/home navigation scenario, including validation and deterministic execution checks.

## Project Structure

### Documentation (this feature)

```text
specs/032-per-step-conditions/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
```text
src/
├── GameBot.Domain/
│   ├── Commands/
│   └── Services/
├── GameBot.Service/
│   ├── Endpoints/
│   └── Models/
└── web-ui/
    └── src/

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: Reuse existing backend domain/service and web-ui modules; implement per-step condition behavior as additive changes in sequence models, validation, runner, and sequence authoring UI.

## Complexity Tracking

No constitution violations requiring special justification.

## Phase 6 Verification Outcomes (2026-03-10)

- Full build/test verification:
    - `dotnet build -c Debug` passed.
    - `dotnet test -c Debug --logger trx --results-directory TestResults` passed with `401/401` tests.
- Quality gate verification via `scripts/analyze-test-results.ps1`:
    - `-VerifyCoverage`: passed (touched-file coverage check skipped because no changed `src/*.cs` files in final polish window).
    - `-VerifySecurity`: passed (`dotnet list ... --vulnerable`, `npm audit --omit=dev --audit-level=high`, repository secret scans).
    - `-VerifyLintFormat`: passed (`dotnet format ... --verify-no-changes`; no changed web-ui files required lint run).
    - `-VerifyStaticAnalysis`: passed (analyzer-enabled `dotnet build`).
- Additional feature-specific validations completed:
    - OpenAPI per-step schema assertions (`SequencePerStepConditionsOpenApiTests`) passed.
    - Mixed per-step performance non-regression test (`PerStepConditionPerformanceIntegrationTests`) passed.

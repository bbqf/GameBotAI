# Implementation Plan: Conditional Sequence Steps (Minimal)

**Branch**: `031-sequence-conditional-steps` | **Date**: 2026-03-06 | **Spec**: [specs/031-sequence-conditional-steps/spec.md](specs/031-sequence-conditional-steps/spec.md)
**Input**: Feature specification from `/specs/031-sequence-conditional-steps/spec.md`

## Summary

Introduce minimal conditional sequence-step support with linear execution semantics in a clean-slate dataset. Add `stepType=conditional` with one `imageVisible` condition and a generic action payload (any currently supported action type), where true executes, false skips, and evaluator errors fail-stop sequence execution.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend)  
**Primary Dependencies**: ASP.NET Core Minimal API, existing GameBot.Domain sequence/command services, existing image detection pipeline, existing execution-log services, existing action execution infrastructure  
**Storage**: File-backed JSON repositories under `data/commands/sequences` and image metadata under `data/images`  
**Testing**: xUnit unit/integration/contract suites via `dotnet test -c Debug`  
**Target Platform**: Windows development/runtime profile  
**Project Type**: Web application (ASP.NET Core API + React/Vite authoring UI)  
**Performance Goals**: Added conditional-step evaluation latency p95 <= 200 ms under declared load profile  
**Constraints**: No new external packages; no branch-graph semantics; single condition per conditional step; clean-slate schema only (`stepType` required)  
**Scale/Scope**: One active sequence execution (no concurrency), 30 steps with 10 conditional steps, 15-minute measurement run

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

### Pre-Phase 0 Gate Review

- Code Quality Discipline: PASS — additive and bounded data-model/runtime changes without new dependencies.
- Testing Standards: PASS — plan includes unit/integration/contract validation for conditional semantics, empty-state creation, and action-payload validation.
- UX Consistency: PASS — minimal authoring changes; clear validation and execution outcomes.
- Performance Requirements: PASS — explicit p95 goal and normal-load profile are defined.

### Post-Phase 1 Design Re-check

- Code Quality Discipline: PASS — contracts/data model remain focused on v1 scope.
- Testing Standards: PASS — artifacts define verifiable behavior for true/false/error and deterministic repeat runs.
- UX Consistency: PASS — no extra navigation flows; stable payload shapes.
- Performance Requirements: PASS — quickstart includes profile-based validation procedure.

## Phase 0: Research Output

`research.md` resolves:
- Linear conditional semantics (no branch graph).
- Single condition type (`imageVisible`) in v1.
- Generic action payload support in both unconditional and conditional steps.
- Clean-slate scope assumptions and deterministic execution expectation.

## Phase 1: Design & Contracts Output

- `data-model.md`: sequence/step entities, action/conditional union, validation, and runtime outcome records.
- `contracts/sequence-conditional-steps.openapi.yaml`: sequence CRUD/validate/execute contracts with conditional-step payloads and step-level execution records.
- `quickstart.md`: empty-state authoring flow, runtime behavior checks, logging checks, and performance profile verification.

## Project Structure

### Documentation (this feature)

```text
specs/031-sequence-conditional-steps/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── sequence-conditional-steps.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/
│   └── Services/
├── GameBot.Service/
│   ├── Endpoints/
│   ├── Models/
│   └── Services/
└── web-ui/
    └── src/

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: Extend existing domain/service/web-ui modules and test suites; no new top-level projects.

## Complexity Tracking

No constitution violations requiring justification.

## Phase 6 Verification Outcomes (2026-03-06)

- Added and validated NFR-003 performance coverage via `ConditionalStepPerformanceIntegrationTests` (30 total steps, 10 conditional steps, p95 budget assertion <= 200 ms).
- Updated OpenAPI contract assertions in `SequenceConditionalStepsOpenApiTests` to validate conditional-step schema keys and required `stepType`.
- Extended `scripts/analyze-test-results.ps1` with explicit quality gates:
    - Coverage gate (`-VerifyCoverage`) with touched-runtime-file scope
    - Security gate (`-VerifySecurity`)
    - Lint/format gate (`-VerifyLintFormat`) for changed files
    - Static-analysis gate (`-VerifyStaticAnalysis`)
- Verification commands and outcomes:
    - `dotnet build -c Debug`: passed
    - `dotnet test -c Debug --logger trx --results-directory TestResults`: passed (`384/384`)
    - `scripts/analyze-test-results.ps1 -ResultsDir TestResults -LatestOnly -VerifyCoverage -CoverageFile <TestResults/.../coverage.cobertura.xml> -VerifySecurity -VerifyLintFormat -VerifyStaticAnalysis`: passed

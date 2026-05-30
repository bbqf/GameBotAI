# Implementation Plan: Wait for Image Primitive Action

**Branch**: `001-wait-for-image` | **Date**: 2026-05-27 | **Spec**: [specs/001-wait-for-image/spec.md](specs/001-wait-for-image/spec.md)
**Input**: Feature specification from `/specs/001-wait-for-image/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a new `WaitForImage` primitive step that can be authored in both commands and sequences, pausing execution until an optional image is detected or a timeout elapses while treating timeout and image-unavailable cases as normal completion rather than failures. The design extends the existing command-step, sequence-step, detection, and execution-log models with minimal schema changes by reusing `DetectionTarget`, adding a dedicated wait-step config, and surfacing wait parameters plus exit conditions through existing command, sequence, and execution-log APIs.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (web UI)  
**Primary Dependencies**: ASP.NET Core Minimal API, existing `GameBot.Domain` command, sequence, and logging models, `CommandExecutor`, `SequenceRunner`, `SequenceStepValidationService`, existing detection pipeline (`IReferenceImageStore`, `IScreenSource`, `ITemplateMatcher`), existing web-ui command, sequence, and execution-log APIs  
**Storage**: Existing file-backed JSON command repository under `data/commands`, existing file-backed JSON sequence repository under `data/commands/sequences`, and existing file-backed execution-log repository under `data/execution-logs`; no new persistence store  
**Testing**: xUnit unit/integration tests for command executor behavior, sequence runner behavior, command and sequence validation/mapping, and execution-log persistence; existing web UI/service tests for DTO shape; touched-area coverage target remains >=80% line and >=70% branch  
**Target Platform**: Windows development/runtime baseline for image-backed execution  
**Project Type**: Web application (ASP.NET Core API + React authoring/execution UI)  
**Performance Goals**: command create/update validation p95 < 200 ms; each wait polling cycle adds no more than one detection attempt per configured capture interval; wait-step logging adds < 25 ms p95 per execution; execution-log detail retrieval p95 < 300 ms for default page size  
**Constraints**: no new external packages; timeout default is 1000 ms; timeout and image-unavailable outcomes are non-error completions; optional image must be supported end-to-end; reuse existing default detection certainty when certainty is omitted; do not add a wait-specific cancellation or termination exit condition; preserve current command and sequence endpoint shapes by additive changes only  
**Scale/Scope**: one new wait primitive across command and sequence flows in `GameBot.Domain`, `GameBot.Service`, and `src/web-ui`; additive DTO/OpenAPI changes for command CRUD/execute, sequence CRUD/execute, and execution-log detail; unit/integration/contract coverage for backend plus affected web UI service/form flows

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

### Pre-Phase 0 Gate Review

- Code Quality: PASS — planned changes extend existing command-step, DTO, executor, and execution-log boundaries without introducing a new subsystem or external dependency.
- Testing: PASS — verified on 2026-05-27 with `dotnet build -c Debug`, `dotnet test -c Debug --logger trx`, and `scripts/analyze-test-results.ps1`; no locked-binary failure reproduced and all recorded tests passed.
- UX Consistency: PASS — command and sequence authoring, timeout semantics, and execution-log messages remain additive and align with current image-selection and execution-history flows.
- Performance: PASS — explicit budgets are defined for validation, polling cadence, logging overhead, and log retrieval latency.

### Post-Phase 1 Design Re-check

- Code Quality: PASS — design reuses `DetectionTarget`, adds a focused `WaitForImageConfig`, and keeps orchestration in the existing `CommandExecutor` / endpoint / logging pipeline.
- Testing: PASS — the repo-wide gate is currently green after rerunning `dotnet build -c Debug`, `dotnet test -c Debug --logger trx`, and `scripts/analyze-test-results.ps1` on 2026-05-27; the required unit, integration, contract, and UI verification work remains part of implementation scope.
- UX Consistency: PASS — contracts keep existing endpoints and add a single new step type plus structured wait outcome details without removing existing fields.
- Performance: PASS — wait polling is bounded by the existing capture interval, introduces no new background worker, and uses append-style execution logging with additive detail attributes only.

## Phase 0: Research Output

`research.md` resolves:
- Reuse of `DetectionTarget` instead of inventing a new image-target model.
- Minimal command-step, sequence-step, and DTO changes needed for a dedicated `WaitForImage` step type.
- Execution outcome and execution-log extension strategy for wait-specific exit conditions.
- Authoring UI component reuse for optional image, optional certainty, and timeout editing in commands and sequences.
- API surfaces and test suites that need additive updates across commands, sequences, and execution logs.

## Phase 1: Design & Contracts Output

- `data-model.md`: wait-step config, command-step and sequence-step extensions, execution outcome semantics, and execution-log detail attributes.
- `contracts/wait-for-image.openapi.yaml`: additive command CRUD/execute, sequence CRUD/execute, and execution-log detail contract updates for the new wait step.
- `quickstart.md`: authoring, execution, and logging verification flow for detection success, timeout, image-unavailable, and command/sequence authoring paths.

## Project Structure

### Documentation (this feature)

```text
specs/001-wait-for-image/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── wait-for-image.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/                    # CommandStep and SequenceStep additions plus wait-step config
│   ├── Logging/                     # Execution log models/outcome mapping
│   └── Services/                    # SequenceRunner and sequence validation support for wait steps
├── GameBot.Service/
│   ├── Endpoints/                   # Command and sequence CRUD validation/mapping plus execution-log detail projection
│   ├── Models/                      # Command, sequence, and execution-log DTO additions
│   ├── Services/                    # CommandExecutor wait orchestration and execution-log service mapping
│   └── Swagger/                     # OpenAPI/Swagger schema examples for the new step type
└── web-ui/
    └── src/
        ├── components/commands/     # Wait-step authoring controls in CommandForm
        ├── pages/                   # Execution log detail rendering
        └── services/                # Command/sequence/execution-log DTO updates

tests/
├── contract/                        # Command, sequence, and execution-log API compatibility checks
├── integration/                     # Command and sequence authoring/execution/logging flows
└── unit/                            # Executor, sequence runner, validation, and mapping behavior
```

**Structure Decision**: Extend the existing backend and web UI modules in place; no new top-level projects, repositories, or services are required.

## Complexity Tracking

No constitution violations require justification. The prior local build/test blocker no longer reproduces after successful 2026-05-27 verification; implementation may proceed while continuing to enforce the documented gate.

## Performance Verification Notes

- Verification target for implementation: keep rerunning `dotnet build -c Debug`, `dotnet test -c Debug --logger trx`, and `scripts/analyze-test-results.ps1` after each substantive implementation slice.
- Wait polling should be validated against the existing capture interval to ensure no more than one detection attempt per cycle in both command and sequence execution paths.
- Execution-log verification should confirm wait-step detail attributes are additive and do not regress log list/detail latency beyond the declared p95 budgets.

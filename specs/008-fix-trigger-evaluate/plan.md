# Implementation Plan: Evaluate-And-Execute Trigger Guard

**Branch**: `001-fix-trigger-evaluate` | **Date**: 2025-11-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-fix-trigger-evaluate/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Ensure `EvaluateAndExecute` always evaluates the command’s trigger before any action dispatch, persists satisfied-state metadata up front, and adds unit coverage for satisfied vs pending flows. The implementation relies on the existing `TriggerEvaluationService`, reinforces repository updates before invoking `ForceExecuteAsync`, and introduces lightweight fakes for deterministic unit tests alongside existing integration coverage.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# / .NET 9  
**Primary Dependencies**: GameBot.Domain repositories, GameBot.Emulator.Session, TriggerEvaluationService (no new external packages)  
**Storage**: File-based JSON repositories under `data/`  
**Testing**: xUnit + FluentAssertions, new unit fixture for `CommandExecutor` plus existing integration suite  
**Target Platform**: Windows (development) + CI runners  
**Project Type**: ASP.NET Core Minimal API backend  
**Performance Goals**: Evaluate & Execute endpoint should complete trigger evaluation + decision in <100 ms median, <250 ms p95 under single-session load  
**Constraints**: Must avoid touching ADB/emulator in unit tests; preserve deterministic behavior with no external timeouts; maintain compatibility with existing API contract  
**Scale/Scope**: Changes limited to `CommandExecutor`, logging/telemetry hooks, and tests within `tests/unit` + `tests/integration`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- Code Quality: planned tooling (lint/format/static analysis), modularity approach, security scanning.
- Testing: unit/integration plan, coverage targets, determinism strategy, CI gating.
- UX Consistency: interface conventions (CLI/API/logs), error messaging, versioning for changes.
- Performance: declared budgets/targets and measurement approach for hot paths.

**Code Quality**: Work lives in existing service modules; follow current logging + repository abstractions; rely on dotnet-format + analyzers already enforced in CI. No new dependencies or global state.

**Testing**: Add failing unit tests covering satisfied/pending triggers before fixes; keep integration tests green; ensure coverage goals (≥80% line / 70% branch for touched files) by instrumenting new tests.

**UX Consistency**: API response schema unchanged; add clearer log/telemetry messages (“trigger pending, skipped execution”) to assist operators without altering client contracts.

**Performance**: Trigger evaluation and repository persistence must remain O(1) on JSON stores; plan introduces no additional network hops. Logging additions are single-line events protected by gating. No constitution violations identified at this stage, and post-design review still meets all gates.

## Project Structure

### Documentation (this feature)

```text
specs/001-fix-trigger-evaluate/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── command-evaluate-and-execute.openapi.yaml
└── (tasks.md planned for /speckit.tasks)
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
├── GameBot.Domain/
│   ├── Actions/
│   ├── Commands/
│   ├── Triggers/
│   └── Services/
├── GameBot.Service/
│   ├── Endpoints/
│   ├── Logging/
│   ├── Services/
│   └── Program.cs
└── GameBot.Emulator/

tests/
├── unit/
│   ├── Logging/
│   └── (new) Commands/CommandExecutorTests.cs
├── integration/
│   └── CommandEvaluateAndExecuteTests.cs
└── contract/
```

**Structure Decision**: Single solution with shared domain + service projects. Feature touches `src/GameBot.Service/Services/CommandExecutor.cs` and adds a new unit-test fixture under `tests/unit/Commands`. Existing integration tests under `tests/integration` confirm end-to-end behavior; no new projects required.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _None_ |  |  |

## Implementation Updates (2025-11-26)

- `EvaluateAndExecute` now emits `triggerStatus` and `message` fields so API consumers can differentiate satisfied, pending, cooldown, and disabled outcomes without parsing logs.
- Logger helpers (`TriggerExecuted`, `TriggerSkipped`, `TriggerBypassed`) were moved into an `internal static partial` class so the source generator can produce implementations invoked across the service.
- Integration coverage now validates both the HTTP response and the persisted trigger state via `GET /triggers/{id}`, ensuring `lastFiredAt` and `lastResult` are recorded (or preserved) for satisfied, pending, cooldown, and disabled flows.
- Deterministic `CommandExecutor` unit tests were added under `tests/unit/Commands/CommandExecutorTests.cs`, using in-memory fakes to guarantee trigger metadata is updated before emulator input dispatch occurs.

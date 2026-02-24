# Implementation Plan: Primitive Tap in Commands

**Branch**: `027-add-primitive-tap-action` | **Date**: 2026-02-24 | **Spec**: [specs/027-add-primitive-tap-action/spec.md](specs/027-add-primitive-tap-action/spec.md)
**Input**: Feature specification from `/specs/027-add-primitive-tap-action/spec.md`

## Summary

Add a new primitive tap command step that behaves like inline `tap(0,0)` with detection-driven coordinate resolution, without requiring an explicit action entity. Primitive tap executes only when detection succeeds, is skipped for detection failure or out-of-bounds coordinates, and selection uses highest-confidence match. Existing action-based command behavior remains backward compatible.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (authoring UI)

**Primary Dependencies**: ASP.NET Core Minimal API, `GameBot.Domain` command/action repositories, `CommandExecutor`, existing detection pipeline (`IReferenceImageStore`, `IScreenSource`, `ITemplateMatcher`), existing web-ui authoring APIs.

**Storage**: Existing file-backed JSON command repository under `data/commands`; no new persistence store.

**Testing**: xUnit unit/integration tests for command serialization, endpoint validation, and executor behavior; existing contract tests for API shape; enforce touched-area coverage minimums of >=80% line and >=70% branch.

**Target Platform**: Windows runtime/development for detection-backed execution, with non-Windows graceful handling preserved.

**Project Type**: Web application (ASP.NET Core API + React authoring UI).

**Performance Goals**:
- Command create/update validation p95 < 200 ms for typical payload sizes.
- Primitive tap detection resolution adds < 150 ms p95 per primitive step when screenshot/template are available.
- No measurable regression (>2%) in force-execute throughput for commands without primitive tap steps.

**Constraints**:
- No new external packages.
- Maintain compatibility for existing `Action` and `Command` step types.
- Primitive tap must require detection at save/validation time.
- Out-of-bounds computed coordinates must skip tap and record `skipped/invalid-target`.

**Scale/Scope**:
- Command authoring and execution paths in `GameBot.Service` and `web-ui`.
- DTO/domain model updates for one new step type and per-step execution outcome.
- Test updates in `tests/unit`, `tests/integration`, and `tests/contract` for touched API and executor behavior.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Review

- Code Quality: PASS — reuse existing endpoint/model/executor architecture; no new dependencies; preserve cohesive logic boundaries.
- Testing: PASS — plan includes unit + integration + contract coverage for new step type and failure modes.
- UX Consistency: PASS — API error semantics follow existing actionable error format; no breaking removal of existing fields.
- Performance: PASS — explicit p95 and regression budgets are defined and will be verified in test/perf notes.

### Post-Phase 1 Design Re-check

- Code Quality: PASS — design keeps primitive tap as a first-class command step with explicit validation rules.
- Testing: PASS — data model and contract artifacts define deterministic expected outcomes for success/skip conditions.
- UX Consistency: PASS — contracts preserve existing endpoints while extending request/response shape in backward-compatible form.
- Performance: PASS — detection selection and out-of-bounds handling are constant-time after match computation; no additional polling/background workloads introduced; verification tasks include p95 and regression checks against declared budgets.

## Phase 0: Research Output

`research.md` resolves design choices for:
- Primitive step representation versus synthetic action creation.
- Validation-time rejection for missing detection.
- Out-of-bounds and multi-match behavior.
- Outcome reporting strategy for primitive tap execution.

## Phase 1: Design & Contracts Output

- `data-model.md`: updated command step domain/API models, validation rules, and execution outcome entity.
- `contracts/primitive-tap.openapi.yaml`: API contract changes for command create/update/get/list and execute endpoints.
- `quickstart.md`: end-to-end authoring/execution verification flow and test commands.

## Project Structure

### Documentation (this feature)

```text
specs/027-add-primitive-tap-action/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── primitive-tap.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Commands/                 # CommandStep type additions and validation rules
├── GameBot.Service/
│   ├── Models/                   # Command DTO updates
│   ├── Endpoints/                # Command create/update/list/get contract handling
│   └── Services/                 # Primitive tap execution behavior and outcome reporting
└── web-ui/                       # Command authoring support for primitive tap step

tests/
├── unit/                         # Domain/service unit tests
├── integration/                  # API + execution behavior integration tests
└── contract/                     # API shape and compatibility tests
```

**Structure Decision**: Extend the existing backend + web-ui structure; no new top-level projects or services.

## Complexity Tracking

No constitution violations requiring justification.

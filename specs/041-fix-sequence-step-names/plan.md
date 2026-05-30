# Implementation Plan: Preserve Sequence Step Command Names

**Branch**: `001-fix-sequence-step-names` | **Date**: 2026-05-29 | **Spec**: `C:\src\GameBot\specs\001-fix-sequence-step-names\spec.md`
**Input**: Feature specification from `C:\src\GameBot\specs\001-fix-sequence-step-names\spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Repair sequence step round-tripping so saved sequences preserve each step's command association and human-readable identity across backend persistence, authoring UI reload, and execution-log rendering. The design standardizes on the existing per-step sequence payload, adds command-name snapshot support for unresolved references, and enriches sequence execution logs with both the step label and command name.

## Technical Context

**Language/Version**: Backend C# 13 / .NET 9, Frontend TypeScript ES2020 / React 18  
**Primary Dependencies**: ASP.NET Core Minimal API, existing `GameBot.Domain` sequence repository and runner, existing execution-log service, React/Vite authoring UI, existing command repository contracts  
**Storage**: File-backed JSON under `data/commands/sequences`, `data/commands`, and `data/execution-logs`  
**Testing**: `dotnet build -c Debug`; `dotnet test -c Debug --logger trx`; existing contract, integration, unit, and web UI Jest suites for sequence authoring and execution logs  
**Target Platform**: Windows-hosted backend service and desktop browser authoring UI  
**Project Type**: Web app (backend + frontend in one repository)  
**Performance Goals**: No observable regression for sequence create/load/save or execution-log detail rendering; local validation keeps sequence authoring and log-detail operations within existing interactive expectations for sequences up to 50 steps  
**Constraints**: Preserve backward compatibility for valid saved per-step sequences, distinguish unresolved command references from intentionally blank steps, keep execution-log wording aligned with authoring labels, and avoid introducing new persistence stores or packages  
**Scale/Scope**: One bug-fix slice spanning sequence API contract handling, file-backed sequence persistence shape, authoring UI mapping, execution-log detail shaping, and targeted regression tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

### Pre-Design Gate Review

- **Code Quality Discipline**: PASS
  - Scope is limited to existing backend/domain/frontend surfaces already responsible for sequence serialization and execution-log detail projection.
  - No new projects, frameworks, or persistence technologies are introduced.
- **Testing Standards**: PASS
  - Existing regression suites already cover per-step sequence round-trips and execution-log projections; the plan adds bug-specific contract/integration/UI coverage for the missing step-command association and log naming behavior.
  - Build and tests were executed successfully on 2026-05-29 after clearing an environment process lock.
- **User Experience Consistency**: PASS
  - The design preserves current authoring routes and deep links while replacing ambiguous blank states with explicit unresolved states.
  - Execution logs become more readable without changing the overall execution-log information architecture.
- **Performance Requirements**: PASS
  - This is a bounded bug fix with no new hot path; quickstart validation includes a regression check for sequence load/save and log detail rendering on realistic step counts.

### Post-Design Gate Review

- **Code Quality Discipline**: PASS (design keeps serialization, UI mapping, and log enrichment isolated to their owning modules).
- **Testing Standards**: PASS (research, data model, and contracts identify the exact regression checks required before implementation is complete).
- **User Experience Consistency**: PASS (design explicitly defines unresolved-reference UI behavior and log wording alignment).
- **Performance Requirements**: PASS (no new dependency or storage cost is introduced; validation remains focused on existing interactive paths).

## Project Structure

### Documentation (this feature)

```text
specs/001-fix-sequence-step-names/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── sequence-step-roundtrip.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Commands/              # Sequence models and file repository persistence
├── GameBot.Service/
│   ├── Program.cs             # Sequence endpoints and execution entrypoints
│   ├── Endpoints/             # Execution log response shaping
│   └── Services/ExecutionLog/ # Sequence execution detail projection
└── web-ui/
    └── src/
        ├── pages/             # Sequence authoring UI
        ├── services/          # Sequence and execution-log DTOs
        ├── lib/               # Sequence mapping helpers
        └── types/             # Sequence per-step request/response types

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: Keep the fix within existing domain, service, web UI, and test projects. The behavior is controlled by existing sequence and execution-log abstractions, so no structural expansion is required.

## Phase 0: Research Plan

- Confirm the canonical persisted sequence-step shape for authoring round-trips and reject reliance on legacy `string[]` step serialization for this workflow.
- Define how unresolved command references preserve user-visible identity without inventing replacement commands.
- Define the minimum execution-log context required to correlate a sequence step with the selected command in the authoring UI.
- Define the regression-test strategy across API, persistence, UI reload, and execution-log detail projection.

## Phase 1: Design Plan

- Produce `data-model.md` for persisted sequence steps, command reference snapshots, unresolved reference presentation, and enriched execution-log outcomes.
- Produce `contracts/sequence-step-roundtrip.openapi.yaml` for sequence create/get/update and execution-log detail behavior relevant to this bug fix.
- Produce `quickstart.md` covering reproduction, verification, unresolved-reference validation, and regression test execution.
- Update Copilot agent context via `.specify/scripts/powershell/update-agent-context.ps1 -AgentType copilot`.

## Phase 2: Task Planning Approach (for `/speckit.tasks`)

- Split implementation into small vertical slices:
  1. Backend sequence contract and persistence alignment for per-step command references and snapshots.
  2. Frontend authoring load/save mapping and unresolved-reference display behavior.
  3. Sequence execution-log enrichment with step label + command name context.
  4. Regression tests across contract, integration, unit, and UI layers.
- Include explicit constitution gate tasks:
  - Re-run build and all tests.
  - Verify touched-area coverage stays at or above repository baseline expectations.

## Complexity Tracking

No constitution violations identified; no complexity exemptions required.

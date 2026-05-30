# Implementation Plan: API Structure Cleanup

**Branch**: `[018-api-refactor]` | **Date**: 2025-12-28 | **Spec**: [specs/018-api-refactor/spec.md](specs/018-api-refactor/spec.md)
**Input**: Feature specification from `/specs/018-api-refactor/spec.md`

## Summary

Consolidate all public routes under a single `/api/{resource}` base, remove duplicate legacy endpoints, and reorganize Swagger to group endpoints by domain (actions, sequences, sessions/emulator, configuration) with schemas and example payloads. Update automated tests to target only canonical routes and ensure legacy paths return clear non-success responses.

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: ASP.NET Core Minimal API, Swashbuckle/Swagger tooling, existing GameBot.Domain + Emulator services  
**Storage**: File-backed JSON repositories under `data/` (no new stores)  
**Testing**: xUnit + integration/contract tests; coverlet for coverage enforcement  
**Target Platform**: Windows (service)  
**Project Type**: ASP.NET Core service with web UI alongside  
**Performance Goals**: Maintain current latency budgets; target <300ms p95 for API metadata/documentation responses and no added overhead on main endpoints  
**Constraints**: No new external packages; keep Windows-only ADB/system drawing guards intact; preserve existing response shapes while changing base paths  
**Scale/Scope**: Moderate API surface (actions, sequences, emulator/session, configuration); scope limited to routing and documentation alignment

## Constitution Check

Pre-design gate (pass):
- Code Quality: No new dependencies; keep modular routing configuration by domain; adhere to logging/error guidance; secrets remain out of repo.
- Testing: Plan to update contract/integration tests to canonical `/api` routes; maintain coverage with existing xUnit suites and coverlet thresholds.
- UX Consistency: Enforce single base path; legacy paths return actionable message; Swagger grouping and summaries kept consistent.
- Performance: Routing changes only; ensure no additional middleware overhead; metadata responses stay under stated p95 target.

Post-design gate: Re-validated after Phase 1 artifacts; no new violations introduced.

## Project Structure

### Documentation (this feature)

```text
specs/018-api-refactor/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (future)
```

### Source Code (repository root)

```text
src/
├── GameBot.Service/      # ASP.NET Core minimal API and Swagger
├── GameBot.Domain/       # Domain models and repositories
├── GameBot.Emulator/     # Emulator/session handling
└── web-ui/               # React UI (not in scope for routing change)

tests/
├── unit/
├── integration/
├── contract/
└── TestAssets/
```

**Structure Decision**: Single backend service with domain and emulator projects; routing and Swagger updates land in GameBot.Service with accompanying test updates in integration/contract suites.

## Complexity Tracking

No constitution violations or added complexity requiring justification.

## Phase 0: Research Summary

- Decision: Canonical base path `/api/{resource}` only; legacy roots (e.g., `/actions`) return 404/410-style non-success with message pointing to `/api/...` (no redirects).
  - Rationale: Eliminates ambiguity and enforces contract; avoids caching side-effects of redirects.
  - Alternatives: Allow dual paths temporarily (rejected: duplicates risk regressions and test drift).
- Decision: Swagger groups by domain tags: Actions, Sequences, Sessions/Emulator, Configuration, Triggers/Detection (if present).
  - Rationale: Mirrors spec requirements and improves discoverability.
  - Alternatives: Flat list (rejected: hard to navigate as surface grows).
- Decision: Each endpoint documents request/response schemas with at least one concrete example payload.
  - Rationale: Reduces consumer errors and supports contract tests.
  - Alternatives: Schemas only without examples (rejected: poorer onboarding).
- Decision: Tests updated to call canonical routes only; legacy route tests removed or assert non-success pointing to `/api`.
  - Rationale: Keeps test suite aligned to contract and prevents silent drift.

## Phase 1: Design & Contracts

- Data Model: Resource groups and endpoint catalog captured for routing and documentation alignment (see data-model.md).
- API Contracts: Canonical route map with expected tags, example payloads, and legacy handling guidance (see contracts/route-contracts.md).
- Quickstart: Run/build/test steps plus verification checklist (see quickstart.md).
- Agent Context: Updated via `update-agent-context.ps1 -AgentType copilot` with language/framework/storage context; no new technologies added beyond existing stack.

## Post-Design Constitution Check

- Code Quality: Plan uses existing stack and modular routing per domain; no new dependencies; documentation clarity improved.
- Testing: Contract/integration suites to be updated to canonical routes; coverage expectations unchanged.
- UX Consistency: Single base path and grouped Swagger tags; actionable legacy responses.
- Performance: No new hot-path cost; metadata targets retained (<300ms p95 for docs endpoints).

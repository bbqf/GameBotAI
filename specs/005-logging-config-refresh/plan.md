
# Implementation Plan: Logging Config Refresh (004)

**Branch**: `004-logging-config-refresh` | **Date**: 2025-11-24 | **Spec**: `specs/004-logging-config-refresh/spec.md`
**Input**: Feature specification from `/specs/004-logging-config-refresh/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Expose runtime-refreshable logging controls so operators can adjust global and per-category verbosity without restarting GameBot.Service. Changes must immediately influence both Microsoft.Extensions.Logging and Serilog output, enforce the timestamp/level/source log format, and surface via authenticated API endpoints plus persisted JSON config.

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: ASP.NET Core Minimal APIs, Microsoft.Extensions.Logging, Serilog (console + JSON sinks)  
**Storage**: File-based JSON (`data/config/config.json`) for persisted logging settings; in-memory level switch registry  
**Testing**: xUnit unit + integration tests via `WebApplicationFactory<Program>`; contract tests validate OpenAPI  
**Target Platform**: Windows + Linux containers (service currently Windows-focused, needs parity)  
**Project Type**: ASP.NET Core background + HTTP API service  
**Performance Goals**: Logging reconfiguration <200 ms, steady-state logging overhead <5% CPU at 2k log events/sec (confirmed with ops research)  
**Constraints**: Authenticated endpoints only; no service restart; deterministic log template `[Timestamp Level] [SourceContext] Message`  
**Scale/Scope**: Single service instance per emulator host; expect ≤10 simultaneous reconfiguration requests but thousands of log events/minute

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Code Quality**: Changes confined to `GameBot.Service` logging pipeline with isolated services (e.g., `DynamicLoggingConfigService`). Static analysis (Roslyn analyzers) and formatting enforced by `dotnet format`. Security review ensures no secret leakage in logs or config endpoints.
- **Testing**: Add deterministic unit tests for level switch registry + config parsing, integration tests covering auth/POST `/api/logging/config`, and contract tests for OpenAPI schema updates. Coverage must stay ≥80% in touched areas; tests must stub auth token to avoid flakiness.
- **UX Consistency**: API follows existing `/api/*` naming, returns actionable JSON (success + error). Log format mandated and documented in quickstart. Any breaking changes (e.g., new config section) documented with migration steps.
- **Performance**: Capture perf note describing baseline log throughput and ensure level switch updates reuse existing sinks to avoid allocations. Target <200 ms reconfiguration and verify via benchmark or instrumentation in tests.

**Post-Phase-1 Re-check**: Design artifacts (research/data-model/contracts/quickstart) satisfy all four principles; no waivers required.

## Project Structure

### Documentation (this feature)

```text
specs/004-logging-config-refresh/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md (Phase 2 via /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
├── GameBot.Emulator/
└── GameBot.Service/

tests/
├── contract/
├── integration/
└── unit/

data/
└── config/
    └── config.json (logging + service settings)
```

**Structure Decision**: All logging configuration logic and endpoints live under `src/GameBot.Service` (Services, Endpoints, Models). Persistence continues to use existing `data/config/config.json`. Tests remain in the corresponding `tests/integration` and `tests/unit` projects to maximize reuse of infrastructure fixtures.

## Complexity Tracking

No waivers requested; planned architecture fits existing service boundaries.

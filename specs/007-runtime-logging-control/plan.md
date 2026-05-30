# Implementation Plan: Runtime Logging Control

**Branch**: `001-runtime-logging-control` | **Date**: 2025-11-25 | **Spec**: [specs/001-runtime-logging-control/spec.md](spec.md)
**Input**: Feature specification from `/specs/001-runtime-logging-control/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Expose a REST-configurable logging policy service that lets operators raise/lower levels and enable/disable specific components (e.g., `GameBot.Domain.Triggers`, `Microsoft.AspNetCore`) at runtime. All components default to Warning, changes become effective within seconds without restarts, and overrides persist plus surface through a GET endpoint alongside auditing.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: ASP.NET Core Minimal API, Microsoft.Extensions.Logging configuration pipeline, existing JSON config repository  
**Storage**: Reuse file-based JSON configuration persisted under `data/config` (no new store)  
**Testing**: xUnit unit tests plus integration tests hitting config endpoint (GameBot.Service)  
**Target Platform**: Windows (Dev) / Linux containers (CI)  
**Project Type**: ASP.NET Core Minimal API service  
**Performance Goals**: Level changes observed within 5 seconds 95th percentile; full reset to defaults confirmed <10 seconds  
**Constraints**: No application restarts allowed for level toggles; configuration updates must be atomic and audit logged; authorization enforced on config endpoints  
**Scale/Scope**: <20 named logging components per environment; tens of concurrent administration calls

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- **Code Quality**: Rely on existing analyzers, Serilog sinks, and modular services. New runtime policy manager will expose <50 LOC methods, reuse dependency injection, and continue secret scanning with zero new packages.
- **Testing**: Commit to unit tests for policy persistence/authorization plus integration tests invoking `/config/logging` to prove deterministic, ≥80% line coverage on affected files; include regression test for toggle latency.
- **UX Consistency**: Extend existing config endpoint; responses mirror current API conventions with actionable errors and no sensitive data leakage; document behavior in quickstart/spec.
- **Performance**: Success metrics provide 5s/10s latency targets. Integration tests will measure propagation delay and fail if budgets exceeded.

Gate status: **PASS** (no blocking issues identified).
 
## Project Structure
```text
src/
├── GameBot.Domain/              # Logging evaluators + configuration services
├── GameBot.Service/             # ASP.NET Core API (config endpoint adjustments)
└── GameBot.Emulator/

tests/
├── unit/                        # xUnit coverage for domain logic
├── integration/                 # API + logging pipeline tests
└── contract/                    # OpenAPI and platform contracts

specs/001-runtime-logging-control/
├── spec.md
├── plan.md
└── (research/data-model/contracts/quickstart to be generated)
```

**Structure Decision**: Extend existing single-service layout: `GameBot.Service` hosts new config endpoints, `GameBot.Domain` holds persistence + policies, tests live under existing `tests/unit` and `tests/integration` suites.
## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _None_ | — | — |

## Constitution Re-check (Post-Design)

- **Code Quality**: Data model + contracts keep schema lean; no new dependencies introduced beyond existing logging pipeline. Plan enforces audit metadata and modular services.
- **Testing**: Quickstart + plan call for unit/integration suites filtered via `RuntimeLoggingControl`, meeting ≥80% coverage expectation.
- **UX Consistency**: OpenAPI contract places endpoints under `/config/logging`, response/error formats align with current API shape, and quickstart documents consumer steps.
- **Performance**: Research + design emphasize immediate propagation (ILogger rule switches) with explicit 5s/10s SLAs; reset endpoint ensures deterministic last-write wins.

Result: **PASS** — ready to proceed to `/speckit.tasks`.

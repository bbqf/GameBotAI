# Implementation Plan: Persisted Execution Log

**Branch**: `028-execution-log` | **Date**: 2026-02-27 | **Spec**: [specs/028-execution-log/spec.md](specs/028-execution-log/spec.md)
**Input**: Feature specification from `/specs/028-execution-log/spec.md`

## Summary

Introduce durable backend execution logging for command and sequence runs with end-user oriented entries that capture timestamp, identifiable object reference, hierarchy context, final status (`success`/`failure`), per-step outcome (`executed`/`not_executed`), concise outcome detail, masked sensitive values, and relative navigation context for future web UI linking. The feature adds persistence, retrieval contracts, and configurable retention policy without requiring immediate UI implementation.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (service/domain), TypeScript ES2020 / React 18 (future consumer only)

**Primary Dependencies**: ASP.NET Core Minimal API, existing `GameBot.Domain` command/sequence services, file-backed repositories under `data/`, existing config/logging policy infrastructure.

**Storage**: File-backed JSON execution-log repository under `data/execution-logs` with retention-driven cleanup; retention configuration persisted in existing config store.

**Testing**: xUnit unit tests for event-to-log mapping and masking rules, integration tests for execution endpoint logging and retrieval endpoints, contract tests for new API shapes; touched-area coverage >=80% line and >=70% branch.

**Target Platform**: Windows development/runtime baseline.

**Project Type**: Web application backend API with future web-ui consumption.

**Performance Goals**:
- Additional logging overhead per command/sequence execution p95 < 25 ms.
- Log query endpoint p95 < 300 ms for default page size with typical local dataset.
- Cleanup execution (retention purge) should not block command execution request path.

**Constraints**:
- Preserve existing command/sequence execution behavior and response compatibility.
- Keep end-user log entries concise and avoid developer-only trace jargon.
- Store only masked/redacted sensitive values.
- Use relative navigation paths only (host/port agnostic).
- Keep retention period configurable.

**Scale/Scope**:
- Execution coverage: all command and sequence execution attempts, including skipped/not executed steps.
- Data volume target: up to 100k retained log entries per environment with paged retrieval.
- Scope limited to backend persistence + retrieval readiness; web-ui rendering deferred.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Review

- Code Quality: PASS — planned implementation reuses existing endpoint/service/repository patterns, introduces focused domain entities, and avoids new external dependencies.
- Testing: PASS — unit/integration/contract tests planned with deterministic fixtures and touched-area coverage targets meeting constitution thresholds.
- UX Consistency: PASS — logs and API responses remain actionable, stable, and user-oriented; sensitive data masking is explicit.
- Performance: PASS — measurable latency budgets and non-blocking cleanup strategy are defined.
- Performance measurement approach: capture p95 write/query timings during verification and record evidence in feature docs.

### Post-Phase 1 Design Re-check

- Code Quality: PASS — data model and contracts preserve cohesive boundaries (service layer owns log write orchestration; domain services expose execution context signals; repository persists snapshots; endpoints read/query).
- Testing: PASS — artifacts define testable status/outcome semantics, hierarchy linking, and masking behavior with deterministic acceptance checks.
- UX Consistency: PASS — contract fields explicitly support object identification, relative navigation context, and concise user-facing outcome summaries.
- Performance: PASS — design uses append-oriented writes and paginated reads; retention cleanup is decoupled from execution path.

## Phase 0: Research Output

`research.md` resolves:
- Canonical execution log schema for command/sequence + step outcomes.
- Relative navigation path strategy for nested objects.
- Retention and cleanup policy configuration approach.
- Sensitive-value masking/redaction policy and default behavior.
- Retrieval/query contract shape for backend-only phase.

## Phase 1: Design & Contracts Output

- `data-model.md`: execution log domain entities, validation rules, and lifecycle/cleanup states.
- `contracts/execution-log.openapi.yaml`: create-by-execution, list/filter, detail retrieval, and retention configuration contract additions.
- `quickstart.md`: backend verification flow for successful run, skipped run, hierarchy linking, masking, and retention config.

## Project Structure

### Documentation (this feature)

```text
specs/028-execution-log/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── execution-log.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/                    # Existing command/sequence domain models
│   ├── Services/                    # Execution context signals consumed by service layer
│   └── Logging/                     # Existing logging policy models/services
└── GameBot.Service/
    ├── Endpoints/                   # Commands/Sequences/ExecutionLog endpoints
    ├── Models/                      # DTOs for execution log query/response
    └── Services/                    # Execution log persistence and masking service

tests/
├── unit/                            # Mapping, masking, retention rule tests
├── integration/                     # Execution + persistence + query flows
└── contract/                        # API contract shape and backward compatibility
```

**Structure Decision**: Extend existing `GameBot.Domain` and `GameBot.Service` modules with an execution-log slice; no new top-level project or storage technology.

## Complexity Tracking

No constitution violations requiring justification.

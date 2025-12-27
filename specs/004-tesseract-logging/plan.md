# Implementation Plan: Tesseract Logging & Coverage

**Branch**: `001-tesseract-logging` | **Date**: 2025-11-24 | **Spec**: `specs/001-tesseract-logging/spec.md`
**Input**: Feature specification from `/specs/001-tesseract-logging/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Improve observability and quality of the OCR subsystem by (1) emitting structured debug-level logs for every Tesseract process invocation (including command, sanitized arguments, stdout/stderr, duration, and exit code) and (2) raising deterministic test coverage of the Tesseract integration namespace to ≥70% line coverage with clear reporting for stakeholders.

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: Tesseract CLI, System.Diagnostics.Process, Microsoft.Extensions.Logging, Serilog, xUnit + coverlet for coverage enforcement  
**Storage**: Local filesystem temp directories for OCR I/O; log output routed to existing sinks (console/Application Insights). No new persistence.  
**Testing**: xUnit unit tests plus targeted integration tests under `tests/integration/TextOcrTesseractTests.cs`; coverage gathered with `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura`.  
**Target Platform**: Windows Server 2022 (primary), Linux containers for CI runners; must keep cross-platform CLI invocation working.  
**Project Type**: ASP.NET Core service plus domain libraries invoked by emulator triggers.  
**Performance Goals**: Logging instrumentation must add <5 ms overhead per Tesseract call and <10 KB per log entry; coverage test execution should finish within 4 minutes in CI.  
**Constraints**: Redact sensitive CLI args/env vars before logging; capture stdout/stderr asynchronously with an 8 KB per-stream truncation budget (flagging truncation in logs); ensure logging respects runtime log-level toggles; coverage enforcement must not require proprietary tooling.  
**Scale/Scope**: Expect up to 200 concurrent OCR invocations per host and nightly coverage runs; impacts `GameBot.Domain.Triggers.Evaluators`, `GameBot.Service` logging pipeline, and `tests/*` suites.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- Code Quality: planned tooling (lint/format/static analysis), modularity approach, security scanning.
- Testing: unit/integration plan, coverage targets, determinism strategy, CI gating.
- UX Consistency: interface conventions (CLI/API/logs), error messaging, versioning for changes.
- Performance: declared budgets/targets and measurement approach for hot paths.

- **Code Quality**: Plan introduces dedicated logging helper + redaction utilities to keep functions under 50 LOC, adds analyzers for unsafe process handling, and ensures no new dependencies without justification.
- **Testing**: Commitments include deterministic runners/mocks, coverage gate at ≥70% lines for Tesseract namespace, and new regression tests for success, failure, timeout, and malformed output scenarios.
- **UX Consistency**: Logging follows existing structured template conventions, includes actionable error hints, and documents log usage in quickstart.
- **Performance**: Define budgets for logging overhead (<5 ms, <10 KB) and coverage runtime (<4 min) with instrumentation hooks to confirm compliance.

[This section is validated against `.specify/memory/constitution.md` during planning.]

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
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
│   └── Triggers/Evaluators/TesseractProcessOcr.cs
├── GameBot.Service/
│   ├── Services/Ocr/
│   ├── Logging/
│   └── Hosted/
└── GameBot.Emulator/

tests/
├── unit/
│   └── TextOcr/
├── integration/
│   └── TextOcrTesseractTests.cs
└── contract/

tools/
└── coverage/ (new reporting script + templates)
```

**Structure Decision**: Enhance existing domain/service layers in-place (no new projects). Logging helpers live under `src/GameBot.Service/Logging`, OCR instrumentation under `src/GameBot.Domain/Triggers/Evaluators`, and coverage tooling plus docs under `tools/coverage` and `tests/*`.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _None_ | — | — |

## Constitution Re-check (Post Phase 1)

- Code Quality: Logging helper + coverage tooling designs keep functions small, reuse existing analyzers, and avoid new dependencies.
- Testing: Data model + contracts prescribe deterministic mocks plus coverage gate instrumentation; ≥70% line coverage target locked into tooling.
- UX: Quickstart + OpenAPI contract document how operators fetch coverage summaries and how logs behave.
- Performance: Research sets explicit logging overhead (<5 ms) and payload caps (8 KB) ensuring no regression against service budgets.

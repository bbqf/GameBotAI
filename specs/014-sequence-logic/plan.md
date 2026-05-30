# Implementation Plan: Sequence Logic Blocks (Loops & Conditionals)

**Branch**: `001-sequence-logic` | **Date**: 2025-12-17 | **Spec**: specs/001-sequence-logic/spec.md
**Input**: Feature specification from `/specs/001-sequence-logic/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Add declarative control blocks to Command Sequences: fixed-count loops, repeat-until/while condition loops (image/text/trigger-based), and if/then/else branching. Enforce safeguards (`timeoutMs`/`maxIterations`) and capture telemetry per block. Technical approach: extend sequence JSON schema with `blocks` supporting nesting, reuse existing detection/trigger evaluation for conditions, add loop control (`break`/`continue`) semantics, and update endpoints and runner logic iteratively with unit/integration tests and structured logging.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# / .NET 9  
**Primary Dependencies**: Existing detection (OpenCV image-match), OCR (Tesseract), trigger evaluation services  
**Storage**: File-backed JSON under `data/commands/sequences`  
**Testing**: xUnit unit + integration; WebApplicationFactory for service endpoints  
**Target Platform**: Windows
**Project Type**: ASP.NET Core Minimal API (service) + Domain libs  
**Performance Goals**: p95 sequence execution control decisions within 100ms cadence; no observed infinite loops; negligible overhead vs baseline  
**Constraints**: Polling cadence bounded 50–5000ms; enforce `timeoutMs` or `maxIterations` per loop; memory stable within existing budgets  
**Scale/Scope**: Feature confined to sequence execution paths; no new external services

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- Code Quality: planned tooling (lint/format/static analysis), modularity approach, security scanning.
- Testing: unit/integration plan, coverage targets, determinism strategy, CI gating.
- UX Consistency: interface conventions (CLI/API/logs), error messaging, versioning for changes.
- Performance: declared budgets/targets and measurement approach for hot paths.

Quality gates mapped: lint/static analysis enforced (CA rules), structured logging via LoggerMessage, unit+integration coverage added; UX aligns with existing API conventions; performance budgets declared for control path cadence; no hot-path regressions expected. Re-evaluate after design artifacts are produced.

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
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]
**Structure Decision**: Single repository with existing service/domain/tests layout; extend domain (`src/GameBot.Domain/Commands`) and service (`src/GameBot.Service/Program.cs`) with sequence blocks; add docs under `specs/001-sequence-logic/`.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |

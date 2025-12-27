# Implementation Plan: Disk-backed Reference Image Storage

**Branch**: `001-image-storage` | **Date**: 2025-11-26 | **Spec**: `/specs/001-image-storage/spec.md`
**Input**: Feature specification from `/specs/001-image-storage/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Persist reference images on disk under `data/images`, load them at startup, and resolve by `referenceImageId` for image-match triggers. Provide `POST/GET/DELETE /images` endpoints. Ensure atomic writes, input validation, and consistent API semantics.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# / .NET 8 (Service), .NET 9 (Domain alignment)  
**Primary Dependencies**: None new (disk I/O via `System.IO`)  
**Storage**: Disk-backed under `data/images`  
**Testing**: xUnit + FluentAssertions; integration tests via `WebApplicationFactory<Program>`  
**Target Platform**: Windows
**Project Type**: ASP.NET Core Minimal API  
**Performance Goals**: p95 `POST/GET/DELETE /images` < 500 ms; startup load < 1s for 100 images  
**Constraints**: Atomic writes, no partial files; ID validation (no traversal); no new external packages  
**Scale/Scope**: Tens to hundreds of images; single-node service

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- Code Quality: planned tooling (lint/format/static analysis), modularity approach, security scanning.
- Testing: unit/integration plan, coverage targets, determinism strategy, CI gating.
- UX Consistency: interface conventions (CLI/API/logs), error messaging, versioning for changes.
- Performance: declared budgets/targets and measurement approach for hot paths.

[This section is validated against `.specify/memory/constitution.md` during planning.]

- Code Quality: Use existing analyzers; small cohesive methods; no secrets. OK.
- Testing: Add unit tests for ID validation and resolver; integration tests for endpoints and restart persistence; target ≥80% coverage in touched areas. OK.
- UX Consistency: Follow existing `/images` conventions; actionable errors (`invalid_image`, `invalid_request`, `not_found`). OK.
- Performance: Declare budgets above; add micro-benchmark optional for startup load; avoid excessive allocations. OK.

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
├── GameBot.Service/
│   └── Endpoints/
│       └── ImageReferencesEndpoints.cs  # Extend endpoints for disk persistence
├── GameBot.Domain/
│   └── Triggers/Evaluators/
│       └── ReferenceImageStore.cs       # New disk-backed implementation

tests/
├── integration/
│   └── TriggerEvaluationTests.cs        # Add restart persistence tests
└── unit/
  └── ImageStoreTests.cs               # Validate ID rules and resolver
```

**Structure Decision**: Extend existing Service endpoints and Domain evaluators; add a disk-backed `ReferenceImageStore` used by evaluator and endpoints.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| N/A | N/A | N/A |

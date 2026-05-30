# Implementation Plan: UI Configuration Editor

**Branch**: `035-ui-config-editor` | **Date**: 2026-04-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/035-ui-config-editor/spec.md`

## Summary

Replace the minimal Configuration tab (API Base URL + Bearer Token only) with a full-featured editor that dynamically renders all backend configuration parameters, supports inline editing with batch "Apply All", drag-and-drop reorder with immediate persistence, live text filtering, and a collapsible "Backend Connection" section for the existing connection fields.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend)
**Primary Dependencies**: ASP.NET Core Minimal API, existing `ConfigSnapshotService` + `IConfigApplier`, React 18 + Vite 7, HTML5 Drag and Drop API (no external DnD library)
**Storage**: File-backed JSON under `data/config/config.json` (existing); parameter order persisted as key order in the JSON `parameters` object
**Testing**: xUnit + FluentAssertions (backend), Jest 29 + React Testing Library 14 (frontend), Playwright (E2E)
**Target Platform**: Windows (backend service), browser (frontend SPA)
**Project Type**: Web service + SPA (existing)
**Performance Goals**: Filter response < 200 ms for 100 parameters; Apply round-trip < 2 s; drag-and-drop reorder persist < 1 s (single PUT)
**Constraints**: No new npm dependencies for DnD (use HTML5 native); no new NuGet packages; existing CSS variable system (no CSS-in-JS)
**Scale/Scope**: ~30 configuration parameters currently; up to ~100 anticipated

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Code Quality Discipline | PASS | Functions < 50 LOC; no dead code; no new dependencies; modular components. CSS uses existing variable system. |
| II. Testing Standards | PASS | Unit tests: Jest + RTL for frontend components and services; xUnit for backend update/reorder endpoints. ≥80% line coverage target. Integration tests for API contract. |
| III. User Experience Consistency | PASS | Follows existing `.row`, `.content`, `button` CSS patterns. Filter uses same input styling. Collapsible section uses `<details>`/`<summary>` HTML5. Error messages are actionable. Dirty-state highlighting and navigate-away prompt for unsaved changes. |
| IV. Performance Requirements | PASS | Filter: < 200 ms for 100 params (simple string match, no API call). DnD reorder persist: < 1 s (single PUT). Apply All: < 2 s round-trip. No N+1 patterns — single API calls. |
| Quality Gates & DoD | PASS | CamelCase method names (no underscores). Lint/format clean. Security: secrets remain masked; no plain-text secret transmission in responses. |

## Project Structure

### Documentation (this feature)

```text
specs/035-ui-config-editor/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (API contracts)
│   └── config-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Service/
│   ├── Endpoints/
│   │   └── ConfigEndpoints.cs         # MODIFY: add PUT /api/config/parameters, PUT /api/config/parameters/reorder
│   ├── Models/
│   │   └── Config.cs                  # MODIFY: add ConfigUpdateRequest, ConfigReorderRequest DTOs
│   ├── Services/
│   │   ├── ConfigSnapshotService.cs   # MODIFY: add UpdateParametersAsync, ReorderParametersAsync methods
│   │   └── IConfigApplier.cs          # NO CHANGE (Apply already called on refresh)
│   └── Swagger/
│       └── SwaggerConfig.cs           # MODIFY: add examples for new endpoints
│
└── web-ui/src/
    ├── pages/
    │   └── Configuration.tsx           # MODIFY: full rewrite — collapsible section, param list, filter, DnD, Apply All
    ├── components/
    │   ├── TokenGate.tsx               # NO CHANGE (reused inside collapsible section)
    │   ├── CollapsibleSection.tsx      # NEW: generic collapsible <details>/<summary> wrapper
    │   ├── ConfigParameterRow.tsx      # NEW: single parameter row (name, value input, source badge, dirty indicator)
    │   └── ConfigParameterList.tsx     # NEW: parameter list with filter + DnD + Apply All
    ├── services/
    │   └── config.ts                   # NEW: API client functions for config CRUD
    └── __tests__/
        └── configuration.spec.tsx      # MODIFY: update tests for new layout

tests/
├── unit/
│   └── ConfigUpdateTests.cs           # NEW: tests for UpdateParametersAsync, ReorderParametersAsync
└── contract/
    └── ConfigEndpointTests.cs          # MODIFY or NEW: contract tests for PUT endpoints
```

**Structure Decision**: Extend existing backend service + web-ui projects. No new projects. Backend changes are additive (new methods on existing service, new endpoints in existing endpoint class). Frontend replaces the Configuration page with new components following existing patterns.

## Complexity Tracking

No constitution violations. No new projects, no new external packages, no new persistence stores.

## Post-Design Constitution Re-Check

All gates pass after Phase 1 design. No violations introduced:

| Principle | Status | Post-Design Notes |
|-----------|--------|-------------------|
| I. Code Quality | PASS | 2 new backend methods (~30 LOC each). 3 new frontend components (< 50 LOC each). No new packages. |
| II. Testing Standards | PASS | Unit tests planned: xUnit for backend update/reorder; Jest + RTL for frontend components/filter/dirty-state. |
| III. UX Consistency | PASS | Native `<details>`/`<summary>` for collapse (accessible). Existing CSS classes reused. Error shape matches API convention. |
| IV. Performance | PASS | Client-side filter O(n) on ~100 items. Single API calls, no N+1. Native HTML5 DnD events. |
| Quality Gates | PASS | CamelCase methods. Secrets masked. No new stores. |

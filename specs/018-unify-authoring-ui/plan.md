# Implementation Plan: Unified Authoring Object Pages

**Branch**: `[017-unify-authoring-ui]` | **Date**: 2025-12-27 | **Spec**: [specs/017-unify-authoring-ui/spec.md](specs/017-unify-authoring-ui/spec.md)
**Input**: Feature specification from `/specs/017-unify-authoring-ui/spec.md`

## Summary

Unify the authoring UI across Actions, Commands, Triggers, Game profiles, and Sequences by reusing the Action page layout: consistent sections, searchable dropdowns for references (with inline “Create new”), array CRUD/reorder controls, and immediate live saves with clear confirmations. The work centers on front-end React/Vite updates plus aligning contracts to support dropdown population and array ordering.

## Technical Context

**Language/Version**: TypeScript (ES2020), React 18, Vite 5; backend contracts in ASP.NET Core (.NET 9)  
**Primary Dependencies**: React, React Router, form state utilities already in web-ui (no new packages expected)  
**Storage**: Backend file-backed JSON repositories (data/), no new stores  
**Testing**: Vitest/React Testing Library for UI units; Playwright/automation for end-to-end flows (arrays, save)  
**Target Platform**: Modern Chromium/Firefox/Edge; desktop authoring  
**Project Type**: Web frontend consuming existing GameBot Service API  
**Performance Goals**: UI interactions respond within 100 ms for control edits; array reorder applies within 200 ms; initial form load < 1.5 s on broadband  
**Constraints**: Immediate save commits live changes (no draft); must function with existing API shapes and file-backed data; no new external services  
**Scale/Scope**: Catalog sizes in dozens-to-low-hundreds of objects per type; arrays up to ~50 items per object; single-author sessions typical

## Constitution Check

*GATE (pre-Phase 0): PASS – no violations identified.*

- Code Quality: Follow repository lint/format; keep components cohesive; no unused deps. Security scan not impacted (frontend-only changes, no secrets).
- Testing: Add/adjust Vitest + RTL for forms and array controls; add e2e for array reorder + inline create-new; maintain coverage ≥80% lines in touched areas.
- UX Consistency: Match existing Action template layout, button placement, inline validation, and messaging.
- Performance: Adhere to stated interaction budgets; avoid expensive re-renders; measure with dev tools where touching hot paths.

Post-Phase 1 recheck required after design outputs.

*Post-Phase 1 Recheck:* PASS — design artifacts (research, data-model, contracts, quickstart) align with constitution; no new violations introduced.

## Project Structure

### Documentation (this feature)

```text
specs/017-unify-authoring-ui/
├── plan.md          # This plan
├── research.md      # Phase 0 output
├── data-model.md    # Phase 1 output
├── quickstart.md    # Phase 1 output
├── contracts/       # Phase 1 output (API contracts)
└── tasks.md         # Phase 2 (/speckit.tasks, not produced here)
```

### Source Code (repository root)

```text
src/
├── web-ui/                  # React/Vite frontend to update (pages, components)
├── GameBot.Service/         # ASP.NET Core API (contract reference only)
├── GameBot.Domain/          # Domain models backing dropdown data
└── GameBot.Emulator/        # Not directly touched for this feature

tests/
├── unit/                    # Add/adjust UI unit tests
├── integration/             # Potential API contract tests (if needed)
└── contract/                # OpenAPI alignment (reference)
```

**Structure Decision**: Single web frontend (src/web-ui) consuming existing backend API; no new subprojects or packages.

## Complexity Tracking

No constitution violations; tracking not required.

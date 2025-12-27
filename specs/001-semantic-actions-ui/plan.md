# Implementation Plan: Semantic Actions UI

**Branch**: `[001-semantic-actions-ui]` | **Date**: 2025-12-27 | **Spec**: [specs/001-semantic-actions-ui/spec.md](specs/001-semantic-actions-ui/spec.md)
**Input**: Feature specification from `/specs/001-semantic-actions-ui/spec.md`

## Summary

- Deliver a semantic, form-based UI for authoring actions without JSON, driven by backend action-type definitions.
- Client-side validation mirrors backend rules; invalid actions cannot be saved (no draft saves). Real-time preview is out of scope.
- Scope covers create, edit (with type-change safeguards), duplicate, a11y basics, and performance budgets defined in research.

## Technical Context

**Language/Version**: TypeScript (ES2020) + React 18 (Vite 5)  
**Primary Dependencies**: React, Vite toolchain, existing GameBot Service API (action types/actions)  
**Storage**: No new client persistence; uses backend for actions. Client state is in-memory form state.  
**Testing**: Vitest + React Testing Library for UI; existing backend tests remain via xUnit.  
**Target Platform**: Web (desktop browsers); development on Windows.  
**Project Type**: Web (frontend consuming backend API).  
**Performance Goals**: Action-type fetch p95 < 1s; initial form render p95 < 1s; validation feedback perceived < 100ms; filtering 200 actions < 300ms.  
**Constraints**: Must source action-type catalog dynamically from backend; block invalid saves (no drafts with errors); no real-time preview in this release; maintain accessibility (labels, keyboard navigation, contrast).  
**Scale/Scope**: Tens to hundreds of actions; per action up to dozens of attributes per type; single authoring surface.

## Constitution Check

- **Code Quality**: Reuse existing lint/format; keep UI components cohesive and small; avoid new deps unless justified; security scanning per repo defaults.  
- **Testing**: Unit tests for form rendering/validation; integration/e2e for create/edit/duplicate; maintain ≥80% line / ≥70% branch coverage on touched code; deterministic fixtures for backend definitions.  
- **UX Consistency**: Actionable labels/help/error copy; confirmation on type change; avoid raw JSON exposure; align with existing web-ui styling and navigation.  
- **Performance**: Budgets declared above; measure with dev tools; avoid blocking network calls on render; cache definitions with short TTL.

Gate status: pass (no violations requiring exceptions).

## Project Structure

### Documentation (this feature)

```text
specs/001-semantic-actions-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md (Phase 2, created by /speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── src/GameBot.Service
└── tests/ (unit, integration)

frontend/
├── src/web-ui
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/ (unit via Vitest; e2e if present)
```

**Structure Decision**: Use existing backend (GameBot.Service) and frontend (web-ui) directories; no new top-level projects required.

## Complexity Tracking

No constitution violations; tracking not required.

## Phase 0: Research
- Completed; see [specs/001-semantic-actions-ui/research.md](specs/001-semantic-actions-ui/research.md).
- Unknowns resolved: backend is authoritative catalog; client-side validation derived from definitions; type-change compatibility rules; performance budgets declared.

## Phase 1: Design & Contracts
- Data model: [specs/001-semantic-actions-ui/data-model.md](specs/001-semantic-actions-ui/data-model.md)
- Contracts: [specs/001-semantic-actions-ui/contracts/actions.openapi.yaml](specs/001-semantic-actions-ui/contracts/actions.openapi.yaml)
- Quickstart: [specs/001-semantic-actions-ui/quickstart.md](specs/001-semantic-actions-ui/quickstart.md)
- Notes: Schema-driven UI; invalid draft saves blocked; preview out of scope this release.

## Post-Design Constitution Check
- **Code Quality**: Plan remains within gates; no new dependencies introduced yet.  
- **Testing**: Unit + integration commitments align with coverage targets.  
- **UX**: A11y and confirmation patterns included; no raw JSON exposure.  
- **Performance**: Budgets defined; measurement approach noted.

Gate status: pass.

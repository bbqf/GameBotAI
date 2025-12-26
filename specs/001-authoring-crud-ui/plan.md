# Implementation Plan: Authoring CRUD UI

**Branch**: `001-authoring-crud-ui` | **Date**: 2025-12-26 | **Spec**: [specs/001-authoring-crud-ui/spec.md](specs/001-authoring-crud-ui/spec.md)
**Input**: Feature specification from `/specs/001-authoring-crud-ui/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Extend the web authoring UI to include a navigation menu for selecting object types (actions, commands, games, sequences, triggers), list views for each, and full CRUD aligned to the backend API. Where references are required, present name-based dropdowns while using IDs internally. Deletion is blocked when the object is referenced; all authenticated users may perform destructive actions with explicit confirmation.

## Technical Context

**Language/Version**: TypeScript (ES2020), React 18  
**Primary Dependencies**: React, React DOM, Vite, @vitejs/plugin-react  
**Storage**: None client-side (in-memory state); persistence via backend API  
**Testing**: Jest + React Testing Library (existing in `src/web-ui`)  
**Target Platform**: Web (desktop browsers); repo runs on Windows  
**Project Type**: Web UI (frontend in `src/web-ui`, backend minimal API already exists)  
**Performance Goals**: p95 list load under 1s for ≤500 items; p95 create/edit confirmation under 3s; navigation visible response under 200ms  
**Constraints**: No new frontend dependencies beyond existing stack unless justified; destructive actions require confirmation; deletion blocked when referenced  
**Scale/Scope**: Up to ~1,000 objects per type in lists; 5 object types; single admin/editor audience

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- Code Quality: Use ESLint with React, TypeScript rules; ensure modular components (lists, forms, services) with no dead code; no secrets in frontend.
- Testing: Jest + React Testing Library unit tests for list rendering, form validation, and delete confirmations; integration tests for API contract adherence (mock/fetch); target ≥80% line coverage in touched UI modules.
- UX Consistency: Consistent labeling for object types; actionable error messages with remediation (e.g., unlink references before delete); predictable ordering and immediate refresh.
- Performance: Budgets declared above; measure via simple timings and devtools; avoid N+1 API calls (batch fetch for dropdowns where needed).

Status: Pass — budgets and testing approach declared; no new dependencies planned. Post-design re-check completed with agent context updated.

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
frontend (existing):
src/web-ui/
├── src/
│   ├── components/            # New: shared UI (Nav, List, Form, Dropdown)
│   ├── pages/                 # New: per-type pages (Actions, Commands, Games, Sequences, Triggers)
│   └── services/              # New: API client per type; fetch helpers
├── tests/                     # Jest tests for components/pages
└── vite.config.ts

backend (existing):
src/GameBot.Service/           # Minimal API providing CRUD endpoints
```

**Structure Decision**: Use existing frontend `src/web-ui` with components/pages/services split; reuse existing backend. No additional projects.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |

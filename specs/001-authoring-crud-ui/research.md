# Research: Authoring CRUD UI

**Branch**: 001-authoring-crud-ui  
**Date**: 2025-12-26

## Decisions

### Navigation approach
- **Decision**: Implement simple tabbed navigation (no new router dependency).
- **Rationale**: Keeps dependencies minimal per constraints; sufficient for authoring pages.
- **Alternatives considered**: React Router (adds dependency and complexity); URL hash routing (not needed for initial authoring scope).

### Testing framework
- **Decision**: Use Jest + React Testing Library (already present in `src/web-ui`).
- **Rationale**: Existing setup reduces friction; broad community support.
- **Alternatives considered**: Vitest (fast, Vite-native) but not currently installed; Playwright for E2E (deferred to later phase).

### List ordering and filtering
- **Decision**: Default ordering by name ascending; add client-side filter box when items > 50.
- **Rationale**: Predictable and simple; avoids backend changes.
- **Alternatives considered**: Server-side sorting/filtering (requires backend changes and pagination).

### Reference dropdown data
- **Decision**: Fetch lists for referenced types up-front per page and cache in memory; refresh on create/edit.
- **Rationale**: Minimizes repeated calls; ensures dropdowns reflect latest names.
- **Alternatives considered**: Lazy-load on open (adds latency); global cache with TTL (more complex state management).

### Delete behavior when referenced
- **Decision**: Block deletion; surface guidance to unlink/migrate references; show which dependents prevent deletion.
- **Rationale**: Protects integrity; matches spec.
- **Alternatives considered**: Cascade delete (risky); soft delete (adds inactive state complexity).

### Performance budgets
- **Decision**: p95 list load under 1s for ≤500 items; p95 create/edit confirmation under 3s; navigation response under 200ms.
- **Rationale**: Aligns with spec success criteria; feasible for UI scale.
- **Alternatives considered**: Stricter budgets (risk of over-optimization).

## Resolved Clarifications
- All NEEDS CLARIFICATION items from Technical Context are resolved in this document.

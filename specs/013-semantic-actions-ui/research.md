# Research: Semantic Actions UI

**Date**: 2025-12-27  
**Feature**: [specs/001-semantic-actions-ui/spec.md](specs/001-semantic-actions-ui/spec.md)

## Findings

- **Decision: Use backend as the authoritative catalog for action types and attribute definitions (fetched on load, cache 5 minutes with ETag/Last-Modified).**  
  **Rationale**: Keeps UI aligned with server-supported types, avoids client drift, and simplifies updating validation rules. Short cache minimizes stale definitions while limiting redundant fetches.  
  **Alternatives considered**: Client-managed config file (risk of drift, requires deployments to update); hybrid offline cache with manual refresh (adds UX complexity without a current offline requirement).

- **Decision: Derive client-side validation and form rendering directly from the fetched definitions, mirroring server rules.**  
  **Rationale**: Ensures parity with backend validation, supports type-specific fields, and blocks invalid saves per spec. Mapping types to controls (text/number/boolean/enum) keeps UX predictable; pattern/range enforcement comes from definitions.  
  **Alternatives considered**: Hard-coded per-type forms (fragile to backend changes); server-only validation (hurts UX, round trips for simple errors).

- **Decision: Handle action-type changes by preserving only compatible attributes (same key and compatible data type) and requiring confirmation before discarding incompatible attributes.**  
  **Rationale**: Aligns with spec safeguards, protects user input, and keeps resulting action valid under the new type.  
  **Alternatives considered**: Blindly drop all attributes on type change (too destructive); always keep all attributes (risks invalid payloads and silent errors).

- **Decision: UX/performance budgets—definition fetch p95 < 1s, initial form render p95 < 1s, validation feedback < 100ms perceived instant, list view of 200 actions filters within < 300ms.**  
  **Rationale**: Keeps the experience responsive for authoring workflows and meets constitution’s performance declaration requirement.  
  **Alternatives considered**: No explicit budgets (fails constitution gate); stricter budgets (<500ms for all) not justified by current scale.

## Open Items

- None. All prior clarifications resolved in spec.

# Research: Unified Authoring Object Pages

## Decision 1: Inline create-new for references
- **Decision**: Reference dropdowns include a “Create new” affordance that opens the unified creation flow in a side panel/modal and auto-selects the new item.
- **Rationale**: Keeps authors in context, avoids dead-end when referenced object absent, enforces consistent layout for all object types.
- **Alternatives considered**: (a) Require pre-creation on separate page (slower, context switching). (b) Open in new tab and refresh (fragile, risks losing unsaved changes).

## Decision 2: Immediate saves (no draft/publish)
- **Decision**: Every save writes live immediately; Save/Cancel only.
- **Rationale**: Simpler mental model, aligns with current backend behavior, reduces dual-state complexity.
- **Alternatives considered**: (a) Draft + Publish (adds state and review workflow not requested). (b) Approval gate (overhead, no governance ask).

## Decision 3: Array management pattern
- **Decision**: Arrays (steps, targets, trigger actions) use unified controls: add/edit modal or inline panel, delete with confirm, drag-and-drop reorder with order preview, and persisted ordering.
- **Rationale**: Arrays are central; uniform controls lower training cost and reduce ordering errors.
- **Alternatives considered**: (a) Text/JSON entry (non-technical users blocked). (b) Up/down arrows only (slower, worse for long lists).

## Decision 4: Performance budget for UI interactions
- **Decision**: Form edits ≤100 ms response, reorder ≤200 ms, initial load <1.5 s on broadband.
- **Rationale**: Keeps UI feeling instant for authors; budgets are achievable with current data sizes (dozens-to-low-hundreds items).
- **Alternatives considered**: (a) No budget (risks regressions). (b) Stricter <50 ms everywhere (unnecessary given scope).

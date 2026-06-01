# Phase 0 Research: Edit Queue Page Layout

All Technical Context items are known (frontend-only refinement of an existing,
fully-specified page). No `NEEDS CLARIFICATION` remained after `/speckit-clarify`. The
decisions below resolve the design choices the layout change raises.

## Decision 1 — How to interleave the new row order without breaking create mode

- **Decision**: Add two optional render slots to `QueueForm` (`templateControls`,
  `entries`) rendered between the emulator field and cycle execution, and between cycle
  execution and the form actions, respectively. Edit mode passes both; create mode
  passes neither.
- **Rationale**: The required edit-page order (name+emulator → templates → cycle →
  sequences → save/cancel) interleaves template/sequence content into the middle of the
  existing form. Slots keep a single `<form>` (so row-5 Save/Cancel stay the form's
  native submit/cancel and Name/Cycle stay controlled) while leaving the create form's
  layout untouched. Lowest-risk, smallest diff.
- **Alternatives considered**:
  - *Build a separate edit-page layout in `QueuesPage`*: duplicates field rendering and
    diverges create/edit markup — more code, drift risk.
  - *Reorder fields inside one monolithic form for both modes*: changes the create
    layout unnecessarily and couples unrelated content into the form component.

## Decision 2 — Inline sections vs. keeping modal dialogs

- **Decision**: Convert `SaveTemplateDialog` and `TemplatePickerDialog` into inline
  collapsible **sections** (drop the `modal-backdrop`/`aria-modal` wrappers; render a
  labeled `<section>`), keeping all internal logic. A new `QueueTemplateControls`
  component owns which section is open (mutual exclusion).
- **Rationale**: The spec requires the Save/Load panels to "open between row 2 and 3"
  inline and default closed — a modal overlay does not satisfy "between rows." Reusing
  the existing components preserves validated behavior (name rules, overwrite confirm,
  list/empty-state/delete) and limits changes to container markup + tests.
- **Alternatives considered**:
  - *Write brand-new section components*: more churn and duplicated logic for no benefit.
  - *Keep modals, add Reload only*: violates the explicit inline-section requirement.

## Decision 3 — Resolving the template to reload (by name vs. by id)

- **Decision**: Track the associated template by **name** only and resolve it on reload
  via `listQueueTemplates()` + case-insensitive name match, then `getQueueTemplate(id)`.
- **Rationale**: The spec says a reload whose template was "deleted **or renamed**" must
  report "no longer available." Name-based resolution makes a rename read as not-found
  (the stored name changed), satisfying FR-018 with no backend change. An id-based
  lookup would still resolve after a rename, contradicting the spec.
- **Alternatives considered**:
  - *Track id and `getQueueTemplate(id)`*: rename would not be detected as unavailable.
  - *Add a `GET /api/queue-templates?name=` endpoint*: unnecessary backend work; the
    list endpoint already returns names at the target scale (<1s, ≤50 templates).

## Decision 4 — When the Reload confirmation appears (diff-aware)

- **Decision**: Prompt for confirmation only when the queue is non-empty **and** its
  current ordered `sequenceIds` differ from the template's. Identical entries → no-op,
  no prompt; empty queue → apply directly, no prompt.
- **Rationale**: Directly encodes the clarification ("confirm only when entries would
  change"). A pure, order-sensitive `sameSequenceOrder(a, b)` helper makes the rule
  testable and keeps the handler thin.
- **Alternatives considered**:
  - *Always confirm*: rejected by clarification (annoying when nothing to discard).
  - *Confirm whenever non-empty*: still prompts on a no-op identical reload.

## Decision 5 — Independence of entry actions from page Save/Cancel

- **Decision**: Add/remove/load/reload act on the queue immediately via their services;
  row-5 Save/Cancel commit or discard only Name + Cycle execution.
- **Rationale**: Clarified directly — sequence entries are runtime-only and not
  persisted with the queue, so they have no relationship to the page-level form
  submission. This also matches the current architecture (entries mutate via dedicated
  endpoints), so no buffering layer is introduced.
- **Alternatives considered**:
  - *Buffer all entry edits until Save*: large change (queue-entry buffering, optimistic
    state), explicitly out of scope per the clarification ("for now at least").

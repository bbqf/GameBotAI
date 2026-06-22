# Phase 0 Research: Queue Management Usability

All spec ambiguities were resolved during `/speckit-clarify` (Session 2026-06-22). No NEEDS
CLARIFICATION markers remain. This document records the decisions that shape the implementation.

## Decision 1 — One-click template save: how to decide whether to confirm

**Decision**: Use a single trimmed/case-insensitive comparison against the queue's currently
associated template name, then let the existing server `409` distinguish the remaining cases — no
extra template listing call:

- Typed name equals the **associated** template name → send `overwrite: true` directly (one click,
  overwrites the same template the queue is linked to).
- Otherwise → send `overwrite: false`:
  - Name matches **no** existing template → the save succeeds as a new template (one click).
  - Name matches a **different** existing template → the server returns `409`; the dialog shows the
    existing overwrite confirmation and, on confirm, re-sends with `overwrite: true`.

**Rationale**: The existing `POST /api/queue-templates` already returns `409` when the name exists
and `overwrite` is false. Today the dialog *always* sends `overwrite: false` first and treats every
409 as a prompt — that is what produces the extra step even when overwriting the same associated
template. Adding the single "is this the associated template?" check on the client lets the common
case (re-saving the associated template) skip the prompt, while the server's 409 still guards the
genuinely destructive case (clobbering an unrelated template) without the client needing to fetch and
match the full template list.

**Implementation note**: The associated template name is already available in the UI
(`associatedTemplateName`, surfaced to the dialog as `originName`). Comparison is trimmed and
case-insensitive, consistent with the Reload flow's matching.

**Alternatives considered**:
- *Always send `overwrite: true`* — rejected: removes the safety guard for renaming into a different
  existing template (spec FR-005 / clarification Q1 = keep that confirmation).
- *Pre-fetch `listQueueTemplates()` and match every case on the client* — rejected: adds a network
  round-trip and duplicates collision logic the server already enforces via 409; the associated-name
  check plus the existing 409 path yields identical behavior with less work.

## Decision 2 — Where save confirmations render

**Decision**: Render the confirmation inline at the control the user clicked, and stop using the
page-top status banner for these two saves (clarification Q2 = move, not duplicate):

- **Queue save**: a status/error line at the `QueueForm` Save/Cancel action row.
- **Template save**: a status/error line at the `QueueTemplateControls` Save Template row.

**Rationale**: Co-location is the explicit user request ("confirmations MUST be shown wherever I
clicked Save"). The template controls in particular sit far down the edit form, so the page-top
banner was effectively invisible at the moment of action.

**Alternatives considered**:
- *Keep the top banner as well* — rejected per clarification Q2 (move, avoid duplicate/noise).
- *Toast/notification system* — rejected: no existing toast infrastructure in web-ui; introducing one
  is out of scope and heavier than needed.

## Decision 3 — Removing the Sequences column

**Decision**: Remove the `Sequences` header and the `q.entryCount` cell from the queues overview
table, and decrement the `colSpan` used by the loading / empty / live-schedule rows accordingly
(6 → 5).

**Rationale**: The count is low-signal (often zero) and entry details remain visible inside the queue
editor. The only cross-cutting risk is the `colSpan={6}` spans used by full-width rows, which must be
updated to keep the layout correct.

**Alternatives considered**:
- *Hide via CSS* — rejected: leaves dead markup and a misleading column count; removal is cleaner.

## Verification approach

`cd src/web-ui` then `npm run build` (Vite) and `npm test` (Jest) — the established green gate for
web-ui. Lint and `tsc --noEmit` have pre-existing failures and are not the gate.

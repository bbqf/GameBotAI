# UI Contract: Edit Queue Page

No REST/external contracts are added or changed. This feature reuses, unchanged:

- `GET /api/queue-templates` — list templates (used to resolve a reload by name).
- `GET /api/queue-templates/{id}` — template detail (entries) for load/reload.
- `POST /api/queue-templates` — save (with `overwrite`) — inline Save section.
- `DELETE /api/queue-templates/{id}` — delete (inline Load section, per-row).
- `PUT /api/queues/{id}/entries` — replace queue entries (load and reload apply path;
  `409 queue_running` while running).

The contract below is the **UI contract** for the edit-queue page (appropriate for an
application feature). Each item maps to functional requirements in `spec.md`.

## Row order (top → bottom) — FR-001

1. **Name** (editable text) + **Emulator** (read-only). No "cannot be changed" hint
   (FR-002, FR-003).
2. **Template controls** (single row) + inline Save/Load panels below it (FR-006, FR-009).
3. **Cycle execution** (checkbox).
4. **Sequences** — one row per entry, existing add/remove controls and empty state
   (FR-005).
5. **Save** / **Cancel** — commit/discard Name + Cycle execution only (FR-004, FR-004a).

## Row 2 controls — FR-006 … FR-013

| Control | Behavior |
|---------|----------|
| Template-name button | Label `(no template)` when none associated, else the name. Click → open inline **Load** panel (FR-007, FR-008). |
| Save Template button | Click → open inline **Save** panel. |
| Reload Template button | Disabled when no template associated (FR-016) or queue Running (FR-017). Click → reload flow. |
| Inline panels | Appear between row 2 and row 3; default closed (FR-010); at most one open (FR-011); collapse on complete/dismiss (FR-012). |
| Save panel | Existing name validation + overwrite confirmation, now inline (FR-013). |
| Load panel | Existing template list, empty state, per-row Delete (with confirm), now inline; Load disabled while Running (FR-013). |

## Reload flow — FR-014 … FR-018

1. Resolve associated template by **name** (case-insensitive) from the list.
   - No match → message "Template '<name>' is no longer available"; no change (FR-018).
2. Fetch its entries; compute `templateSequenceIds`.
3. Compare with the queue's current ordered `sequenceIds`:
   - Equal → no-op, no prompt.
   - Queue empty → apply, no prompt.
   - Non-empty and different → confirmation modal; **Reload** applies, **Cancel** leaves
     entries unchanged (FR-015).
4. Apply = replace queue entries with the template's (full, order-preserving) via
   `PUT /api/queues/{id}/entries`.

## States observed

- **Running queue**: Reload and Load disabled; Save allowed. Save/Cancel field edits
  follow existing running-queue rules.
- **No template associated**: name button shows placeholder, still opens Load; Reload
  disabled.
- **Empty sequences**: row 4 shows the existing empty state; row order unchanged.

# Implementation Plan: Edit Queue Page Layout

**Branch**: `048-edit-queue-layout` | **Date**: 2026-06-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/048-edit-queue-layout/spec.md`

## Summary

Refine the **Edit Queue** page into a fixed, ordered set of rows and add a **Reload
Template** action — a frontend-only change. The edit page becomes, top to bottom:
(1) Name (editable) + Emulator (read-only, with the "cannot be changed" hint removed),
(2) a single template-controls row (template-name button + **Save Template** +
**Reload Template**) with the Save and Load panels expanding **inline** between rows 2
and 3 (collapsed by default, only one open at a time), (3) Cycle execution,
(4) Sequences, (5) page-level Save/Cancel.

No backend, API, or persistence change is needed: Save/Load already exist
(`POST /api/queue-templates`, `GET /api/queue-templates[/{id}]`, `PUT /api/queues/{id}/entries`)
from feature 047. The work is: (a) interleave the existing edit controls via two
render slots on `QueueForm`, (b) convert the two template **dialogs** into inline
collapsible **sections** owned by a new `QueueTemplateControls` component, and (c) add
**Reload Template** — resolve the queue's associated template by its remembered name,
diff the current entries against it, and re-apply via the existing replace-entries
service, prompting only when the reload would actually discard changes (per
clarification). Save/Cancel govern only Name + Cycle execution; entry/template actions
act on the queue immediately and independently (per clarification).

## Technical Context

**Language/Version**: TypeScript 5.6.3 / React 18.3.1 (web-ui). No backend change.
**Primary Dependencies**: React, Vite; existing in-house components
(`QueueForm`, `QueueEntryList`, `SaveTemplateDialog`, `TemplatePickerDialog`,
`ConfirmDeleteModal`, `SearchableDropdown`); existing services (`queues.ts`,
`queueTemplates.ts`).
**Storage**: N/A — no persisted-data change. Queue sequence entries remain runtime-only
(non-persistent); templates remain file-backed exactly as in 047.
**Testing**: Jest 29 + React Testing Library 14 (frontend). No xUnit change required.
**Target Platform**: Modern web browser (authoring SPA) against the GameBot service.
**Project Type**: Web application (frontend slice only this iteration).
**Performance Goals**: Interactions reflected <1s at the established scale (≤~50
templates × ≤~100 entries); UI-only, no heavy compute (reuses SC-007 budget from 047).
**Constraints**: CamelCase method names only (no underscores); functions ≤50 LOC;
reuse existing confirmation/UX conventions; Reload disabled while running and when no
template is associated; confirm-on-reload only when entries would change.
**Scale/Scope**: ~4 frontend files touched + 1 new component; no new dependencies.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI),
implementation progression is blocked until failures are fixed or a documented
maintainer waiver exists.

| Gate (from constitution) | Status | Notes |
|--------------------------|--------|-------|
| Lint/format/static analysis clean | Must pass | ESLint; enforced in CI |
| No underscores in method names — CamelCase only | Must pass | All new TS handlers/components CamelCase |
| Functions ≤50 LOC | Must pass | Reload handler + section components kept thin; diff helper extracted |
| Unit ≥80% line / ≥70% branch on touched areas | Must pass | Tests for `QueueTemplateControls` (reload diff/confirm, section toggling), inline sections, `QueueForm` slots, `QueuesPage` wiring |
| Deterministic, isolated, fast tests | Must pass | RTL with mocked services; no real I/O |
| UX consistency with existing conventions | Must pass | Reuses `ConfirmDeleteModal`, existing form/field classes, error envelope; same Save/Load logic, now inline |
| Actionable error messages | Must pass | "Template no longer available" on reload-missing; existing name/queue-running messages preserved |
| Performance goals declared | ✅ Declared | <1s interactions (SC inherited) |
| Public API/contract documented | ✅ N/A new | No backend contract change; UI contract in `contracts/edit-queue-ui.md` |
| Observability | ✅ N/A | No new logging (consistent with 046/047) |
| No unjustified new dependencies | ✅ None added | Reuses existing stack |

No constitution violations. No waivers required. No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/048-edit-queue-layout/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (UI state model; no new persisted entities)
├── quickstart.md        # Phase 1 output (manual verification)
├── contracts/
│   └── edit-queue-ui.md # Phase 1 output — UI contract (rows + control behaviors)
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/web-ui/src/components/queues/
├── QueueForm.tsx                     # MODIFIED — remove emulator hint; add `templateControls`/`entries` render slots (edit only)
├── QueueTemplateControls.tsx         # NEW — row 2: template-name button + Save/Reload buttons; owns which inline section is open; reload orchestration
├── SaveTemplateDialog.tsx            # MODIFIED → inline section (drop modal-backdrop; collapsible panel; keep name validation + overwrite confirm)
├── TemplatePickerDialog.tsx          # MODIFIED → inline section (drop modal-backdrop; collapsible panel; keep list/empty-state/delete)
├── QueueEntryList.tsx                # UNCHANGED — rendered in row 4 via QueueForm `entries` slot
└── __tests__/
    ├── QueueForm.test.tsx            # MODIFIED — assert hint removed; slots render in order
    ├── SaveTemplateDialog.test.tsx   # MODIFIED — inline rendering (no backdrop), unchanged logic
    ├── TemplatePickerDialog.test.tsx # MODIFIED — inline rendering, unchanged logic
    └── QueueTemplateControls.test.tsx# NEW — section mutual-exclusion, reload diff/confirm/disabled/missing

src/web-ui/src/pages/
├── QueuesPage.tsx                    # MODIFIED — compose edit rows via QueueForm slots; track associatedTemplateName; reload handler; remove standalone modal mounts for Save/Load
└── __tests__/
    ├── QueuesPage.layout.spec.tsx    # NEW — row order + Save/Cancel scope (US1)
    ├── QueuesPage.templates.spec.tsx # MODIFIED — inline sections; associated-name pre-fill; Load disabled while running (US2)
    └── QueuesPage.reload.spec.tsx    # NEW — reload diff/confirm/disabled/missing (US3)

src/web-ui/src/lib/
├── sequenceOrder.ts                  # NEW — pure, order-sensitive `sameSequenceOrder(a, b)` helper (backs the reload diff)
└── __tests__/sequenceOrder.test.ts   # NEW — equal/empty/diff-length/diff-order/duplicates

src/web-ui/src/services/
├── queues.ts                         # UNCHANGED — replaceQueueEntries already exists
└── queueTemplates.ts                 # UNCHANGED — list/get/save/delete already exist

src/web-ui/src/styles.css             # MODIFIED — template-controls row + inline section panel styles
```

**Structure Decision**: Web application, **frontend-only** slice. The edit page is
recomposed by giving `QueueForm` two optional render slots so the existing form keeps a
single `<form>` (and the row-5 Save/Cancel stay its submit/cancel) while the template
controls and the sequence list are interleaved at rows 2 and 4. The two template
experiences are converted from modal dialogs to inline collapsible sections owned by a
new `QueueTemplateControls` component; genuine confirmations (overwrite, delete,
replace-on-load, reload) remain modal via the existing `ConfirmDeleteModal`. No backend
module, endpoint, contract, or persisted entity changes.

## Implementation Design

### Row layout via QueueForm slots

`QueueForm` gains two optional props, rendered only when provided (create mode passes
neither, preserving today's layout):

- `templateControls?: React.ReactNode` — rendered immediately **after** the emulator
  field and **before** the cycle-execution field (row 2, plus the inline panel area
  between rows 2 and 3).
- `entries?: React.ReactNode` — rendered **after** cycle execution and **before** the
  `form-actions` (row 4, before row 5).

Remove the `form-hint` "The bound emulator cannot be changed after creation." (FR-003).
Emulator stays read-only in edit mode (unchanged). The page-level Save/Cancel remain the
form's submit/cancel and continue to commit only Name + Cycle execution (FR-004).

### QueueTemplateControls (new) — row 2

Props: `{ associatedTemplateName?, status, currentSequenceIds, onSaveTemplate,
onLoadTemplate, onReload }` (exact shape finalized in `data-model.md`). Internal state:
`openSection: 'none' | 'save' | 'load'` (FR-010 default `'none'`; FR-011 mutual
exclusion — opening one sets the other closed).

Row contents (FR-006):
- **Template-name button**: label = `associatedTemplateName ?? '(no template)'`;
  `onClick` → `openSection = 'load'` (FR-007/FR-008).
- **Save Template button**: `onClick` → `openSection = 'save'`.
- **Reload Template button**: `disabled = !associatedTemplateName || status ===
  'Running'` (FR-016/FR-017); `onClick` → `onReload()`.

Inline sections (rendered between the row and the slot boundary, FR-009):
- When `openSection === 'save'` → the converted `SaveTemplateDialog` (now an inline
  section; file/component name unchanged).
- When `openSection === 'load'` → the converted `TemplatePickerDialog` (now an inline
  section; file/component name unchanged).
- Completing or dismissing a section calls back to set `openSection = 'none'` (FR-012).

> Naming: the two template components keep their existing names/files
> (`SaveTemplateDialog`, `TemplatePickerDialog`); only their rendering changes from a
> modal overlay to an inline collapsible section. They are **not** renamed.

### Inline section conversion

`SaveTemplateDialog` and `TemplatePickerDialog` keep all of their current logic (name
validation, `template_exists` → overwrite confirm; template list, empty state, per-row
Delete with `ConfirmDeleteModal`) but render as **inline panels**: drop the
`modal-backdrop`/`modal` wrappers and `aria-modal`, render a labeled `<section>` with the
existing controls, and expose an `onClose`/Cancel that collapses the panel. Behavior
otherwise unchanged (FR-013), so existing tests change only their container assertions.

### Reload Template

Handled in `QueuesPage` via `onReload`:
1. Guard: ignore if no associated template or `status === 'Running'` (the button is
   already disabled; defensive).
2. Resolve by **remembered name**: `listQueueTemplates()` → find a case-insensitive name
   match. None → surface "Template '<name>' is no longer available." and stop (FR-018;
   covers delete and rename).
3. `getQueueTemplate(match.id)` → `templateSeqIds = entries.map(e => e.sequenceId)`.
4. Diff against `detail.entries.map(e => e.sequenceId)` (order-sensitive equality).
   - Identical → no-op, no prompt (nothing to do).
   - `detail.entries.length === 0` → apply directly, no prompt.
   - Otherwise (non-empty and differs) → show a reload confirm modal; on confirm apply,
     on cancel leave unchanged (FR-015, clarified).
5. Apply = reuse the existing `applyLoad(name, sequenceIds)` path
   (`replaceQueueEntries` → set associated name → `reloadDetail` → `refresh`).

A small pure helper `sameSequenceOrder(a, b)` (≤50 LOC, CamelCase) backs the diff and is
unit-tested directly.

### QueuesPage wiring changes

- Replace `loadedTemplateName?: string` with an associated-template object that also
  carries the id when known, **or** keep the name and always resolve the id on reload
  via the list call (chosen: keep name-only state + resolve-by-name, so rename correctly
  reads as "unavailable"). Set on successful load (already) and on successful save
  (the `POST` response name).
- Render the edit section through `QueueForm` slots: pass `<QueueTemplateControls .../>`
  as `templateControls` and `<QueueEntryList .../>` as `entries`. Remove the separate
  `queue-templates-section` block and the standalone `<SaveTemplateDialog>` /
  `<TemplatePickerDialog>` mounts (now owned by `QueueTemplateControls`).
- Add a reload confirm `ConfirmDeleteModal` (title "Reload template", confirm
  "Reload") driven by a `pendingReload` state, mirroring the existing `pendingLoad`
  replace-confirm.

## Contracts

No new or changed external/REST contracts. Reuses `GET /api/queue-templates`,
`GET /api/queue-templates/{id}`, and `PUT /api/queues/{id}/entries` from 047. The
**UI contract** (row order, control set, and behaviors) is documented in
`contracts/edit-queue-ui.md`.

## Testing Strategy

**Frontend (Jest + RTL)** — touched-area coverage ≥80% line / ≥70% branch:
- `sameSequenceOrder`: equal/empty/different-length/different-order/duplicates.
- `QueueTemplateControls`: default both sections closed; clicking the name opens Load;
  clicking Save opens Save; opening one closes the other; Reload disabled when no
  associated template and when running; Reload enabled + fires otherwise.
- `SaveTemplateDialog` / `TemplatePickerDialog` (now inline sections): render inline (no
  `modal-backdrop`); preserve validation/overwrite-confirm and list/empty-state/delete;
  Cancel/Close collapses (calls `onClose`).
- `QueueForm`: emulator hint absent; in edit, slots render in order
  emulator → templateControls → cycle → entries → actions; create renders without slots.
- `QueuesPage.layout` (US1): row order on the edit page (name+emulator → template
  controls → cycle → sequences → Save/Cancel); emulator read-only, hint absent; Cancel
  discards name/cycle edits; entry add/remove applies immediately (independent of Save).
- `QueuesPage.templates` (US2): associated name pre-fills the inline Save section and
  updates after save/load; Load disabled while the queue is running.
- `QueuesPage.reload` (US3): Reload with identical entries → no prompt, no change; with
  an empty queue → applies without prompt; with divergent non-empty entries → confirm
  shown and cancelable; when the template is missing → "no longer available"; Reload
  disabled with no associated template and while running.

The `QueuesPage` test scenarios are split across three spec files —
`QueuesPage.layout.spec.tsx`, `QueuesPage.templates.spec.tsx`, and
`QueuesPage.reload.spec.tsx` — one per user story (US1/US2/US3).

**No backend tests** change (no backend change). Manual verification in `quickstart.md`.

## Complexity Tracking

No constitution violations requiring justification.

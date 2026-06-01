---

description: "Task list for Edit Queue Page Layout"
---

# Tasks: Edit Queue Page Layout

**Input**: Design documents from `/specs/048-edit-queue-layout/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Included — the GameBot constitution (Testing Standards II) requires tests for
executable logic, and the plan declares a frontend test strategy. Within each user story
the Tests subsection is written first (and is numbered before) its Implementation
subsection, per TDD.

**Organization**: Tasks are grouped by user story. This is a **frontend-only** change
(no backend, API, or persisted-entity work). All paths are under `src/web-ui/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (maps to spec.md user stories)
- Exact file paths included in each task

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a green baseline before changing the edit-queue page.

- [ ] T001 Run the frontend suite to confirm a green baseline and confirm the touched files exist (`npm --prefix src/web-ui test`); files: `src/web-ui/src/pages/QueuesPage.tsx`, `src/web-ui/src/components/queues/{QueueForm,QueueEntryList,SaveTemplateDialog,TemplatePickerDialog}.tsx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared `QueueForm` change that the layout (US1) and the template row
(US2) both build on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 Add optional `templateControls` and `entries` render slots to `QueueForm` — render `templateControls` after the emulator field and before cycle execution, and `entries` after cycle execution and before `form-actions`; remove the "The bound emulator cannot be changed after creation." hint (FR-003); create mode passes neither slot, preserving its layout — in `src/web-ui/src/components/queues/QueueForm.tsx`
- [ ] T003 [P] Update `QueueForm` tests: assert the emulator hint is absent, that in edit mode the slot nodes render in order (emulator → templateControls → cycle → entries → actions), and that create mode renders without the slots — in `src/web-ui/src/components/queues/__tests__/QueueForm.test.tsx`

**Checkpoint**: `QueueForm` exposes ordered slots and no longer shows the emulator hint.

---

## Phase 3: User Story 1 - Edit a queue through a clear, ordered layout (Priority: P1) 🎯 MVP

**Goal**: The edit page renders its controls top-to-bottom as rows 1–5; Name is
editable, Emulator read-only; the page-level Save/Cancel commit/discard only Name +
Cycle execution, while sequence-entry actions apply immediately.

**Independent Test**: Open a queue for editing and confirm the row order
(name+emulator → template-controls region → cycle → sequences → Save/Cancel), that
emulator is read-only with no "cannot be changed" hint, and that Save/Cancel affect only
name + cycle execution (entry add/remove persists regardless).

### Tests for User Story 1

- [ ] T004 [P] [US1] Layout-order and Save/Cancel-scope test: edit page renders rows in order (name+emulator, a template-controls region between rows 2 and 3, cycle, sequences, Save/Cancel); emulator read-only; hint absent; Cancel discards name/cycle edits and closes; entry add/remove applies immediately and is unaffected by Cancel (FR-001–FR-005, FR-004a) — in `src/web-ui/src/pages/__tests__/QueuesPage.layout.spec.tsx`

### Implementation for User Story 1

- [ ] T005 [US1] Compose the Edit-Queue section via `QueueForm` slots in `QueuesPage`: pass `<QueueEntryList>` as the `entries` slot (row 4) and an **empty/placeholder** template-controls region as the `templateControls` slot (row 2) — this placeholder is replaced by `<QueueTemplateControls>` in T012; remove the standalone `queue-templates-section` block so ordering follows rows 1–5; keep `submitForm` committing only name + cycle execution and `closeForms` as Cancel; leave entry add/remove calling their services immediately — in `src/web-ui/src/pages/QueuesPage.tsx`

**Checkpoint**: Edit page shows the correct row order; Name/Emulator/Cycle/Save/Cancel behave per FR-001–FR-005 (the template row content arrives in US2).

---

## Phase 4: User Story 2 - Manage templates from a single inline row (Priority: P1)

**Goal**: Row 2 is a single template-controls row (template-name button + Save Template)
whose Save and Load panels expand inline between rows 2 and 3, default closed, only one
open at a time.

> **Scope note (FR-006)**: FR-006 lists three row-2 controls — template-name button,
> Save Template, **and Reload Template**. US2 delivers the row plus the name button and
> Save Template; the **Reload Template** button is added in US3 (T016). US2 therefore
> completes row 2 except for the Reload control.

**Independent Test**: On the edit page, row 2 shows the template name as a button plus a
Save Template button; clicking the name opens the Load section inline; triggering Save
opens the Save section in the same place; both are closed by default and only one is
open at a time; saving/loading updates the displayed template name.

### Tests for User Story 2

- [ ] T006 [P] [US2] `QueueTemplateControls` tests: both sections closed by default (FR-010); template-name button shows `(no template)` placeholder when none and opens the Load section (FR-007, FR-008); Save Template opens the Save section; opening one closes the other (FR-011); completing/dismissing collapses back to closed (FR-012) — in `src/web-ui/src/components/queues/__tests__/QueueTemplateControls.test.tsx`
- [ ] T007 [P] [US2] Update inline-section tests: assert the Save and Load sections render inline (no `modal-backdrop`/`aria-modal`) while preserving behavior — name validation + overwrite confirm (Save); list/empty-state/per-row delete (Load) — in `src/web-ui/src/components/queues/__tests__/SaveTemplateDialog.test.tsx` and `src/web-ui/src/components/queues/__tests__/TemplatePickerDialog.test.tsx`
- [ ] T008 [US2] Update `QueuesPage` template-wiring tests: associated template name pre-fills the inline Save section and updates after a successful save/load; Load disabled while the queue is Running (FR-013) — in `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx`

### Implementation for User Story 2

- [ ] T009 [P] [US2] Convert `SaveTemplateDialog` into an inline collapsible section (keep the file/component name): drop the `modal-backdrop`/`modal`/`aria-modal` wrappers, render a labeled `<section>`, expose Cancel/Close that calls `onClose` to collapse; keep name validation and the `template_exists` → overwrite-confirm flow unchanged — in `src/web-ui/src/components/queues/SaveTemplateDialog.tsx`
- [ ] T010 [P] [US2] Convert `TemplatePickerDialog` into an inline collapsible section (keep the file/component name): drop the modal wrappers, render a labeled `<section>`; keep the template list, empty state, and per-row Delete (with `ConfirmDeleteModal`) and the `loadDisabled` behavior unchanged — in `src/web-ui/src/components/queues/TemplatePickerDialog.tsx`
- [ ] T011 [US2] Create `QueueTemplateControls`: row with a template-name button (label `associatedTemplateName ?? '(no template)'`, opens Load) and a Save Template button (opens Save); `openSection: 'none' | 'save' | 'load'` state defaulting to `'none'` with mutual exclusion; render the inline Save/Load sections (from T009/T010) below the row; props `{ associatedTemplateName?, status, currentSequenceIds, onSaveTemplate, onLoadTemplate }` — in `src/web-ui/src/components/queues/QueueTemplateControls.tsx` (depends on T009, T010)
- [ ] T012 [US2] Wire `QueueTemplateControls` into the `QueuesPage` `templateControls` slot, replacing the T005 placeholder region: replace `loadedTemplateName` usage with `associatedTemplateName` set on successful save and load, pass `currentSequenceIds` from `detail.entries`, and remove the standalone `<SaveTemplateDialog>`/`<TemplatePickerDialog>` mounts (now owned by the controls) — in `src/web-ui/src/pages/QueuesPage.tsx` (depends on T011, T005)

**Checkpoint**: The template row + inline Save/Load sections work end-to-end; existing save/load/delete behavior preserved (Reload arrives in US3).

---

## Phase 5: User Story 3 - Reload the current template with confirmation (Priority: P2)

**Goal**: A Reload Template button re-applies the associated template's persisted
entries, prompting for confirmation only when the reload would change a non-empty queue;
disabled when no template is associated or the queue is running; reports when the
template is missing.

**Independent Test**: Load a template, edit entries, click Reload, confirm → entries
restored; click Reload again (now identical) → applies without a prompt; empty queue →
applies without a prompt; running queue → Reload disabled; deleted/renamed template →
"no longer available".

### Tests for User Story 3

- [ ] T013 [P] [US3] Unit tests for `sameSequenceOrder`: equal arrays, empty arrays, different lengths, different order, duplicates — in `src/web-ui/src/lib/__tests__/sequenceOrder.test.ts`
- [ ] T014 [P] [US3] Reload behavior tests in `QueuesPage`: Reload disabled when no associated template and when Running (FR-016/FR-017); identical entries → no prompt, no change; empty queue → applies without prompt; non-empty + divergent → confirmation shown and cancelable (FR-015); missing template → "Template '<name>' is no longer available", entries unchanged (FR-018) — in `src/web-ui/src/pages/__tests__/QueuesPage.reload.spec.tsx`

### Implementation for User Story 3

- [ ] T015 [P] [US3] Create the pure, order-sensitive `sameSequenceOrder(a: string[], b: string[]): boolean` helper — in `src/web-ui/src/lib/sequenceOrder.ts`
- [ ] T016 [US3] Add the Reload Template button to `QueueTemplateControls` (completing row 2 per FR-006): disabled when `!associatedTemplateName || status === 'Running'` (FR-016/FR-017); calls a new `onReload` prop — in `src/web-ui/src/components/queues/QueueTemplateControls.tsx` (depends on T011)
- [ ] T017 [US3] Implement the reload handler in `QueuesPage`: resolve the associated template by name via `listQueueTemplates()` (case-insensitive) → missing → surface "no longer available" (FR-018); else `getQueueTemplate(id)`, diff its `sequenceIds` against `detail.entries` via `sameSequenceOrder`; if non-empty and divergent set `pendingReload` and confirm via `ConfirmDeleteModal` (title "Reload template", confirm "Reload"), otherwise apply directly; apply reuses the existing `applyLoad` path; pass `onReload` to `QueueTemplateControls` — in `src/web-ui/src/pages/QueuesPage.tsx` (depends on T015, T016, T012)

**Checkpoint**: All three stories function independently; reload honors the diff-aware confirmation rule.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Styling and verification across the refined page.

- [ ] T018 [P] Add styles for the template-controls row (template-name button, action buttons) and the inline Save/Load section panels so they read as rows between rows 2 and 3 — in `src/web-ui/src/styles.css`
- [ ] T019 Run the full frontend suite and lint; confirm ≥80% line / ≥70% branch coverage on touched areas and zero lint errors (`npm --prefix src/web-ui test` and `npm --prefix src/web-ui run lint`)
- [ ] T020 Execute `specs/048-edit-queue-layout/quickstart.md` manual verification (row order, inline section toggling, reload diff/confirm/disabled/missing, Save/Cancel independence)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories (provides the `QueueForm` slots).
- **User Stories (Phase 3–5)**: All depend on Foundational.
  - US1 (layout composition) lands before US2 wiring: T012 depends on T005 (US2 replaces the T005 placeholder region with `QueueTemplateControls`).
  - US3 builds on US2's `QueueTemplateControls` (T016 depends on T011) and US1's page composition (T017 depends on T012).
- **Polish (Phase 6)**: After the desired stories are complete.

### User Story Dependencies

- **US1 (P1)**: After Foundational. Independently testable for row order + Save/Cancel scope (with a placeholder template region).
- **US2 (P1)**: After Foundational; T012 replaces the US1 placeholder region (depends on T005 and T011).
- **US3 (P2)**: After US2 (T016 depends on T011) and US1 page composition (T017 depends on T012); the `sameSequenceOrder` helper (T013/T015) is independent and parallelizable.

### Within Each User Story

- Write tests first and watch them fail, then implement (TDD per constitution); the
  Tests subsection is numbered before the Implementation subsection in each story.
- Section conversions (T009/T010) before the controls component (T011).
- Controls component (T011) before page wiring (T012) and before the Reload button (T016).
- Helper (T015) and Reload button (T016) before the reload handler (T017).

### Parallel Opportunities

- Foundational: T003 after T002.
- US2: tests T006 and T007 are [P]; implementations T009 and T010 are [P] (different files); then T011 → T012 are sequential.
- US3: tests T013 and T014 are [P]; helper T015 is [P]; then T016 → T017 are sequential.
- Polish: T018 is [P] with the others.

---

## Parallel Example: User Story 2

```bash
# Author the section/controls tests in parallel:
Task: "QueueTemplateControls tests in .../__tests__/QueueTemplateControls.test.tsx"   # T006
Task: "Inline-section tests in .../__tests__/{SaveTemplateDialog,TemplatePickerDialog}.test.tsx"  # T007

# Convert both dialogs to inline sections in parallel (different files):
Task: "Convert SaveTemplateDialog to inline section in .../SaveTemplateDialog.tsx"    # T009
Task: "Convert TemplatePickerDialog to inline section in .../TemplatePickerDialog.tsx" # T010
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 (Setup) and Phase 2 (Foundational — `QueueForm` slots).
2. Complete **US1** (ordered layout) → validate row order + Save/Cancel scope.
3. Complete **US2** (inline template row) → both P1 stories together form the
   shippable layout-refinement MVP (the template row is empty without US2).
4. **STOP and VALIDATE** the MVP, then proceed to US3.

### Incremental Delivery

1. Setup + Foundational → slots ready.
2. US1 + US2 (both P1) → refined, inline template-aware edit page (MVP).
3. US3 (P2) → Reload Template with diff-aware confirmation (completes row 2 per FR-006).
4. Polish → styling + full verification.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- This feature is frontend-only — no backend/API/persistence tasks (Save/Load and
  `PUT /api/queues/{id}/entries` already exist from feature 047).
- US1 and US2 are both P1 and tightly coupled (both touch `QueuesPage.tsx`); US3 is the
  cleanly separable increment.
- The two template components keep their existing names (`SaveTemplateDialog`,
  `TemplatePickerDialog`); only their rendering changes from modal to inline section.
- Verify tests fail before implementing; commit after each task or logical group.

---
description: "Task list for Queue Management Usability"
---

# Tasks: Queue Management Usability

**Input**: Design documents from `/specs/062-queue-management-usability/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Included — the project constitution (Testing Standards) requires tests for executable
logic, and the real green gate for web-ui is `vite build` + `jest`.

**Organization**: Tasks are grouped by user story. All work is in `src/web-ui`; the backend is
untouched. Note that `src/web-ui/src/pages/QueuesPage.tsx` is touched by all three stories, so tasks
editing it are NOT parallel with each other (same-file conflict).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3
- Exact file paths are included in each task

## Path Conventions

Web application, frontend only: source under `src/web-ui/src/`, tests under
`src/web-ui/src/**/__tests__/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the green baseline before changing behavior.

- [ ] T001 Establish baseline: from `src/web-ui` run `npm install` (if needed), `npm run build`, and `npm test`; confirm the suite is green so later diffs are attributable to this feature.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None required — there is no shared schema, model, or infrastructure to build. The three
user stories are independent UI changes and can begin immediately after Setup.

**Checkpoint**: No foundational work; proceed to user stories.

---

## Phase 3: User Story 1 - One-click template save when the name is unchanged (Priority: P1) 🎯 MVP

**Goal**: Saving back to the associated template (or under a genuinely new name) completes in one
action; only a collision with a *different* existing template still prompts to overwrite.

**Independent Test**: Edit a queue linked to template "Daily Farm", change an entry, click Save
Template without renaming → saved with no overwrite prompt. Rename to a different existing template
→ overwrite confirmation shown. Rename to a brand-new name → saved in one action.

### Tests for User Story 1 ⚠️

> Write/adjust these FIRST and ensure they fail before implementation.

- [ ] T002 [P] [US1] Update `src/web-ui/src/components/queues/__tests__/SaveTemplateDialog.test.tsx`: assert that when the typed name equals `originName` (trimmed, case-insensitive) Save persists immediately with `overwrite: true` and shows NO overwrite confirmation; when the name differs and the API returns 409, the overwrite confirmation still appears; and that an empty/invalid name (FR-009) blocks the save (shows the validation error, no `onSave` call) on both the one-click and the differing-name paths.
- [ ] T003 [P] [US1] Update `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx`: assert one-click save (no prompt) for the associated-name and brand-new-name cases, and that the overwrite confirmation path still works for a different existing template.

### Implementation for User Story 1

- [ ] T004 [US1] In `src/web-ui/src/components/queues/SaveTemplateDialog.tsx`, change `handleSave` so that after validation, if the trimmed name matches `originName` (case-insensitive) it calls `persist(true)` directly (one-click overwrite of the associated template); otherwise it calls `persist(false)` and keeps the existing 409 → overwrite-confirmation branch for collisions with a different existing template. Brand-new names continue to succeed with `overwrite: false`.
- [ ] T005 [US1] Verify `handleSaveTemplate` in `src/web-ui/src/pages/QueuesPage.tsx` still passes `overwrite` through unchanged to `saveQueueTemplate` and updates `associatedTemplateName` on success (no behavior change needed beyond confirming compatibility with T004).

**Checkpoint**: Re-saving the associated template is one click; destructive renames still guarded. Run `npm test`.

---

## Phase 4: User Story 2 - Save confirmation appears where the user clicked Save (Priority: P1)

**Goal**: Both the queue Save and the template Save show their success/failure result inline at the
control the user clicked, replacing the page-top status banner for these saves.

**Independent Test**: Save the queue → confirmation appears at the queue form's Save row. Save a
template → confirmation appears at the template controls' Save Template row. Force a failure → error
appears at the same control.

### Tests for User Story 2 ⚠️

- [ ] T006 [P] [US2] Update `src/web-ui/src/components/queues/__tests__/QueueTemplateControls.test.tsx`: assert a success message renders at the template controls after a successful template save, and an error renders there on failure.
- [ ] T007 [US2] Update `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx`: assert the queue-save and template-save success confirmations render at their respective Save controls and that the page-top banner is NOT used for these two saves (other table messages — start/stop/load/delete — remain unchanged). *(Same file as T003; do not run in parallel with it.)*

### Implementation for User Story 2

- [ ] T008 [P] [US2] In `src/web-ui/src/components/queues/QueueForm.tsx`, render an inline status/error region at the Save/Cancel action row driven by a new optional prop (e.g. `saveResult?: { kind: 'success' | 'error'; message: string }`) with `role="status"`/`role="alert"` respectively.
- [ ] T009 [P] [US2] In `src/web-ui/src/components/queues/QueueTemplateControls.tsx`, render an inline status/error region at the Save Template row driven by a new optional prop (e.g. `saveResult?: { kind: 'success' | 'error'; message: string }`).
- [ ] T010 [US2] In `src/web-ui/src/pages/QueuesPage.tsx`, add state for the queue-save result and the template-save result; set them in `submitForm` (success/error for queue create+update) and in `handleSaveTemplate` (success/error for template save) instead of routing those outcomes through `setTableMessage`; pass the results into `QueueForm` and `QueueTemplateControls`; clear them when forms re-open. Leave `tableMessage` for the unrelated start/stop/load/delete flows.

**Checkpoint**: Both saves confirm at the click site; page-top banner no longer used for them. Run `npm test`.

---

## Phase 5: User Story 3 - Remove the Sequences column from the queues overview (Priority: P2)

**Goal**: The overview table no longer shows the Sequences (entry-count) column; all other columns
and row actions keep working, including full-width expandable rows.

**Independent Test**: Open Queues → no Sequences column; Name/Emulator/Cycle/Status/Actions present;
the live-schedule row still spans the table correctly.

### Tests for User Story 3 ⚠️

- [ ] T011 [P] [US3] Update `src/web-ui/src/pages/__tests__/QueuesPage.layout.spec.tsx`: assert the Sequences header/cell is absent; that the remaining columns (Name, Emulator, Cycle, Status, Actions) are present; that the row actions (Start/Stop/Schedule/Edit/Delete) still render (FR-002); and that the expanded live-schedule row spans the full table (its `colSpan` matches the new 5-column count, not 6).

### Implementation for User Story 3

- [ ] T012 [US3] In `src/web-ui/src/pages/QueuesPage.tsx`, remove the `<th>Sequences</th>` header and the `<td>{q.entryCount}</td>` cell, and change the `colSpan={6}` on the loading, empty, and live-schedule rows to `colSpan={5}`.

**Checkpoint**: Overview is decluttered and layout intact. Run `npm test`.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across all stories.

- [ ] T013 Run the full web-ui gate from `src/web-ui`: `npm run build` and `npm test`; ensure green.
- [ ] T014 Execute `specs/062-queue-management-usability/quickstart.md` scenarios A, B, and C manually against the running web-ui.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: None.
- **User Stories (Phases 3–5)**: All depend only on Setup. They are independent in behavior but share
  `QueuesPage.tsx`, so the QueuesPage edits (T005, T010, T012) must be serialized relative to each
  other.
- **Polish (Phase 6)**: After the desired stories are complete.

### User Story Dependencies

- **US1 (P1)**: Independent. MVP.
- **US2 (P1)**: Independent of US1; both can be demoed separately.
- **US3 (P2)**: Independent of US1/US2.

### Within Each User Story

- Tests written/updated and failing before implementation.
- Component changes before the QueuesPage wiring that consumes them (T008/T009 before T010).

### Parallel Opportunities

- T002 and T003 (US1 tests, different files) can run in parallel.
- T006 can run in parallel with US1/US3 work (different file); T007 shares a file with T003.
- T008 and T009 (different component files) can run in parallel; T010 (QueuesPage) must follow them.
- T011 (US3 test, different file) can run in parallel with US1/US2 component work.
- The three QueuesPage edits (T005, T010, T012) must NOT run in parallel with each other.

---

## Parallel Example: User Story 2

```bash
# Component edits in different files can proceed together:
Task: "Inline save-result region in src/web-ui/src/components/queues/QueueForm.tsx"        # T008
Task: "Inline save-result region in src/web-ui/src/components/queues/QueueTemplateControls.tsx"  # T009
# Then wire them up in QueuesPage (single file, sequential):
Task: "Set + pass queue/template save results in src/web-ui/src/pages/QueuesPage.tsx"      # T010
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → baseline green.
2. Phase 3 US1 → re-saving the associated template is one click.
3. **STOP and VALIDATE**: quickstart Scenario B. Demo if ready.

### Incremental Delivery

1. Setup → baseline.
2. US1 → one-click save (MVP) → validate Scenario B.
3. US2 → co-located confirmations → validate Scenario C.
4. US3 → column removed → validate Scenario A.
5. Polish → full build/test gate + quickstart.

---

## Notes

- All changes are frontend-only under `src/web-ui`; no backend, route, or contract changes.
- `QueuesPage.tsx` is the one shared file — serialize T005, T010, T012.
- The real green gate is `vite build` + `jest`; lint and `tsc --noEmit` have pre-existing failures
  and are not the gate.
- Commit after each task or logical group; keep the suite green before each commit (constitution).

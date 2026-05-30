# Tasks: Sequence Loop Step Management

**Input**: Design documents from `specs/037-loop-step-management/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, quickstart.md ✅

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: Install new dependency required by US2

- [ ] T001 Install `@dnd-kit/core @dnd-kit/sortable @dnd-kit/utilities` in `src/web-ui/` via `npm install`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared DnD infrastructure types and the drag-item wrapper component that both US2 phases (SortableSequenceStepList and LoopBlock refactor) depend on.

**⚠️ CRITICAL**: T007 and T008 in Phase 4 both depend on T003 being complete.

- [ ] T002 Add `StepDragData` type `{ scopeId: string; type: 'step' }` to `src/web-ui/src/types/stepEntry.ts`
- [ ] T003 Create `src/web-ui/src/components/SortableStepItem.tsx` — wraps a step row with `useSortable({ id, data: { scopeId, type: 'step' } })`; applies dnd-kit `transform`/`transition` styles; accepts `id: string`, `scopeId: string`, `disabled?: boolean`, `children: React.ReactNode`; renders a visible drag handle (e.g., `⠿` grip icon) so users can see and interact with the drag affordance (addresses SC-004)
- [ ] T003a Create `src/web-ui/src/components/__tests__/SortableStepItem.test.tsx` — unit tests covering: (a) renders children; (b) renders drag handle icon; (c) applies `transform`/`transition` style from useSortable (mock dnd-kit hooks); (d) applies disabled state correctly; (e) passes `scopeId` and `type: 'step'` in useSortable data (constitution: ≥80% line / ≥70% branch coverage for new module)

**Checkpoint**: Shared drag infrastructure ready — US1 (Phase 3) and US2 (Phase 4) can now proceed.

---

## Phase 3: User Story 1 — Add Step Outside a Loop (Priority: P1) 🎯 MVP

**Goal**: A persistent "Add step" button at the very bottom of the sequence step list always appends a blank action step at the top-level scope, regardless of whether loops are present.

**Independent Test**: Open a sequence containing a loop. Click the new "Add step" button at the bottom. Verify a new blank action step appears at the end of the top-level sequence, not inside the loop. Verify the loop's internal "Add step" button still adds steps inside the loop only.

### Implementation for User Story 1

- [ ] T004 [US1] Add `handleAddTopLevelStep()` function to `SequencesPage.tsx` — creates a blank `SequenceStep` with `stepType: 'Action'` and `actionType: 'command'` (same pattern as `createLoopStep` which returns `SequenceStep`); appends it to `form.steps` via `setForm`; follow existing `nextGeneratedStepId` helper for ID generation
- [ ] T005 [US1] In `SequencesPage.tsx` create-sequence form section (after the `<ReorderableList>` at ~line 1267), add `<button type="button" data-testid="add-top-level-step" onClick={handleAddTopLevelStep} disabled={submitting || loading}>Add step</button>`
- [ ] T006 [US1] In `SequencesPage.tsx` edit-sequence form section (after the `<ReorderableList>` at ~line 1495), add the same persistent "Add step" button wired to `handleAddTopLevelStep`

**Checkpoint**: US1 is fully functional. User can now add top-level steps when a loop is present. Test independently before proceeding to US2.

---

## Phase 4: User Story 2 — Scope-Constrained DnD Reordering (Priority: P2)

**Goal**: Replace the ↑/↓ button-based reordering with drag-and-drop. Top-level steps can be dragged to reorder among top-level items (including past loop blocks). Loop-body steps can be dragged to reorder within the loop. Cross-scope drops are rejected with a visual "not allowed" indicator.

**Independent Test**: In a sequence with top-level steps [A, Loop, B]: (1) drag step B before the Loop — sequence becomes [A, B, Loop]; (2) attempt to drag a top-level step into the loop body — a "not allowed" visual appears and the step snaps back; (3) drag loop-body steps to reorder — top-level order is unchanged.

### Implementation for User Story 2

- [ ] T007 [P] [US2] Create `src/web-ui/src/components/SortableSequenceStepList.tsx` — accepts `steps: SequenceStep[]`, `onDelete`, `disabled`, `isDragInvalid: boolean` (no `onChange` — reordering is handled entirely by `DndContext.onDragEnd` in `SequencesPage`, not by this component); wraps items in a `SortableContext` (ids = step ids, each with `scopeId = "root"` in data); renders each step in a `SortableStepItem`; for Loop steps renders `LoopBlock` as child content and passes `isDragInvalid` as `isDropInvalid` to `LoopBlock`; does NOT own `DndContext` (that lives in SequencesPage)
- [ ] T008 [US2] Refactor `src/web-ui/src/components/sequences/LoopBlock.tsx` — remove the `move()` helper and `handleBodyReorder()` function; remove the ↑/↓ `<button>` elements (lines 124–125); wrap the `<ol>` body in a `SortableContext` (ids = body step ids, `scopeId = loop.id`); render each body step `<li>` inside a `SortableStepItem` with `scopeId={loop.id}`; preserve `handleAddBodyStep`, `handleAddBreakStep`, and their buttons unchanged; add `isDropInvalid?: boolean` prop; apply CSS class `loop-block--drop-invalid` to the `loop-block__body` div when `isDropInvalid` is true
- [ ] T009 [US2] Add `activeStepId: string | null` and `isDragInvalid: boolean` state to `SequencesPage.tsx`; implement `handleDragStart`, `handleDragEnd`, `handleDragOver`, `handleDragCancel` handlers: `handleDragStart` sets `activeStepId`; `handleDragEnd` compares `active.data.current.scopeId` vs `over?.data.current?.scopeId` — if matching, performs reorder in `form.steps` via array splice; always clears both state vars; `handleDragCancel` clears both state vars; `handleDragOver` sets `isDragInvalid = true` when active and over scopes differ
- [ ] T010 [US2] Wrap the step list section in `SequencesPage.tsx` create-sequence form in `<DndContext onDragStart={handleDragStart} onDragEnd={handleDragEnd} onDragOver={handleDragOver} onDragCancel={handleDragCancel}>`; replace `<ReorderableList ... />` (~line 1254) with `<SortableSequenceStepList steps={form.steps} onDelete={(item) => { setForm(prev => ({ ...prev, steps: prev.steps.filter(s => s.id !== item.id) })); setDirty(true); }} disabled={submitting || loading} isDragInvalid={isDragInvalid} />`
- [ ] T011 [US2] Wrap the step list section in `SequencesPage.tsx` edit-sequence form (~line 1495) in `<DndContext onDragStart={handleDragStart} onDragEnd={handleDragEnd} onDragOver={handleDragOver} onDragCancel={handleDragCancel}>`; replace `<ReorderableList ... />` with `<SortableSequenceStepList steps={form.steps} onDelete={(item) => { setForm(prev => ({ ...prev, steps: prev.steps.filter(s => s.id !== item.id) })); setDirty(true); }} disabled={submitting || loading} isDragInvalid={isDragInvalid} />`
- [ ] T012 [P] [US2] Add `.loop-block--drop-invalid { outline: 2px solid #d32f2f; opacity: 0.7; }` CSS rule to the project stylesheet (`src/web-ui/src/index.css` or equivalent)

**Checkpoint**: US2 is fully functional. Drag-and-drop reordering works at both nesting levels. Cross-scope drops are rejected visually. Test independently.

---

## Phase 5: User Story 3 — Preserve In-Loop Step Addition (Priority: P3)

**Goal**: Verify that the LoopBlock refactor in Phase 4 (T008) preserves the existing "Add step" and "Add break" functionality for loop bodies without regression.

**Independent Test**: Add a loop to a sequence. Use the loop's internal "Add step" button. Verify the new step appears inside the loop body. Use "Add break" — verify it adds a break step inside the loop body. Existing LoopBlock unit tests must pass.

### Implementation for User Story 3

- [ ] T013 [US3] Update `src/web-ui/src/components/sequences/__tests__/LoopBlock.test.tsx` (create file if it doesn't exist) — add/update tests to verify: (a) "Add step" button adds an action step to the loop body; (b) "Add break" button adds a break step; (c) `isDropInvalid=true` prop applies `loop-block--drop-invalid` CSS class; (d) ↑/↓ buttons are absent; existing passing tests remain green

**Checkpoint**: US3 preserved — in-loop step addition works identically to before the refactor.

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] T014 [P] Update form hint text in `SequencesPage.tsx` — change `"Steps execute in listed order; drag buttons to reorder before saving."` (appears in both create and edit forms) to `"Steps execute in listed order; drag to reorder within the same level."`
- [ ] T015 Add unit tests for `SortableSequenceStepList.tsx` in `src/web-ui/src/components/__tests__/SortableSequenceStepList.test.tsx` — cover: renders step list items; `isDragInvalid=true` passes `isDropInvalid=true` to `LoopBlock`; `onDelete` callback fires with the correct step; loop steps render `LoopBlock` as child content
- [ ] T016 Run `npm test` in `src/web-ui/` and fix any failing tests or coverage regressions introduced by the DnD refactor
- [ ] T017 [P] Add Playwright E2E test in `src/web-ui/tests/sequences-loop-steps.spec.ts` covering US1 (add step after loop at top level) and US2 (drag step past loop; attempt cross-scope drag and verify snap-back)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 (npm install must complete before importing dnd-kit)
- **Phase 3 (US1)**: Depends on Phase 2 — but US1 uses only React state, not dnd-kit directly; T004–T006 do not import `SortableStepItem`
- **Phase 4 (US2)**: Depends on Phase 2 (T003 `SortableStepItem` must exist) — T007 and T008 can run in parallel; T009–T011 depend on T007 and T008
- **Phase 5 (US3)**: Depends on Phase 4 (T008 LoopBlock refactor must be complete)
- **Polish**: Depends on Phases 3–5

### User Story Dependencies

- **US1 (P1)**: No dependency on US2 or US3 — can be built and demoed independently after Phase 2
- **US2 (P2)**: Depends on Phase 2 (SortableStepItem); no dependency on US1
- **US3 (P3)**: Depends on US2 (T008 LoopBlock refactor)

### Within Phase 4 (US2)

```
T007 (SortableSequenceStepList) ─┐
                                  ├── T010 (create form DndContext)
T008 (LoopBlock DnD refactor) ───┤
                                  ├── T011 (edit form DndContext)
T009 (handlers in SequencesPage)─┘
T012 (CSS) — independent [P]
```

### Parallel Opportunities

- T007 and T008 can run in parallel (different files, both depend only on T003)
- T009 (handlers) can be authored in parallel with T007/T008 (no runtime dependency until T010/T011)
- T012 (CSS) is fully independent and can be done any time after T001
- T014 (hint text), T015 (unit tests), T017 (E2E tests) can all run in parallel in Polish phase

---

## Parallel Example: Phase 4 (US2)

```
# After T003 completes, launch these in parallel:
Task T007: Create SortableSequenceStepList.tsx
Task T008: Refactor LoopBlock.tsx for DnD
Task T009: Implement DnD event handlers in SequencesPage.tsx
Task T012: Add CSS for loop-block--drop-invalid

# After T007, T008, T009 complete:
Task T010: Wire DndContext into create form (SequencesPage.tsx)
Task T011: Wire DndContext into edit form (SequencesPage.tsx)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Install deps
2. Complete Phase 2: Create SortableStepItem (needed later for US2)
3. Complete Phase 3: US1 (T004–T006) — the "Add step" button
4. **STOP and VALIDATE**: Verify top-level step addition works in the browser
5. Demo if ready — this alone resolves the blocking UX gap

### Incremental Delivery

1. Phase 1–3 → **MVP**: Top-level "Add step" button works
2. Phase 4 (US2) → DnD reordering with scope constraints
3. Phase 5 (US3) → Regression tests confirming no regression
4. Polish → Updated hint text, full test suite, E2E coverage

---

## Notes

- [P] tasks = different files, no shared-state dependencies
- The `ReorderableList` component is intentionally left unchanged (used by `CommandForm`)
- `SortableStepItem` must not import from `SequencesPage` — it is a generic DnD wrapper
- `SortableSequenceStepList` must NOT own `DndContext` — only `SequencesPage` does, so cross-component drag events work
- Scope enforcement lives exclusively in `onDragEnd` in `SequencesPage.tsx` — no scope logic inside individual components
- `SortableSequenceStepList` has no `onChange` prop — reordering state updates live in `DndContext.onDragEnd` in `SequencesPage` only
- After T010/T011, the existing `onAdd` prop of `ReorderableList` is no longer called for sequences; the new "Add step" button from Phase 3 takes over that role

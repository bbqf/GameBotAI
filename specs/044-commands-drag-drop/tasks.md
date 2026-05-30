# Tasks: Drag and Drop for Command Steps

**Input**: Design documents from `/specs/044-commands-drag-drop/`
**Prerequisites**: plan.md ✅, spec.md ✅

**Organization**: Tasks grouped by user story. US1 (P1) is the complete MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to

---

## Phase 1: Setup

**Purpose**: Verify the baseline before any changes.

- [ ] T001 Run the existing test suite from `src/web-ui/` to confirm all tests pass before changes (`npm test` or `npx vitest run`)

---

## Phase 2: Foundational — Wire DnD into CommandForm

**Purpose**: Core implementation change in `CommandForm.tsx` plus pre-flight checks. Must be complete before any user story test can be written or validated.

**⚠️ CRITICAL**: US1 and US2 both depend on this phase.

- [ ] T002 Search the project for all files importing from `ReorderableList` (grep `from.*ReorderableList` in `src/web-ui/src/`) to confirm `CommandForm.tsx` is the only consumer of the `ReorderableList` component before modifying its import; document any other usages found
- [ ] T003 In `src/web-ui/src/components/commands/CommandForm.tsx`: remove the `ReorderableList` component from the import (keeping `import type { ReorderableListItem } from '../ReorderableList'`); add imports for `DndContext`, `DragEndEvent`, `DragOverEvent`, `DragStartEvent`, `PointerSensor`, `useSensor`, `useSensors` from `@dnd-kit/core`; add import for `arrayMove` from `@dnd-kit/sortable`; add import for `SortableSequenceStepList` from `../SortableSequenceStepList`
- [ ] T004 In `src/web-ui/src/components/commands/CommandForm.tsx`: add `activeStepId` and `overId` state (`useState<string | null>(null)`); add `sensors` via `useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }))`; add `handleDragStart`, `handleDragOver`, `handleDragEnd`, `handleDragCancel` handlers (see plan.md for exact handler bodies)
- [ ] T005 In `src/web-ui/src/components/commands/CommandForm.tsx`: replace the `<ReorderableList>` JSX element with `<DndContext>` (sensors, onDragStart/Over/End/Cancel) wrapping `<SortableSequenceStepList>` (items, onDelete, disabled, emptyMessage, activeId, overId); remove the `updateStepOrder` function (now dead code)
- [ ] T006 [US2] In `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: fix existing bug — remove `actionOptions={[]}` prop from both zoom test cases (prop does not exist on current `CommandFormProps`); confirm tests compile and pass before writing new tests

**Checkpoint**: Build must succeed with zero TypeScript errors and all pre-existing tests must pass before proceeding.

---

## Phase 3: User Story 1 — Drag to Reorder Steps (Priority: P1) 🎯 MVP

**Goal**: A user can drag a command step to a new position; the order updates immediately and persists on save.

**Independent Test**: Open a command with 2+ steps in the editor. Grab the drag handle (⠿) on any step, drag it above or below another step, release. The step should appear in the new position. Save and reopen — order must be preserved.

- [ ] T007 [US1] In `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: add test that renders `CommandForm` with two steps and verifies `onChange` is called with the steps in swapped order after `handleDragEnd` is invoked with matching active/over IDs (invoke the `onDragEnd` prop of `DndContext` via the rendered component's internal handler)
- [ ] T008 [US1] In `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: add test that verifies no `onChange` call occurs when drag is cancelled (`handleDragCancel`) or when active.id equals over.id
- [ ] T009 [US1] In `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: add test that verifies drag handles are inert (disabled) when `CommandForm` receives `submitting={true}`
- [ ] T010 [P] [US1] In `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: add test that simulates `handleDragStart` and then asserts the `DropIndicator` element (or its containing node) is present in the DOM, confirming the drop indicator renders during an active drag
- [ ] T011 [P] [US1] In `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: add test that renders `CommandForm` with exactly one step, fires `handleDragEnd` with active.id equal to over.id (or with no over), and asserts `onChange` is not called (single-step no-op edge case)
- [ ] T012 [P] [US1] In `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: add regression test that renders `CommandForm` with two steps, clicks the Delete button on one step, and asserts `onChange` is called with the remaining step — confirming delete functionality is intact after the DnD swap

**Checkpoint**: All T007–T012 tests must pass. At this point US1 is fully functional and testable.

---

## Phase 4: User Story 2 — Consistent Interaction Pattern (Priority: P2)

**Goal**: The drag handle icon and drop indicator in the command editor match the sequences editor visually and behaviourally.

**Independent Test**: Render `CommandForm` with steps and confirm the ⠿ drag handle icon appears on each step row. Compare `SortableStepItem` render output between sequence and command contexts — same component, same markup.

- [ ] T013 [P] [US2] In `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: add test that renders `CommandForm` with at least one step and asserts the drag handle (`aria-label="Drag to reorder"` or text content `⠿`) appears once per step

**Checkpoint**: T013 passes confirming visual consistency. All existing zoom tests still pass.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cleanup.

- [ ] T014 Run full Vitest test suite from `src/web-ui/` and confirm zero failures and no coverage regressions
- [ ] T015 Manual UI smoke test: start dev server, open a command with 3+ steps, drag each step to a different position, save, reload and verify order persisted; also confirm arrow buttons no longer appear

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — run immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — blocks all user stories
- **US1 (Phase 3)**: Depends on Phase 2 completion (T006 must complete before T007–T012)
- **US2 (Phase 4)**: Depends on Phase 2 completion — can run in parallel with Phase 3 after T006
- **Polish (Phase 5)**: Depends on Phases 3 and 4

### User Story Dependencies

- **US1 (P1)**: No dependency on US2 — fully independent after Foundational
- **US2 (P2)**: No dependency on US1 — T013 can be written in parallel with T007–T012

### Within Each Phase

- T002 → T003 → T004 → T005 → T006 (sequential: each builds on the previous)
- T007, T008, T009 are sequential relative to T006 but independent of each other [P eligible]
- T010, T011, T012 are independent of each other and of T007–T009 [P eligible]
- T013 is independent of T007–T012 [P eligible]

---

## Parallel Opportunities

```text
# After T006 completes, story test tasks can run in parallel:

Phase 3 (US1 tests — all independent):    Phase 4 (US2):
T007 drag-reorder test                    T013 drag handle visibility test
T008 drag-cancel / no-op test
T009 disabled state test
T010 DropIndicator during drag test
T011 single-step no-op edge case
T012 delete regression test
```

---

## Implementation Strategy

### MVP (User Story 1 Only)

1. Complete Phase 1: verify baseline
2. Complete Phase 2: T002 → T003 → T004 → T005 → T006 (grep check, imports, handlers, JSX swap, test file fix)
3. Complete Phase 3: T007–T012 (US1 tests)
4. **STOP and VALIDATE**: drag reordering works end-to-end
5. Ship if sufficient

### Full Delivery

1. MVP above
2. Phase 4: T013 (US2 visual consistency test)
3. Phase 5: T014 (full suite), T015 (manual smoke)

---

## Notes

- No new npm packages required — `@dnd-kit/core` and `@dnd-kit/sortable` are already installed
- `SortableSequenceStepList` and `SortableStepItem` are reused without modification
- `ReorderableList` component itself is **not deleted** — only its usage in `CommandForm` is replaced
- The `toStepItems` mapping function in `CommandForm.tsx` is unchanged
- `closestCenter` (dnd-kit default) is correct for a flat list; no custom collision detection needed
- T006 (actionOptions bug fix) is in Phase 2 so the test file compiles cleanly before any new test cases are added in Phase 3

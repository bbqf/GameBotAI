# Implementation Plan: Sequence Loop Step Management

**Branch**: `037-loop-step-management` | **Date**: 2026-05-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/037-loop-step-management/spec.md`

## Summary

Users cannot currently add steps at the top-level scope of a sequence once a loop has been added, and step reordering uses button-based ↑/↓ controls with no scope enforcement. This feature adds a persistent "Add step" button at the bottom of the sequence editor (appending top-level steps) and replaces the ↑/↓ controls with drag-and-drop reordering (using `@dnd-kit/core` + `@dnd-kit/sortable`) that enforces scope boundaries — top-level steps cannot be dragged into a loop, and loop-body steps cannot be dragged out.

## Technical Context

**Language/Version**: TypeScript 5.6.3, React 18.3.1
**Primary Dependencies**: React 18, `@dnd-kit/core` ^6.x (new), `@dnd-kit/sortable` ^8.x (new), `@dnd-kit/utilities` ^3.x (new)
**Storage**: N/A — UI-only feature; no backend changes
**Testing**: Jest 29.7.0 (unit/component), Playwright 1.49.1 (E2E)
**Target Platform**: Web browser (Chrome/Edge), desktop viewport
**Project Type**: Web application — React frontend
**Performance Goals**: Drag-and-drop interactions at 60fps; no perceptible lag for sequences up to 50 steps
**Constraints**: `ReorderableList` component must remain unchanged (used by `CommandForm` and potentially other views); no changes to backend API or data serialization
**Scale/Scope**: Single-user authoring session; sequences typically < 20 steps

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| Build/CI green | ✅ Pass | No current failures on master |
| Lint/format clean | ✅ Pass | New code must pass existing ESLint config |
| Method naming (CamelCase only) | ✅ Pass | New methods must use CamelCase per constitution |
| Test coverage ≥80% line / ≥70% branch | ⚠️ Required | New components need unit tests; LoopBlock changes need updated tests |
| Performance goal declared | ✅ Pass | 60fps DnD declared above |
| No new unused dependencies | ✅ Pass | All three `@dnd-kit` packages are used |

*Post-Phase-1 re-check*: No violations found after design. All gates remain green.

## Project Structure

### Documentation (this feature)

```text
specs/037-loop-step-management/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
└── tasks.md             ← Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code

```text
src/web-ui/src/
├── components/
│   ├── SortableSequenceStepList.tsx    NEW — DnD-based step list for sequence editors
│   ├── SortableStepItem.tsx            NEW — useSortable wrapper for individual steps
│   ├── ReorderableList.tsx             UNCHANGED — button-based, kept for CommandForm etc.
│   └── sequences/
│       └── LoopBlock.tsx               MODIFY — replace ↑/↓ with DnD sortable body
└── pages/
    └── SequencesPage.tsx               MODIFY — add DndContext, use SortableSequenceStepList,
                                                  add bottom "Add step" button
```

**CSS** (wherever global styles live, e.g., `src/web-ui/src/index.css` or a component CSS file):
- Add `.loop-block--drop-invalid` class for the "not allowed" visual during drag

**Structure Decision**: Web application, frontend only. Backend unchanged.

## Implementation Phases

### Phase 1: New DnD Components

**Goal**: Create `SortableStepItem` and `SortableSequenceStepList` as standalone components.

**`SortableStepItem.tsx`**:
- Wraps any step with `useSortable({ id, data: { scopeId, type: 'step' } })`
- Exposes a drag handle (or makes the entire row draggable)
- Applies `transform` / `transition` styles from `useSortable`
- Props: `id: string`, `scopeId: string`, `children: React.ReactNode`, `disabled?: boolean`

**`SortableSequenceStepList.tsx`**:
- Accepts `steps: SequenceStep[]`, `onChange`, `onDelete`, `disabled`
- Wraps the list in `SortableContext` with `scopeId = "root"` items
- Renders each step in a `SortableStepItem`
- For `Loop` steps, renders `LoopBlock` as the item content (same as current `ReorderableList` `details` approach)
- Does NOT own `DndContext` — that lives in `SequencesPage` to allow cross-component drag events

### Phase 2: Modify LoopBlock for DnD Body

**Goal**: Replace the ↑/↓ reorder buttons inside `LoopBlock` with a sortable body using dnd-kit.

**Changes to `LoopBlock.tsx`**:
- Remove `move()` helper and `handleBodyReorder()`
- Wrap `<ol>` body in `SortableContext` with items tagged `scopeId = loop.id`
- Each body step `<li>` becomes a `SortableStepItem` with `scopeId = loop.id`
- Remove the ↑ / ↓ `<button>` elements (lines 124–125)
- Accept `isDropInvalid?: boolean` prop; apply `loop-block--drop-invalid` CSS class to the body div when true

### Phase 3: Update SequencesPage

**Goal**: Wire up `DndContext`, replace `ReorderableList` with `SortableSequenceStepList`, add the persistent "Add step" button.

**Changes to `SequencesPage.tsx`** (both create form ~line 1254 and edit form ~line 1495):

1. **Add state**:
   ```
   const [activeStepId, setActiveStepId] = useState<string | null>(null);
   const [isDragInvalid, setIsDragInvalid] = useState(false);
   ```

2. **Add `DndContext` wrapping** the step list section:
   - `onDragStart`: set `activeStepId`
   - `onDragOver`: compare `active.data.current.scopeId` vs `over?.data.current?.scopeId`; set `isDragInvalid` if different
   - `onDragEnd`: if scopes match, perform reorder (update `form.steps`); always clear `activeStepId` and `isDragInvalid`
   - `onDragCancel`: clear both state variables

3. **Replace `<ReorderableList>` with `<SortableSequenceStepList>`** passing `isDragInvalid` through to `LoopBlock`

4. **Add persistent "Add step" button** after `</SortableSequenceStepList>`:
   - Creates a blank action step (same structure as `handleAddBodyStep` in `LoopBlock`) appended to `form.steps`
   - `data-testid="add-top-level-step"`

5. **Update form hint** text (line 1268) from `"drag buttons to reorder"` to `"drag steps to reorder"`

### Phase 4: CSS and Visual Feedback

Add to the project's CSS:
```css
.loop-block--drop-invalid {
  outline: 2px solid #d32f2f;
  opacity: 0.7;
}
```

Optionally add a custom `DragOverlay` in `SequencesPage` for a floating "ghost" step label during drag.

### Phase 5: Tests

**Unit tests** (`SortableSequenceStepList.test.tsx`):
- Renders steps and a loop block
- Top-level "Add step" button appends a step to the sequence
- `onDelete` removes the correct step

**Unit tests** (update `LoopBlock.test.tsx`):
- Existing "Add step inside loop" behavior unchanged
- ↑/↓ buttons absent in new implementation
- `isDropInvalid` prop applies the CSS class

**E2E** (`sequences-loop-steps.spec.ts`):
- User story 1: add a step at the top level when a loop is present → verify step is outside the loop
- User story 2: drag a top-level step past a loop → verify reorder is correct
- FR-007/FR-008: attempt to drag a top-level step over a loop body → verify "not allowed" CSS class appears and step stays at top level after release

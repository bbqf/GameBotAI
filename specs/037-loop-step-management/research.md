# Research: Sequence Loop Step Management

**Branch**: `037-loop-step-management` | **Date**: 2026-05-30

## Decision 1: Drag-and-Drop Library

**Decision**: Use `@dnd-kit/core` + `@dnd-kit/sortable` + `@dnd-kit/utilities`

**Rationale**:
- TypeScript-first, composable hooks-based API; no opinionated DOM structure
- Multiple independent `SortableContext` instances can share one `DndContext` — exactly the pattern needed for scope-constrained reordering (top-level and loop body as separate contexts)
- Custom `data` payload on draggable items makes scope comparison straightforward in `onDragEnd`
- Active maintenance, good React 18 compatibility

**Alternatives considered**:
- **Native HTML5 DnD API**: No third-party cost, but cross-browser inconsistency (Firefox vs Chrome scroll behavior), no built-in sortable helpers, complex visual feedback implementation. Rejected due to implementation complexity for scope constraints.
- **`react-beautiful-dnd`**: Well-known but officially deprecated. Each `Droppable` is isolated, which actually maps well to scope constraint but the project is unmaintained. Rejected.
- **`@hello-pangea/dnd`** (maintained fork of react-beautiful-dnd): Viable alternative; similar scope isolation model. Less composable for custom collision detection. Could serve as fallback.

---

## Decision 2: Scope Enforcement Strategy

**Decision**: Tag each draggable step with a `scopeId` string. Validate scope match in `onDragEnd`; apply CSS class on cross-scope `onDragOver`.

**Rationale**:
- Top-level steps get `scopeId = "root"`. Loop body steps get `scopeId = <loop.id>`.
- dnd-kit allows arbitrary `data` attached to `useSortable`'s `data` prop, propagated in `active.data.current` and `over.data.current` during drag events.
- `onDragEnd`: if `active.data.current.scopeId !== over.data.current.scopeId`, return early without reordering.
- `onDragOver`: compare same fields; if mismatch, add a CSS modifier class (e.g., `loop-block--drop-invalid`) to the target container for the "not allowed" visual.
- When invalid drop occurs (mouse released over invalid target), dnd-kit automatically keeps the item in its original position — no explicit snap-back code needed.

**Alternatives considered**:
- **Separate `DndContext` per scope**: Would prevent cross-scope drag entirely at the DnD level (no `over` event across contexts). Simpler but gives no visual feedback during drag; user sees nothing wrong until they release. Rejected in favor of visible "not allowed" indicator.
- **`modifiers` array**: dnd-kit modifiers restrict pointer movement, not drop targets. Not suited for scope constraints. Rejected.

---

## Decision 3: "Add Step" Button Placement

**Decision**: A single persistent "Add step" button is placed at the very bottom of the step list in `SequencesPage`, below the `SortableSequenceStepList`. Clicking it appends a blank action step to the top-level `form.steps` array.

**Rationale**:
- User explicitly chose Option A during clarification: bottom-of-sequence, appends to end.
- Mirrors `LoopBlock`'s existing "Add step" button pattern (scoped to loop bottom) but at the sequence level.
- No ambiguity about insertion position; always appends.

---

## Decision 4: Coexistence with ReorderableList

**Decision**: Create a new `SortableSequenceStepList` component for sequences. Leave `ReorderableList` unchanged for its other usages (e.g., CommandForm).

**Rationale**:
- `ReorderableList` is used in at least `CommandForm` with the existing ↑/↓ button model. Modifying it would risk regressions in unrelated UI.
- `SortableSequenceStepList` is purpose-built for the sequence editor; it accepts `SequenceStep` objects directly (not `ReorderableListItem`) and owns the DnD context.
- `LoopBlock` gets its own internal sortable body using the same shared `DndContext` (bubbled up from the sequence editor).

---

## Decision 5: Visual "Not Allowed" Indicator

**Decision**: Apply a CSS class to the hovered drop container when a cross-scope drag is detected. Use a red-outline or dimmed styling. On release over invalid target, item snaps back (no-op in `onDragEnd`).

**Rationale**:
- Standard UX pattern for constrained DnD. The CSS class approach is decoupled — styles can be adjusted without touching logic.
- dnd-kit's `DragOverlay` component can also render a "forbidden" cursor overlay if desired; adding it is non-breaking.

**Implementation note**: Use a React state variable `isDragInvalid` set in `onDragOver` and cleared in `onDragEnd`/`onDragCancel`. Pass it as a prop to loop blocks to apply the CSS class conditionally.

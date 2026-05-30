# Implementation Plan: Drag and Drop for Command Steps

**Branch**: `044-commands-drag-drop` | **Date**: 2026-05-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/044-commands-drag-drop/spec.md`

## Summary

Replace the up/down arrow button reordering in `CommandForm` with the same drag-and-drop interaction already in use in the Sequences editor. The change is entirely within the React frontend: swap `ReorderableList` for `SortableSequenceStepList` wrapped in a `DndContext`, wiring drag handlers identical in structure to those in `SequencesPage`.

## Technical Context

**Language/Version**: TypeScript 5 / React 18  
**Primary Dependencies**: `@dnd-kit/core` v6, `@dnd-kit/sortable` v10 (already installed), React Testing Library, Vitest  
**Storage**: N/A — pure UI, step order persisted via existing `onChange` callback  
**Testing**: Vitest + React Testing Library (existing pattern in `CommandForm.zoom.test.tsx`)  
**Target Platform**: Browser SPA (Vite/React)  
**Project Type**: Web application (frontend component change)  
**Performance Goals**: Drag interaction ≤16ms frame time — inherits dnd-kit's existing optimisation; no regression versus sequences editor  
**Constraints**: Zero new dependencies; reuse existing `SortableStepItem`, `SortableSequenceStepList`, `DropIndicator` without modification  
**Scale/Scope**: Single-file component change + one test file update

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| Build clean | ✅ Pass | No build changes; dnd-kit deps already present |
| Lint / format | ✅ Expected pass | Same code patterns as SequencesPage |
| Tests pass | ✅ Expected pass | Existing zoom tests unaffected; new DnD tests to be added |
| Coverage ≥80% | ✅ Expected pass | New handlers covered by new interaction tests |
| UX consistency | ✅ Pass | Explicitly reuses identical components |
| No new deps | ✅ Pass | @dnd-kit already a project dependency |
| Dead code removed | ✅ Pass | `updateStepOrder` and `ReorderableList` import removed |

*Post-design re-check*: No violations identified. Simple component substitution with no architectural complexity.

## Project Structure

### Documentation (this feature)

```text
specs/044-commands-drag-drop/
├── plan.md              # This file
├── research.md          # Phase 0 output (see below — no unknowns)
├── data-model.md        # Phase 1 output (see below — type unchanged)
└── tasks.md             # Phase 2 output (/speckit-tasks command)
```

### Source Code

```text
src/web-ui/src/
├── components/
│   ├── commands/
│   │   └── CommandForm.tsx            ← PRIMARY CHANGE (swap ReorderableList → DnD)
│   ├── SortableStepItem.tsx           ← reused as-is
│   ├── SortableSequenceStepList.tsx   ← reused as-is
│   └── DropIndicator.tsx              ← reused transitively (no direct change)
└── features/authoring/__tests__/
    └── CommandForm.zoom.test.tsx      ← UPDATE existing + ADD drag interaction tests
```

**Structure Decision**: Single-project frontend (Option 1). All changes are in `src/web-ui/src/`. No backend changes needed.

## Phase 0: Research

No NEEDS CLARIFICATION items exist. All decisions resolved:

### research.md

**Decision**: Reuse `SortableSequenceStepList` + `DndContext` directly inside `CommandForm`.  
**Rationale**: `SortableSequenceStepList` already accepts `ReorderableListItem[]` and `onDelete`, which match the existing call-site in `CommandForm`. Only three additions are needed: (1) DnD state (`activeStepId`, `overId`), (2) sensors, (3) `DndContext` wrapper with drag handlers.  
**Alternatives considered**:
- Extract a `CommandStepDndList` component — rejected; `SortableSequenceStepList` already handles the flat-list case with no changes.
- Keep `ReorderableList` alongside DnD — rejected per FR-004 (full replacement).

**Decision**: Use `closestCenter` (dnd-kit default) instead of the custom `closestCenterToCursor`.  
**Rationale**: The command step list is flat (single scope). The custom collision detection in `SequencesPage` exists solely to handle nested loop scopes. With one scope, `closestCenter` is correct and simpler.  
**Alternatives considered**: Copy `closestCenterToCursor` — unnecessary overhead.

**Decision**: DnD state (`activeStepId`, `overId`) is internal to `CommandForm` (local `useState`).  
**Rationale**: `CommandForm` is already a self-contained controlled component. Drag state is transient UI state; only the final `onChange` call propagates the new order to the parent. This mirrors the `SequencesPage` pattern.

## Phase 1: Design & Contracts

### data-model.md

No data model changes. The `ReorderableListItem` type (from `ReorderableList.tsx`) is unchanged and continues to be used as the display representation of each step:

```ts
// src/web-ui/src/components/ReorderableList.tsx — UNCHANGED
type ReorderableListItem = {
  id: string;
  label: string;
  description?: string;
  details?: React.ReactNode;
};
```

The `StepEntry` type in `CommandForm.tsx` and the `toStepItems` mapping function are also unchanged. Only the reordering mechanism changes.

### CommandForm.tsx — Detailed Change Plan

**Imports to add:**
```ts
import { DndContext, DragEndEvent, DragOverEvent, DragStartEvent, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { SortableSequenceStepList } from '../SortableSequenceStepList';
```

**Imports to remove:**
```ts
import { ReorderableList, ReorderableListItem } from '../ReorderableList';
// Keep ReorderableListItem type — it's used by toStepItems return type and SortableSequenceStepList
// Change to: import type { ReorderableListItem } from '../ReorderableList';
```

**State to add inside `CommandForm`:**
```ts
const [activeStepId, setActiveStepId] = useState<string | null>(null);
const [overId, setOverId] = useState<string | null>(null);
const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));
```

**Handlers to add:**
```ts
const handleDragStart = (event: DragStartEvent) => {
  setActiveStepId(event.active.id as string);
  setOverId(null);
};

const handleDragOver = (event: DragOverEvent) => {
  setOverId(event.over?.id as string ?? null);
};

const handleDragEnd = (event: DragEndEvent) => {
  const { active, over } = event;
  setActiveStepId(null);
  setOverId(null);
  if (!over || active.id === over.id) return;
  const oldIndex = value.steps.findIndex((s) => s.id === active.id);
  const newIndex = value.steps.findIndex((s) => s.id === over.id);
  if (oldIndex === -1 || newIndex === -1) return;
  onChange({ ...value, steps: arrayMove(value.steps, oldIndex, newIndex) });
};

const handleDragCancel = () => {
  setActiveStepId(null);
  setOverId(null);
};
```

**Function to remove:** `updateStepOrder` (dead code once `ReorderableList` is removed).

**JSX replacement** (inside `<FormSection title="Steps" ...>`):
```tsx
// REMOVE:
<ReorderableList
  items={stepItems}
  onChange={updateStepOrder}
  onDelete={(item) => removeStep(item.id)}
  disabled={submitting || loading}
  emptyMessage="No steps yet. Add command, primitive tap, or wait-for-image steps."
/>

// REPLACE WITH:
<DndContext
  sensors={sensors}
  onDragStart={handleDragStart}
  onDragOver={handleDragOver}
  onDragEnd={handleDragEnd}
  onDragCancel={handleDragCancel}
>
  <SortableSequenceStepList
    items={stepItems}
    onDelete={(item) => removeStep(item.id)}
    disabled={submitting || loading}
    emptyMessage="No steps yet. Add command, primitive tap, or wait-for-image steps."
    activeId={activeStepId}
    overId={overId}
  />
</DndContext>
```

### Test Plan — CommandForm.zoom.test.tsx

The existing zoom tests reference `actionOptions` prop which no longer exists in `CommandForm`; this is an existing discrepancy to fix. Updates:

1. **Fix existing tests**: Remove `actionOptions={[]}` prop (not part of current interface).
2. **Add drag interaction test**: Render `CommandForm` with two steps, simulate DnD reorder, assert `onChange` called with swapped step order.
3. **Add disabled state test**: Confirm drag handles render with `cursor: not-allowed` when `disabled` is passed.

Note: dnd-kit's `PointerSensor` requires pointer events. Tests use `@testing-library/user-event` with `userEvent.pointer` or mock `DragEndEvent` directly via the `onDragEnd` handler invocation.

### No API/Contract Changes

This feature is purely a UI interaction change. No backend endpoints, API schemas, or data contracts are affected.

## Complexity Tracking

> No constitution violations. No entry required.

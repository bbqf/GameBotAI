# Data Model: Sequence Loop Step Management

**Branch**: `037-loop-step-management` | **Date**: 2026-05-30

## Existing Types (unchanged)

These types are defined in `src/web-ui/src/types/stepEntry.ts` and remain unmodified by this feature.

### StepEntry (discriminated union)

```
StepEntry = ActionStepEntry | LoopStepEntry | BreakStepEntry
```

| Field | Type | Notes |
|-------|------|-------|
| `type` | `"Action" \| "Loop" \| "Break"` | Discriminant |
| `id` | `string` | UUID, unique across the sequence |
| `stepId` | `string` | Display name / key used by backend |

### LoopStepEntry

| Field | Type | Notes |
|-------|------|-------|
| `type` | `"Loop"` | Discriminant |
| `id` | `string` | Also used as `scopeId` for loop-body DnD scope |
| `loopType` | `"count" \| "while" \| "repeatUntil"` | |
| `body` | `StepEntry[]` | Ordered child steps; scoped within this loop |

### SequenceStep (in SequencesPage)

Wrapper that combines `StepEntry` with a `stepType` discriminant and form-level metadata (e.g., `loopEntry`). Not modified by this feature.

---

## New DnD Metadata Type

Used as the `data` payload for `useSortable` / `useDraggable` in dnd-kit. Not persisted — exists only during drag interactions.

### StepDragData

| Field | Type | Description |
|-------|------|-------------|
| `scopeId` | `string` | `"root"` for top-level steps; `loop.id` for loop-body steps |
| `type` | `"step"` | Constant discriminant to distinguish from other draggable types |

**Usage**:
```typescript
// Top-level step
useSortable({ id: step.id, data: { scopeId: 'root', type: 'step' } })

// Loop body step
useSortable({ id: step.id, data: { scopeId: loop.id, type: 'step' } })
```

---

## Scope Model

| Scope | scopeId value | Who owns SortableContext |
|-------|--------------|--------------------------|
| Top-level sequence | `"root"` | `SortableSequenceStepList` |
| Loop body | `loop.id` (UUID) | `LoopBlock` (modified) |

The single `DndContext` wraps the entire sequence editor in `SequencesPage`. Scope enforcement is done in the `onDragEnd` handler: if `active.data.current.scopeId !== over.data.current.scopeId`, the handler returns without modifying state.

---

## Visual State During Drag

Not a persisted data model — describes transient UI state managed in `SequencesPage`.

| State Variable | Type | Cleared |
|----------------|------|---------|
| `activeStepId` | `string \| null` | On `onDragEnd` / `onDragCancel` |
| `isDragInvalid` | `boolean` | On `onDragEnd` / `onDragCancel` |

`isDragInvalid = true` when the currently hovered drop target has a different `scopeId` than the active item. Passed as prop to `LoopBlock` to apply `loop-block--drop-invalid` CSS class.

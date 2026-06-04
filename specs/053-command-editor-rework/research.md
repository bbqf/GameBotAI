# Research: Command Editor Rework

## Decision: Panel-per-action-type via controlled sub-components

**Decision**: Each primitive action type (Tap, WaitForImage, EnsureGameRunning) becomes its own small React component that manages its own form state internally. The parent `CommandForm` only tracks which panel is currently open (`pendingActionType`) and which step is being edited (`editingStepId`), replacing the current flood of individual `pendingX` useState hooks.

**Rationale**: Isolating form state per panel keeps each panel self-contained, makes the add-flow and edit-flow symmetric (both open the same panel component), and eliminates the risk of stale values from one action type bleeding into another.

**Alternatives considered**: Keeping all state in `CommandForm` (flat approach) — rejected because it requires even more top-level state as actions grow. Single generic `ActionPanel` with conditional fields — rejected because it recreates the current clutter problem inside one component.

---

## Decision: ActionTypeSelector is a standalone component

**Decision**: A new `ActionTypeSelector` component renders a `<select>` (or equivalent) listing the three valid primitive actions. It is rendered above the step list. Selecting an action calls `onChange`; selecting the blank option closes the panel.

**Rationale**: Separates the "what to add" choice from the "fill in details" panel, matching the spec's action-first flow.

---

## Decision: Edit flow reuses the same panel components as add flow

**Decision**: When the user clicks an existing step, `CommandForm` sets `editingStepId` (the id of the step being edited) and `pendingActionType` (derived from that step's type). The panel renders with the step's current values as `initialValue` props. On confirm, the step is updated in-place by id; on cancel, edit mode is cleared.

**Rationale**: Reusing add panels for editing avoids a separate edit-panel component per action type, keeps the surface area small, and satisfies FR-011.

---

## Decision: SortableSequenceStepList receives an onEdit callback

**Decision**: `SortableSequenceStepList` gains an optional `onEdit: (item: ReorderableListItem) => void` prop. Each step item renders an "Edit" button alongside the existing "Delete" button that calls `onEdit(item)`.

**Rationale**: Minimal change to the list component; the parent (`CommandForm`) owns the edit routing logic.

---

## Decision: Label change — "PrimitiveTap" → "Tap" in display and validation messages

**Decision**: The `toStepItems()` helper changes label from `"Primitive tap: {imageId}"` to `"Tap: {imageId}"`. Error messages and step section description are updated accordingly. The underlying `StepEntry.type` discriminant value `'PrimitiveTap'` stays unchanged to maintain API/DTO compatibility.

**Rationale**: User-facing rename only; no backend changes needed.

---

## Decision: Command step addition removed from UI; existing Command steps remain renderable

**Decision**: Remove the `SearchableDropdown` / "Add command step" button from `CommandForm`. The `commandOptions` prop and the `Command` branch in `toStepItems()` are retained so existing Command steps already saved in the database still display and can be deleted. No data migration is needed.

**Rationale**: Matches FR-001 and the Assumptions in the spec.

---

## Decision: No new API endpoints or data model changes

**Decision**: This is a pure frontend/UI change. All existing DTOs, endpoints, and `StepEntry` type shapes remain untouched.

**Rationale**: The backend already supports all three primitive action types.

---

## Performance Goals

Panel open/close is instant state toggle; no async work. No performance budget declaration needed beyond standard React render expectations for a small form.

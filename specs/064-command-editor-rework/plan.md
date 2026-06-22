# Implementation Plan: Command Editor Rework

**Branch**: `053-command-editor-rework` | **Date**: 2026-06-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/053-command-editor-rework/spec.md`

## Summary

Replace the flat "all-fields-always-visible" step-addition controls in `CommandForm` with an action-first selector that opens a dedicated attribute panel per primitive action type (Tap, Wait for Image, Ensure Game Running). Add in-place step editing by clicking an existing step. Remove the "Add command" step type from the UI. Pure frontend change — no backend or API modifications.

## Technical Context

**Language/Version**: TypeScript 5.x / React 18  
**Primary Dependencies**: React Testing Library, Jest, @dnd-kit/core, @dnd-kit/sortable  
**Storage**: N/A (UI-only change)  
**Testing**: Jest + React Testing Library (existing pattern in `src/web-ui/src/components/__tests__/` and `src/web-ui/src/features/authoring/__tests__/`)  
**Target Platform**: Web (Vite + browser)  
**Project Type**: Web application frontend  
**Performance Goals**: Panel open/close completes within one frame (≤ 16 ms) under standard dev-machine conditions; no async work introduced; no new network requests on panel open/close  
**Constraints**: No backend changes; `StepEntry.type` discriminant values unchanged for API compatibility  
**Scale/Scope**: 2 modified files, 3 new panel components, 1 updated list component

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Lint/format zero errors | Required | Run `vite build` + Jest as quality gate per project conventions |
| No new high/critical static analysis issues | Required | Applies to new panel components |
| Unit tests ≥ 80% line / ≥ 70% branch for touched areas | Required | New panel components and updated `CommandForm` must be covered |
| CamelCase method names only (no underscores) | Required | Applies to all new helper functions |
| No implementation may proceed past a red build/test | Hard stop | |
| UX: updated label "Tap" reflected in step list display and validation messages | Required | Consistency principle |

No violations requiring justification.

## Project Structure

### Documentation (this feature)

```text
specs/053-command-editor-rework/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── checklists/
│   └── requirements.md
└── tasks.md             ← Phase 2 output (/speckit-tasks)
```

### Source Code (affected paths)

```text
src/web-ui/src/
├── components/
│   ├── commands/
│   │   ├── CommandForm.tsx                     ← MODIFY (major refactor)
│   │   ├── CommandForm.css                     ← MODIFY (panel styles)
│   │   ├── ActionTypeSelector.tsx              ← NEW
│   │   ├── TapPanel.tsx                        ← NEW
│   │   ├── WaitForImagePanel.tsx               ← NEW
│   │   └── EnsureGameRunningPanel.tsx          ← NEW
│   └── SortableSequenceStepList.tsx            ← MODIFY (add onEdit prop)
└── features/
    └── authoring/
        └── __tests__/
            └── CommandForm.zoom.test.tsx       ← MODIFY (update for new controls)

src/web-ui/src/components/commands/__tests__/  ← NEW directory
    ├── TapPanel.test.tsx                       ← NEW
    ├── WaitForImagePanel.test.tsx              ← NEW
    ├── EnsureGameRunningPanel.test.tsx         ← NEW
    ├── ActionTypeSelector.test.tsx             ← NEW
    └── CommandForm.steps.test.tsx              ← NEW
```

**Structure Decision**: Single web application frontend. New components colocate with the existing `commands/` folder. Tests follow the existing `__tests__/` subdirectory pattern.

## Implementation Phases

### Phase A — New Panel Components

Create the three action-specific panel components and the action type selector. Each panel is self-contained and manages its own form state.

**A1 — `ActionTypeSelector`**

A `<select>` with a blank first option followed by: Tap, Wait for Image, Ensure Game Running.

Props:
```ts
type ActionTypeSelectorProps = {
  value: 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | '';
  onChange: (next: 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | '') => void;
  disabled?: boolean;
};
```

**A2 — `TapPanel`**

Props:
```ts
type TapPanelProps = {
  initialValue?: { referenceImageId: string; confidence?: string; offsetX?: string; offsetY?: string };
  onConfirm: (value: { referenceImageId: string; confidence?: string; offsetX?: string; offsetY?: string }) => void;
  onCancel: () => void;
  disabled?: boolean;
};
```

Renders: image selector (required), confidence (optional, 0–1), offsetX (optional), offsetY (optional), Add/Save button, Cancel button.

Validation:
- Blocks confirm if `referenceImageId` is empty or stale. Error text: **"Reference image is required."**
- If `confidence` is non-empty, must satisfy `0 ≤ parseFloat(confidence) ≤ 1`. Error text: **"Confidence must be a number between 0 and 1."** (If blank, skip this check — the field is optional.)

**A3 — `WaitForImagePanel`**

Props:
```ts
type WaitForImagePanelProps = {
  initialValue?: { timeoutMs?: string; referenceImageId?: string; confidence?: string };
  onConfirm: (value: { timeoutMs: string; referenceImageId?: string; confidence?: string }) => void;
  onCancel: () => void;
  disabled?: boolean;
};
```

Renders: timeoutMs input (required), image selector (optional), confidence (optional), Add/Save button, Cancel button.

Validation:
- Blocks confirm if `timeoutMs` is empty or not a non-negative integer (i.e., `/^\d+$/` fails). Error text: **"Timeout must be a non-negative whole number (ms)."**
- If `confidence` is non-empty, must satisfy `0 ≤ parseFloat(confidence) ≤ 1`. Error text: **"Confidence must be a number between 0 and 1."** (If blank, skip — field is optional.)

**A4 — `EnsureGameRunningPanel`**

Props:
```ts
type EnsureGameRunningPanelProps = {
  onConfirm: () => void;
  onCancel: () => void;
  disabled?: boolean;
};
```

Renders: descriptive text ("Checks that the game is in the foreground; starts it if not running."), Add button, Cancel button. No input fields.

---

### Phase B — Update `SortableSequenceStepList`

Add an optional `onEdit` callback prop:

```ts
type SortableSequenceStepListProps = {
  // existing props...
  onEdit?: (item: ReorderableListItem) => void;
};
```

When `onEdit` is provided, render an "Edit" button in `.reorderable-list__controls` for each item alongside the existing "Delete" button. Clicking calls `onEdit(item)`.

---

### Phase C — Refactor `CommandForm`

**C1 — State changes**

Remove all `pendingX` useState hooks. Add:
```ts
const [pendingActionType, setPendingActionType] = useState<'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | ''>('');
const [editingStepId, setEditingStepId] = useState<string | null>(null);
```

**C2 — Remove Command step addition UI**

Delete the `SearchableDropdown` and "Add command step" button from the JSX. Retain the `commandOptions` prop (still needed to render existing Command steps in `toStepItems`).

**C3 — Replace flat add-controls with selector + panel**

In the Steps `FormSection`, render:
1. `ActionTypeSelector` bound to `pendingActionType` / `setPendingActionType`.
2. Conditionally render the matching panel based on `pendingActionType`:
   - `'PrimitiveTap'` → `TapPanel`
   - `'WaitForImage'` → `WaitForImagePanel`
   - `'EnsureGameRunning'` → `EnsureGameRunningPanel`
   - `''` → nothing
3. Each panel's `onConfirm` appends (add mode) or updates (edit mode) the relevant step, then resets `pendingActionType` to `''` and `editingStepId` to `null`.
4. Each panel's `onCancel` resets both fields without modifying steps.

**C4 — Edit flow**

Pass `onEdit` to `SortableSequenceStepList`. When triggered:
```ts
const EDITABLE_TYPES = new Set(['PrimitiveTap', 'WaitForImage', 'EnsureGameRunning']);

const handleEditStep = (item: ReorderableListItem) => {
  const step = value.steps.find(s => s.id === item.id);
  if (!step || !EDITABLE_TYPES.has(step.type)) return; // Command steps have no panel
  setEditingStepId(step.id);
  setPendingActionType(step.type as 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning');
};
```
The guard prevents Command-type steps from entering edit mode (they have no attribute panel). The panel reads `initialValue` from the matching step in `value.steps` (looked up by `editingStepId`). On confirm, the step is replaced in `value.steps` at its current index.

**C5 — Update `toStepItems` label**

Change `"Primitive tap: ..."` → `"Tap: ..."`.

**C6 — Update validation messages in `CommandsPage`**

Change `"Primitive tap steps require..."` → `"Tap steps require..."`.

**C7 — Update `FormSection` description**

Change the Steps section description from `"Choose command or primitive tap steps..."` to `"Select an action type to add or edit steps."`.

---

### Phase D — Tests

**D1 — New tests for each panel component** (`TapPanel.test.tsx`, `WaitForImagePanel.test.tsx`, `EnsureGameRunningPanel.test.tsx`):
- Renders correct fields only
- Confirm blocked when required fields missing
- Confirm fires with correct values
- Cancel fires without calling onConfirm
- initialValue pre-fills fields

**D2 — `ActionTypeSelector.test.tsx`**:
- Renders three options plus blank
- onChange fires with correct type string
- Blank option produces empty string

**D3 — `CommandForm.steps.test.tsx`** (add-flow, edit-flow, panel switching):
- Selecting action type shows correct panel
- Switching action clears previous panel state
- Adding a step resets selector to blank
- Clicking a step opens its panel pre-filled
- Editing a step updates it in place
- "Add command" UI is absent
- Step list labels show "Tap: ..." not "Primitive tap: ..."
- Existing Command steps still render and their Delete button fires (`onDelete` called); their Edit button is a no-op (clicking does not open any panel)
- `handleEditStep` with a Command-type step leaves `pendingActionType` as `''` and `editingStepId` as `null`

**D4 — Update `CommandForm.zoom.test.tsx`**:
- Remove references to old `pendingX` controls
- Retain Detection section field assertions (unchanged)
- Update or remove step-panel-specific selectors

---

## Complexity Tracking

No constitution violations. No complexity justification required.

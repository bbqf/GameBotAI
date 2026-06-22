# Data Model: Command Editor Rework

This feature is a pure UI refactoring. No new backend entities or API changes are introduced. The section below documents the **frontend state model** changes that govern the new panel-based interaction.

---

## CommandForm State (revised)

| Field | Type | Description |
|---|---|---|
| `pendingActionType` | `'PrimitiveTap' \| 'WaitForImage' \| 'EnsureGameRunning' \| ''` | Which add/edit panel is currently open. `''` = panel hidden, selector blank. Matches `ActionTypeSelectorProps.value` directly — no conversion needed. |
| `editingStepId` | `string \| null` | `null` when adding a new step; set to a step's `id` when editing an existing one. |

**Removed fields** (replaced by the above):
- `pendingCommandId`
- `pendingPrimitiveReferenceImageId`
- `primitiveTapStale`
- `pendingPrimitiveConfidence`
- `pendingPrimitiveOffsetX`
- `pendingPrimitiveOffsetY`
- `pendingWaitReferenceImageId`
- `pendingWaitConfidence`
- `pendingWaitTimeoutMs`

---

## TapPanel Internal State

Managed inside `TapPanel` component; not visible to parent until `onConfirm` fires.

| Field | Type | Required | Default |
|---|---|---|---|
| `referenceImageId` | `string` | Yes | `''` |
| `confidence` | `string` | No | `''` |
| `offsetX` | `string` | No | `'0'` |
| `offsetY` | `string` | No | `'0'` |
| `stale` | `boolean` | — | `false` |

**Validation**: `referenceImageId` must be non-empty and not stale before `onConfirm` is allowed.

---

## WaitForImagePanel Internal State

| Field | Type | Required | Default |
|---|---|---|---|
| `timeoutMs` | `string` | Yes | `'1000'` |
| `referenceImageId` | `string` | No | `''` |
| `confidence` | `string` | No | `''` |

**Validation**: `timeoutMs` must be a non-negative integer string before `onConfirm` is allowed.

**Staleness decision**: WaitForImagePanel does **not** track a `stale` flag. The `referenceImageId` field is optional and advisory — a stale or missing image reference does not block confirm (unlike TapPanel where the image is required). If the user mounts a WaitForImage step with a deleted image reference the panel will display an empty image selector; the user may leave it blank (timeout-only) or pick a new image. This is consistent with the "optional image" semantics of WaitForImage.

---

## EnsureGameRunningPanel Internal State

No configurable fields. Panel only shows a description and a confirm button.

---

## Unchanged Entities

| Entity | Change |
|---|---|
| `StepEntry` | Unchanged; `'PrimitiveTap'` discriminant stays |
| `DetectionTargetForm` | Unchanged |
| `CommandFormValue` | Unchanged |
| All backend DTOs | Unchanged |
| `commandOptions` prop on `CommandForm` | Retained (needed to display existing Command steps) |

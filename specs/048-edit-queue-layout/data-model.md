# Phase 1 Data Model: Edit Queue Page Layout

This feature introduces **no new persisted entities** and changes no existing ones. It
reorganizes presentation and adds a Reload action over the existing model. The "model"
here is therefore the **UI/component state** of the edit page, plus the (unchanged)
domain entities it references.

## Referenced domain entities (unchanged)

- **Queue** (from 046): `id`, `name`, `emulatorSerial`, `cycleExecution`, `status`
  (`Stopped` | `Running`), runtime-only ordered `entries`. Only `name` and
  `cycleExecution` are editable via the page-level Save.
- **Queue Entry** (from 046): `{ entryId, sequenceId, sequenceName, stale }` — runtime
  only; mutated immediately by add/remove/replace, independent of page Save/Cancel.
- **Queue Template** (from 047): `{ id, name, entryCount, createdAt, updatedAt,
  entries[] }`, file-backed. Read for load/reload; written by save; removed by delete.

## UI state model (new/changed, frontend only)

### `QueuesPage` edit-session state

| Field | Type | Notes |
|-------|------|-------|
| `detail` | `QueueDetailDto \| undefined` | The queue being edited (existing). |
| `form` | `{ name, emulatorSerial, cycleExecution }` | Row-1/row-3 fields; committed by Save (existing). |
| `associatedTemplateName` | `string \| undefined` | Name of the template last loaded or saved this session (replaces `loadedTemplateName`). Drives the template-name button label, the Save pre-fill, and reload resolution. UI-state only — no live link. |
| `pendingLoad` | `{ name, sequenceIds } \| undefined` | Existing replace-on-load confirmation (non-empty queue). |
| `pendingReload` | `{ name, sequenceIds } \| undefined` | NEW — drives the reload confirmation modal; set only when a reload would change a non-empty queue. |

### `QueueTemplateControls` (new component) state

| Field | Type | Notes |
|-------|------|-------|
| `openSection` | `'none' \| 'save' \| 'load'` | Default `'none'` (FR-010). At most one open (FR-011). Reset to `'none'` on completion/dismiss (FR-012). |

Props (shape; finalized in code): `associatedTemplateName?`, `status`,
`currentSequenceIds: string[]`, `onSaveTemplate(name, overwrite)`,
`onLoadTemplate(templateId)`, `onReload()`.

### `QueueForm` (modified) props

| Prop | Type | Notes |
|------|------|-------|
| `templateControls?` | `React.ReactNode` | Rendered between emulator and cycle (row 2). Edit only. |
| `entries?` | `React.ReactNode` | Rendered between cycle and form-actions (row 4). Edit only. |

(The emulator hint text is removed; no prop change for that.)

## Derived values / rules

- **Template-name button label** = `associatedTemplateName ?? '(no template)'`.
- **Reload enabled** = `Boolean(associatedTemplateName) && status !== 'Running'`
  (FR-016/FR-017).
- **Reload needs confirmation** = `currentSequenceIds.length > 0 &&
  !sameSequenceOrder(currentSequenceIds, templateSequenceIds)` (FR-015).
- **`sameSequenceOrder(a, b)`** = pure, order-sensitive array equality (lengths equal and
  each index equal). Backs the diff; unit-tested independently.
- **Reload target missing** = no case-insensitive name match in `listQueueTemplates()` →
  "Template '<name>' is no longer available" (FR-018).

## State transitions

- Open edit page → `openSection = 'none'`, both panels closed.
- Click template-name → `openSection = 'load'`.
- Click Save Template → `openSection = 'save'`.
- Open one panel → the other closes (`openSection` is single-valued).
- Save success → `associatedTemplateName = savedName`; panel closes.
- Load success → `associatedTemplateName = loadedName`; panel closes; entries replaced.
- Reload: resolve by name → diff → (confirm if needed) → entries replaced;
  `associatedTemplateName` unchanged (same template).

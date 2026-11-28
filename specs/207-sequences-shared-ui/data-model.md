# Data Model: Move the Sequences list, form page, and delete to the shared UI components

No backend data changes. `SequenceDto` and the payloads do not change.

## Payload rules (must not change)

| Member | Create | Update |
|--------|--------|--------|
| name, version, steps, interStepDelayRangeMs | always | always |
| parameters | only when not empty | always (also empty) |
| excludeFromSuccessNotifications | only `true` when on | always (`true` or `false`) |
| ignoreSmartScheduling | only `true` when on | always (`true` or `false`) |
| onTimeLimit | only `'end'` | always (`'fail'` or `'end'`) |

## List columns

| Column | Width | Value |
|--------|-------|-------|
| Name | flexible | `name` |
| Options | 360 px | "No success notifications" and/or "Ignores smart scheduling", joined by ", "; else "—" |
| Steps | 96 px, right | `steps.length` (tabular digits) |

## UI state

| Name | Note |
|------|------|
| loadedName | The name when the sequence loads. The edit page title. "Loading…" before the load ends. |
| mode | `'create'` when `creating`, `'edit'` when `editingId` is set. |
| Delete flow | `useDeleteFlow` with `kind: 'sequence'` (feature 206). |

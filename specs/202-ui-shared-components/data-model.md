# Data Model: Shared UI components

This feature has no stored data. These are the UI types that the shared components use.

## ListColumn<T>

| Field | Type | Rule |
|-------|------|------|
| `id` | string | Unique in the list. |
| `header` | string | Header text. An empty string is permitted (for example, an icon column). |
| `width` | number (px) or `'flex'` | Exactly one column of a list has `'flex'`. |
| `align` | `'start'` or `'end'` | Default `'start'`. Numbers use `'end'`. |
| `render` | `(row: T) => ReactNode` | One-line content. |
| `text` | `(row: T) => string` (optional) | The tooltip text. Empty text shows "—". |
| `cellTestId` | string (optional) | A `data-testid` for each cell of the column. |

## ListView options for actions

| Field | Type | Rule |
|-------|------|------|
| `actionsColumn` | `{ header: string; width: number }` | Required on the desktop when the list has actions. |
| `rowActions` | `(row: T) => RowActions` | The normal way to give actions. |
| `renderActions` | `(row: T) => ReactNode` | Desktop alternative for a caller that needs a hook for each row. |
| `renderPhoneActions` | `(row: T) => ReactNode` | Phone alternative, same reason. |
| `menuLabel` | `(row: T) => string` | Accessible name of "⋮". Default "More actions". |
| `openLabel` | `(row: T) => string` | Accessible name of the open button. Default "Open <title>". |

## RowAction

| Field | Type | Rule |
|-------|------|------|
| `id` | string | Unique in the row. |
| `label` | string | The action name, for example "Pause". The tooltip. |
| `ariaLabel` | string | The accessible name, for example "Pause PNS Daily". |
| `icon` | `IconName` | From the shared icon set. |
| `tone` | `'default'` or `'danger'` | A danger action in a menu is red and is last. |
| `disabled` | boolean | The action stays in its position, dimmed. |
| `onSelect` | `() => void` | |

## RowActions

| Field | Type | Rule |
|-------|------|------|
| `visible` | `RowAction[]` | Desktop: max. 3. Phone: max. 1. The same count for all rows of a list. |
| `menu` | `RowAction[]` | Optional. Shown in the "⋮" menu. |

## ListState

`'ready'` or `{ kind: 'loading' }` or `{ kind: 'empty', text?: string }` or `{ kind: 'error', text: string, onRetry?: () => void }`.
Default texts: "Loading…", "No items.", and **Try again**.

## ChipState

`running` (success), `paused` (warning), `stopped` (idle), `failed` (danger), `done` (success), `unknown` (idle).

### Queue to chip state

| Queue | Chip |
|-------|------|
| `status` = `Running`, `operatorPause` = `paused` | `paused`, "Paused" |
| `status` = `Running`, other `operatorPause` | `running`, "Running" |
| `status` = `Stopped` | `stopped`, "Stopped" |
| other | `unknown`, "Unknown" |

## Queue row actions

| Queue state | Slot 1 | Slot 2 | Slot 3 | Menu |
|-------------|--------|--------|--------|------|
| Stopped | Start (play) | Stop (off) | Edit | Duplicate, Delete |
| Running, pause `none` | Pause | Stop | Edit | Duplicate, Delete (off) |
| Running, pause `pausing` | Cancel pause (undo) | Stop | Edit | Duplicate, Delete (off) |
| Running, pause `paused` | Resume (play) | Stop | Edit | Duplicate, Delete (off) |

A pause call in progress turns slot 1 off. Phone: main action Start (stopped) or Stop (running); menu Open, and for a
running queue the slot 1 action.

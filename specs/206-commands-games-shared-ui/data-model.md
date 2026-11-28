# Data Model: Move the Commands and Games pages to the shared UI components

No backend data changes. The DTOs (`GameDto`, `CommandDto`) and the payloads do not change.

## UI state (new)

| Name | Where | Fields |
|------|-------|--------|
| DeleteTarget | `useDeleteFlow` | `id`, `name` |
| DeleteConflict | `useDeleteFlow` | `message` (API message or the default text), `references?: Record<string, { id: string; name: string }[]>` |
| loadedName | `GamesPage`, `CommandsPage` | The item name when the edit data loads. The edit page title. |

## List columns

| Page | Column | Width | Value |
|------|--------|-------|-------|
| Games | Name | flexible | `name` |
| Games | Package name | 320 px | `packageName` (monospace) or "—" |
| Commands | Name | flexible | `name` |
| Commands | Steps | 96 px, right | `stepCount` (tabular digits) |

Row actions (both): Edit (icon `edit`) and the "⋮" menu with Delete (danger). Actions column: 1 icon + menu.

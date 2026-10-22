# Data Model: Gamepacks Page Take-Over Option

No data is stored. These are page states and request fields.

## Page state (GamepacksPage)

| Field | Type | Default | Note |
|-------|------|---------|------|
| `takeOver` | boolean | false | The check box. It is not kept between visits. |
| `lastDryRun` | object or null | null | The last dry run. See below. |

## lastDryRun

| Field | Type | Note |
|-------|------|------|
| `key` | string | `name@version` of the dry run. |
| `takeOver` | boolean | The check box value used for the dry run. |
| `valid` | boolean | The `valid` flag of the response. |
| `takenOver` | number | The `takenOver` count of the response. |

Rule: an install with `takeOver` on is allowed only when `lastDryRun` exists, `valid` is true, and `key` and `takeOver`
match the current version and check box. A change of the take-over check box sets `lastDryRun` to null and clears the dry run notice.
A change of the Dry run check box keeps `lastDryRun`. The count `Take over N.` shows when `lastDryRun.takeOver` is true, valid or not.
The page does not start a new dry run.

## Request (existing API, no change)

`POST /api/gamepacks/catalog/install` body: `{ name, version, dryRun, takeOver }`. `takeOver` is false if missing.

## Response fields used (existing API)

`valid`, `problems[]`, `created`, `updated`, `removed`, `takenOver` (number), `takeOverBackupId` (string, real install only),
`takeOvers[]` (dry run with take-over, not used by this feature).

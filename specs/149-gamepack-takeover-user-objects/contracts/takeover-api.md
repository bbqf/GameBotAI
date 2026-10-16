# Contract: Take-over API

All routes use the same API token rule as the other gamepack routes. JSON names use camelCase.

## 1. Install with take-over

`POST /api/gamepacks/install?takeOver=true[&dryRun=true]`

Body: as today (the gamepack file as `application/json`, or `multipart/form-data` with the parts `gamepack` and `signature`).

Query:

| Name | Type | Default | Meaning |
|------|------|---------|---------|
| dryRun | bool | false | As today. |
| takeOver | bool | false | Take over user objects of the same type and name. |

Response `200` (real install) adds these fields to the existing response:

```json
{
  "dryRun": false,
  "valid": true,
  "problems": [],
  "gamepack": { "name": "demo-pack", "version": "2.0.0" },
  "created": 1,
  "updated": 0,
  "removed": 0,
  "takenOver": 3,
  "takeOverBackupId": "to-20261016T101500123-1a2b3c4d"
}
```

Response `200` (dry run with `takeOver=true`) adds the list:

```json
{
  "dryRun": true,
  "valid": true,
  "problems": [],
  "takenOver": 2,
  "takeOvers": [
    { "type": "sequence", "name": "Daily", "identical": true },
    { "type": "command", "name": "Tap Home", "identical": false }
  ]
}
```

Rules:

- `takenOver` is always present. It is 0 when the option is off or nothing was taken over.
- `takeOverBackupId` is present only for a real install that replaced at least one object.
- `takeOvers` is present only for a dry run with `takeOver=true` and no problem.
- With `takeOver` off, or missing, a conflict with a user object gives `422` (real install) or `200` with `valid: false` (dry run), as today.
- A conflict with an object of another gamepack gives the same result with the option on or off: `422` (real install) or `200` with `valid: false` (dry run). The install route does not return `409` for a conflict.
- A failed write gives `500`. All objects stay as before. The response has no backup id.

## 2. Restore a take-over backup

`POST /api/gamepacks/takeover-backups/{backupId}/restore`

No body. The route holds the same gate as install and rollback. It does not stop or start a queue.

Responses:

| Status | When | Body |
|--------|------|------|
| 200 | All objects restored. | `{ "backupId": "...", "result": "restored", "restored": 3 }` |
| 404 | The id is unknown or has a wrong form. | Problem JSON with `result: "not_found"`. |
| 409 | A check failed. Nothing changed. | `{ "backupId": "...", "result": "refused", "reasons": [ { "code": "object_changed", "type": "sequence", "name": "Daily", "message": "..." } ] }` |
| 500 | A write failed. | Problem JSON with `result: "failed"` or `"partial_restore"`. |

Reason codes:

| Code | Meaning |
|------|---------|
| object_missing | A backed-up object no longer exists. |
| object_changed | The live object differs from the content that the install wrote. |
| owner_changed | The gamepack no longer owns the object, or the gamepack is not installed. |

After a restore, each restored object has its old content and the owner `user`. The backup file stays. A second restore of the same backup gives `409` with `owner_changed`.

## 3. Managed update with take-over

`POST /api/gamepacks/update?takeOver=true[&dryRun=true][&templates=...]`

`POST /api/gamepacks/catalog/update` with body `{ "name": "...", "version": "...", "dryRun": false, "takeOver": true, "templates": [] }`

`POST /api/gamepacks/catalog/install` with body `{ "name": "...", "version": "...", "dryRun": false, "takeOver": true }`

The catalog install returns the install result of section 1, with `takenOver` and `takeOverBackupId`. A client of the catalog install can restore with the route in section 2.

The managed report (feature 148), for the managed update and the catalog update, adds these fields:

```json
{
  "takenOver": 3,
  "takeOverBackupId": "to-20261016T101500123-1a2b3c4d",
  "takeOvers": []
}
```

Rules:

- `takeOverBackupId` is not set for a dry run, for a call that took nothing over, and for a failed call.
- For a dry run with `takeOver: true`, `takeOvers` has the entries and `takenOver` is the planned count.
- `backupId` keeps its meaning: the queue template backup.
- The option does not change which queues the call stops. A queue changes its run state only for the reasons of feature 148.
- If the call fails after the install worked, the flow puts the user objects back with an internal forced restore. It runs before the gamepack rollback, under the gate of the managed update. Force skips `owner_changed` only. The restore report has the step `takeover_restore`. No public route uses force.
- A conflict with an object of another gamepack gives the same status as with the option off (422 for an install, 200 with `valid: false` for a dry run).
- The option on the managed rollback (`POST /api/gamepacks/{name}/rollback?managed=true`) does not exist. A rollback never takes over an object.

## 4. OpenAPI

The OpenAPI document MUST describe:

- the query `takeOver` and the body field `takeOver`,
- the dry-run list (`takeOvers`, with `identical`),
- the count `takenOver`,
- the field `takeOverBackupId`,
- the restore route, its responses, and the reason codes.

The text uses STE. The schema filters in `GameBot.Service/Swagger` (`GamepackSchemaFilter`, `ManagedUpdateSchemaFilter`) carry the descriptions. The report completeness test (feature 148) MUST include the new fields.

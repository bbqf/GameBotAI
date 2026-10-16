# Contract: Managed gamepack update API

All routes use the existing API token rule. All routes are in the OpenAPI document with tag `Gamepacks`. All descriptions are in STE.

## Routes

### POST /api/gamepacks/update

Update from a gamepack file. Name: `UpdateGamepack`.

- Body: same as `POST /api/gamepacks/install`. Either the gamepack file as `application/json`, or `multipart/form-data` with the part `gamepack` and the optional part `signature`. Limit: 100 MB.
- Query: `dryRun` (bool, default false). `templates` (repeatable string, template names, default all).

### POST /api/gamepacks/catalog/update

Update from the catalog. Name: `UpdateGamepackFromCatalog`.

- Body (`application/json`): `{ "name": string, "version": string, "dryRun": bool, "templates": [string] }`. `name` and `version` are required.

### POST /api/gamepacks/{name}/rollback?managed=true

Managed rollback. Name stays `RollbackGamepack`. New query values: `managed` (bool, default false), `dryRun` (bool, used only with `managed`), `templates` (repeatable string, used only with `managed`).

- Without `managed=true`: the behavior of feature 143. The response type stays `GamepackRollbackResponse`. `dryRun` and `templates` are ignored.
- With `managed=true`: the response is a `ManagedUpdateReport`.

## Response: ManagedUpdateReport

See `data-model.md` for all fields. Example of a done update:

```json
{
  "result": "done",
  "reason": null,
  "dryRun": false,
  "gamepack": { "name": "pns", "version": "2.0.0" },
  "previousVersion": "1.0.0",
  "stoppedQueues": [ { "id": "q1", "name": "PNS Daily", "templateName": "pns-daily" } ],
  "restartedQueues": [ { "id": "q1", "name": "PNS Daily", "templateName": "pns-daily" } ],
  "notRestartedQueues": [],
  "backupId": "20261016T101500123-pns",
  "created": 1, "updated": 3, "removed": 0,
  "newEntries": [ { "templateName": "pns-daily", "sequenceName": "pns-new-task", "position": 7 } ],
  "removedSequenceEntries": [],
  "notAffectedTemplates": [],
  "problems": [],
  "signature": { "status": "not_checked", "policy": "off", "wouldReject": false },
  "restore": { "happened": false, "restored": false, "steps": [], "failedObjects": [] }
}
```

Example of a failed update with a restore: `result` is `"failed"`, `reason` is `"booking_failed"`, and `restore` is `{ "happened": true, "restored": true, "steps": [ { "step": "gamepack_rollback", "outcome": "ok" }, { "step": "settings_import", "outcome": "ok" }, { "step": "start_queue:PNS Daily", "outcome": "ok" } ], "failedObjects": [] }`.

## Status codes

| Case | Status | Body |
|------|--------|------|
| `done` or `planned` | 200 | report |
| Dry run with a file, signature, or validation problem | 200 | report with `result` `rejected`, the `reason`, and the problems |
| New version equals the installed version (exact match of the version text) | 200 | report with `result` `done`, an empty `stoppedQueues`, no `backupId`, and no change |
| A queue does not restart | 200 | report with `result` `done` and the queue in `notRestartedQueues` |
| Malformed request: the body is not valid JSON, the multipart body is not valid, a part is missing, `name` or `version` is missing | 400 | error body with `reason` `invalid_request` |
| File content is not valid (normal call; a dry run gives 200) | 400 | error body with `reason` `invalid_file` |
| A name in `templates` matches no template (normal call and dry run, same status) | 400 | report with `result` `rejected` and `reason` `template_not_found`. This reason wins over a signature problem. A dry run also lists all other problems. |
| Check problems of the file (conflict, reference, API major), normal call | 422 | report with `result` `rejected`, `reason` `validation_failed`, and `problems` |
| Signature policy rejects (normal call only; a dry run gives 200) | 422 | report with `result` `rejected`, `reason` `signature_rejected`, and `signature` |
| Catalog entry not listed | 404 | error body with `reason` `entry_not_listed` (same status as feature 147) |
| Catalog not configured | 409 | error body with `reason` `not_configured` (same status as feature 147) |
| Other catalog failures | 400, 502, or 504 | same status mapping as feature 147, with `reason` `catalog_error` |
| Gamepack not installed (managed rollback) | 404 | error body with `reason` `gamepack_not_found` |
| No previous version (managed rollback) | 409 | error body with `reason` `no_previous_version` |
| A managed call runs on the instance (any gamepack), normal call | 409 | error body with `reason` `update_in_progress` |
| A managed call runs on the instance, dry run | 200 | report with `result` `rejected` and the problem `update_in_progress` |
| Request larger than the limit | 413 | error body with `reason` `too_large` |
| A step failed after the start | 500 | report with `result` `failed`, `reason`, and `restore` |

An error body has one field `reason` with one name from the reason list in `data-model.md`. The report field `result` has only these values: `done`, `planned`, `failed`, `rejected`.

No known failure gives an unhandled error (FR-016). An unknown exception gives a `failed` report with reason `unexpected` if the flow has started. Otherwise it gives a plain 500 problem.

## Rules that a client can rely on

1. A request with `dryRun` true changes no queue, no template, no gamepack, and no file (FR-013).
2. A new entry has `enabled` false, no schedule, and no parameter values (FR-007).
3. The platform restarts only the queues in `stoppedQueues` (FR-009).
4. When `restore.happened` is true, the installed version and the templates equal the state before the call, unless `restore.restored` is false and `failedObjects` lists the exceptions (FR-010).
5. The stop limit is 60 seconds for each queue (clarification 1).
6. The platform runs one managed call at a time on the whole instance.
7. The backup holds the affected templates only. If the update affects no queue template, the platform writes no backup. For a failed first install, the restore returns to "no gamepack installed". It removes the files that the install step created. A failed managed rollback does not call the rollback a second time.
8. A dry run reports all problems together: source, signature, scope (`template_not_found`), and conflict (`update_in_progress`). A dry run does not take the instance lock.
9. A test checks that each report has every item of FR-011 (SC-006).

## Compatibility

- No existing route, request, or response changes. `managed` is a new optional query value.
- The catalog protocol of feature 147 does not change (FR-017).
- OpenAPI: new response schemas are added with an `ISchemaFilter` and STE descriptions, as for feature 145.

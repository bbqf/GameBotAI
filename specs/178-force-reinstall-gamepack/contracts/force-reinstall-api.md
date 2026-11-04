# Contract: Force option on the gamepack install routes (178)

Only the changes are here. All other behavior of the routes stays as it is.

## Force option

| Route | How to pass force | Default |
|---|---|---|
| `POST /api/gamepacks/install` | query `force=true` | false |
| `POST /api/gamepacks/update` | query `force=true` | false |
| `POST /api/gamepacks/catalog/install` | JSON body field `force` | false |
| `POST /api/gamepacks/catalog/update` | JSON body field `force` | false |

These are exactly four routes. The managed update (`managed=true`) is a mode of the two update routes. It is not a fifth route. It also accepts `force`.

The option has an effect only when the gamepack is installed and the version in the file equals the installed version. In all other cases the option is ignored. No error code is new.

## Response change (install routes)

A new optional pair of members in the install response. Both members are absent when the call is not a forced reinstall.

```json
{
  "dryRun": false,
  "valid": true,
  "gamepack": { "name": "demo-pack", "version": "1.0.0" },
  "created": 0,
  "updated": 3,
  "removed": 0,
  "reinstalled": true,
  "overwritten": [
    { "type": "sequence", "name": "Run the demo command" }
  ]
}
```

| Member | Type | Meaning |
|---|---|---|
| `reinstalled` | boolean | True when the call was a forced reinstall of the same version. |
| `overwritten` | array of `{type, name}` | Objects whose stored content differs from the file. Equal objects are not listed. The array can be empty. |

## Response change (managed mode of the update routes)

In managed mode (`managed=true`), the report (`ManagedUpdateReportDto`) gets the same two members, with the same rules.

Behavior change: with `force=true` and the same version, the managed update no longer ends with a no-op `done` report. It runs the full flow (stop queues, back up templates, install, book entries, restart queues).

## Dry run

`dryRun=true` together with `force=true`:
- gives the normal dry-run result;
- gives `reinstalled` and `overwritten`;
- changes no object;
- makes no rollback point.

## Rollback

`POST /api/gamepacks/{name}/rollback` (plain and `managed=true`):
- If the pack has a reinstall point, the rollback restores the objects to the content from before the last forced install. The version stays X. `previous` is not touched. The reinstall point is deleted.
- If the pack has no reinstall point, the rollback works as before.
- If the pack has neither, the answer is the same as before (`409 no_previous_version`).

## List

`GET /api/gamepacks`: each item gets `hasReinstallPoint` (boolean).

## Errors

No new status code. A failed forced install gives the same `500 failed` or `500 partial_restore` answer as a normal install. No object changes after `failed` (FR-006).

## OpenAPI text

The `WithDescription` text of the four install routes and of the rollback route gets one short paragraph that names the force option, `reinstalled`, `overwritten`, and the reinstall point. The text uses Simplified Technical English.

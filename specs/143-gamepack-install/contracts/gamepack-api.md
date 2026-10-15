# Contract: Gamepack API

All routes use the existing access rules of the service API. The service shows the schemas in OpenAPI.

## POST /api/gamepacks/install

Query: `dryRun` (boolean, default false). Body: a gamepack file (`application/json`).

```json
{
  "manifest": { "name": "pns-daily", "version": "1.2.0", "changelog": "Fix help sequence.", "requiredApiMajor": 1 },
  "sequences": [ { "id": "a1", "name": "Help All", "...": "..." } ],
  "commands": [ { "id": "c1", "name": "Tap help", "...": "..." } ],
  "images": [ { "id": "help-icon", "contentType": "image/png", "dataBase64": "iVBOR..." } ]
}
```

`requiredApiMajor` is optional. The service stores it and does not check it.
The gamepack name has 1 to 64 characters from letters, digits, `_`, `-`, and `.`, and does not start with `.`.

Response 200 (valid, or dry run):

```json
{
  "dryRun": false,
  "valid": true,
  "problems": [],
  "gamepack": { "name": "pns-daily", "version": "1.2.0" },
  "created": 3, "updated": 1, "removed": 0
}
```

For a dry run, `created`, `updated`, and `removed` show what the install would do. No data changes.
An update keeps the id of each existing object with the same type and name.

Response 422 (install refused, not a dry run): same shape with `valid: false`. Nothing changes.
A dry run with problems gives 200 with `valid: false`, so a client can read the report.

```json
{
  "dryRun": true,
  "valid": false,
  "problems": [
    { "category": "conflict", "objectType": "sequence", "name": "Help All", "owner": "user", "message": "A sequence with this name exists and the user owns it." },
    { "category": "reference", "objectType": "sequence", "name": "Daily", "owner": null, "message": "Command 'c9' is not in the gamepack or the installation." }
  ]
}
```

In a problem, `owner` is text: `user` or `<name> <version>`.

- 400: the body is not valid JSON (single `structure` problem in the same shape).
- 413: the request is larger than 100 MB. Problem response. Nothing changes.

Image rules (structure problems, also in a dry run): `contentType` is an image type, `dataBase64` is valid base64, and the decoded size is at most 10,000,000 bytes. The image `id` is its name. A gamepack name that is a reserved Windows device name (CON, PRN, AUX, NUL, COM1-9, LPT1-9) is a structure problem. Names match without case.
- 500: a write failed. Problem response. All objects stay as before. If the restore also failed, the status is still 500, the body has `"result": "partial_restore"`, and it says the state can be partly restored. The service logs the failed object.

## GET /api/gamepacks

200: array of installed gamepacks.

```json
[ { "name": "pns-daily", "version": "1.2.0", "changelog": "Fix help sequence.", "requiredApiMajor": 1, "hasPreviousVersion": true, "installedAtUtc": "2026-10-15T10:00:00Z" } ]
```

An empty installation gives `[]`.

## POST /api/gamepacks/{name}/rollback

The rollback runs the same conflict and reference check as an install on the kept previous content.

- 200: `{ "name": "pns-daily", "version": "1.1.0", "result": "rolled_back", "created": 0, "updated": 1, "removed": 1 }`. After this, no previous version is kept.
- 422: the check found a problem. Body: `{ "result": "refused", "valid": false, "problems": [ ... ] }` (same problem shape as the install). Nothing changes.
- 409: `{ "result": "no_previous_version" }` (problem response). Nothing changes.
- 404: `{ "result": "not_found" }` (problem response).
- 500: a write failed. All objects stay as before. If the restore also failed, `"result": "partial_restore"`.

## Changed responses

`GET` of a sequence, a command, or an image (single and list) adds a read-only field:

```json
"owner": { "kind": "gamepack", "name": "pns-daily", "version": "1.2.0" }
```

or `"owner": { "kind": "user" }`. A write request ignores this field.

## Unchanged behavior (FR-015)

- A hand edit of an owned object is accepted. The owner record does not change.
- A hand create with the name of an owned object of the same type is refused with 409. The owner record does not change.
- A hand delete works as before. The owner entry is removed at once. A later hand-made object with the same name belongs to the user. The next install re-creates the deleted object only if no conflict exists, and a rollback restores it.
- A hand change during an install is not blocked. It can be lost or can fail. Avoid hand changes during an install.
- The service does not check references from user objects to an object that an install drops.
- The owner read shows an owner only if the object exists.

## Errors

- No route uninstalls a gamepack.
- No route changes the registry except install and rollback.

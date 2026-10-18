# Contract: gamepack install error responses

Routes: `POST /api/gamepacks/install`, the catalog install route, the rollback route, the take-over restore route.

## Fixed file error text (FR-008)

`Cannot replace file '<full path>' for <kind> '<id>': <reason>`

- `<kind>` is one of `sequence`, `command`, `image`, `template`, `registry`, `previous`. The kinds `registry` and `previous` are for the pack record files (`registry.json` and `previous\<name>.json`).
- The contract and the tests use this exact template for all kinds.
- An error has one prefix only. An image error is not wrapped a second time.
- Covered writes (FR-008a): image save, alternates save, registry `Save` and `Update` (kind `registry`), `WritePrevious` (kind `previous`), and the sequence, command and template writes.

## Failed write, restore worked

- Status: 500
- Body (problem details): `result` = `failed`; `detail` holds the fixed file error text.
- All objects hold their old content.

Example `detail`: `Cannot replace file 'C:\data\images\pit-title.png' for image 'pit-title': The process cannot access the file because it is being used by another process.`

## Failed write, restore failed

- Status: 500 (unchanged)
- Body: `result` = `partial_restore` (unchanged); `detail` as above; new member `failedRestoreKeys`: list of strings, one per file in an unknown state. Each entry uses the format `<kind>:<id> (<full file path>)`. The full path comes from the store that wrote the file. For `registry` and `previous` the id is the pack name.

Example: `"failedRestoreKeys": ["image:pit-title (C:\\data\\images\\pit-title.png)", "registry:pns (C:\\data\\gamepacks\\registry.json)"]`

## Retry rule (FR-010)

The install retries a replace only for sharing and lock violations, with a bounded wait (up to 1.55 s per file). Other errors fail at once with the fixed text.

## Not changed

Dry run response, gamepack file format, signature rules, all success responses, the `partial_restore` name and the HTTP status.

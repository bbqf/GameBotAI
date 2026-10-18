# Data Model: Fix gamepack install file replace failure

No stored data changes. The gamepack file format, the registry format and the signature rules stay as they are (FR-006).

## New type

### AtomicFile (static helper, `GameBot.Domain`)

| Member | Description |
|--------|-------------|
| `ReplaceFile(string tempPath, string targetPath, string kind, string id)` | Moves the temp file over the target with overwrite. Retries only on sharing and lock violations (Win32 code 32 or 33): 5 tries, delays 50, 100, 200, 400, 800 ms. Other errors fail at once. On failure it deletes the temp file and throws an `IOException` with the text `Cannot replace file '<full path>' for <kind> '<id>': <reason>`. The inner exception is kept. |

`kind` is one of sequence, command, image, template, registry, previous. This is a new helper, not a change to `AtomicFileWriter`. The reason is in plan.md.

### Read-only path methods (FR-009)

Each store that writes an install file gives the full path of a file for an id (or pack name), without side effects: the image, alternates, sequence, command and template stores. `GamepackRegistryStore` already holds `RegistryPath` and gives the previous-file path in the same way. The applier uses these paths to build the restore-key entries.

## Changed types

### GamepackApplyOutcome

- `FailedRestoreKeys` (exists): list of keys in an unknown state. Now it reaches the client.

### Install, rollback and take-over restore responses (problem details)

| Member | Type | Rule |
|--------|------|------|
| `result` | string | `failed` or `partial_restore`. Unchanged. |
| `failedRestoreKeys` | string[] | New. Present only when `result` is `partial_restore`. Each item uses the format `<kind>:<id> (<full file path>)`, for example `image:abc (C:\data\images\abc.png)`. One format for all kinds. |

## State transitions

Install outcome: `Applied`, `Failed` (restore worked), `PartialRestore` (restore failed for at least one key). Unchanged.

# Data Model: Take over same-named user objects on gamepack install

## Changed domain types

### GamepackInstallOptions (new record, `GameBot.Domain.Gamepacks`)

| Field | Type | Default | Meaning |
|-------|------|---------|---------|
| DryRun | bool | false | Check only. No data changes. |
| TakeOver | bool | false | Take over user objects of the same type and name. |
| SignatureText | string? | null | The detached signature text. |

### PlannedObject (change)

| Field | Type | Meaning |
|-------|------|---------|
| IsTakeOver | bool (new) | True when the write replaces a user object. `ExistingId` is the id of that object. |

Rule: `IsTakeOver` is true only when `ExistingId` is set. `IsUpdate` stays true for a take-over write, so the writer uses the update call.

### GamepackPlan (change)

| Member | Type | Meaning |
|--------|------|---------|
| Updated | int (change) | Writes with `ExistingId` set and `IsTakeOver` false. |
| TakenOver | int (new) | Writes with `IsTakeOver` true. |
| TakeOvers | IReadOnlyList&lt;TakeOverEntry&gt; (new) | One entry for each take-over write. Set by the validator. |

### TakeOverEntry (new record)

| Field | Type | Meaning |
|-------|------|---------|
| Type | string | `sequence`, `command`, or `image`. |
| Name | string | The object name. For an image, the id. |
| Identical | bool | True when the canonical content of the user object equals the canonical content of the gamepack object. |

### GamepackInstallOutcome (change)

| Field | Type | Meaning |
|-------|------|---------|
| TakenOver | int (new) | Number of objects taken over (planned for a dry run). 0 when the option is off. |
| TakeOverBackupId | string? (new) | Id of the backup. Set only for a real install that replaced at least one object. |
| TakeOvers | IReadOnlyList&lt;TakeOverEntry&gt; (new) | The list for a dry run with the option on. Empty otherwise. |

The catalog install returns this outcome, so its result has `takenOver` and `takeOverBackupId` too.

### GamepackApplyOutcome (change)

Adds `TakenOver` (int) and `TakeOverBackupId` (string?).

### GamepackWriteOperations (change)

Adds `TakeOverBackup` and `TakeOverRestore` for the fault-injection gate.

## New: take-over backup

### TakeOverBackup (stored file)

| Field | Type | Meaning |
|-------|------|---------|
| Id | string | `to-yyyyMMddTHHmmssfff-<8 hex>`. Equals the file name without the extension. |
| CreatedAtUtc | DateTimeOffset | Time of the install. |
| Gamepack | string | The gamepack name that took the objects. |
| Version | string | The gamepack version at the install. |
| Entries | list of TakeOverBackupEntry | One for each replaced object. At least one. |

### TakeOverBackupEntry

| Field | Type | Meaning |
|-------|------|---------|
| Type | string | `sequence`, `command`, or `image`. |
| Name | string | The object name (the id for an image). |
| ObjectId | string | The stored id. It does not change in the take-over. |
| Sequence | JSON object? | The old sequence. Set for type `sequence`. |
| Command | JSON object? | The old command. Set for type `command`. |
| Image | object? | `contentType`, `filename`, `dataBase64`. Set for type `image`. |
| PostHash | string | SHA-256 (lower-case hex) of the canonical content that the install wrote. The restore compares it with the live object. |

Rules:

- The id matches `^to-[0-9]{8}T[0-9]{9}-[0-9a-f]{8}$`. The store rejects any other id.
- The store writes the file before the first object write. It deletes the file when the install fails and the restore works.
- The store has no retention rule.

### ITakeOverBackupStore

| Member | Meaning |
|--------|---------|
| IsValidId(id) | True when the id has the form above. |
| Write(backup) | Writes a new file. Returns the id. |
| Read(id) | Returns the backup, or null when the id is not valid or the file is missing. |
| Delete(id) | Removes the file. No error when the file is missing. |

### TakeOverRestoreMode

`Strict` (the public route) checks `object_missing`, `object_changed`, and `owner_changed`. These are the only three checks. `Force` (internal, managed update only) skips `owner_changed`. It still checks the other two. `Force` runs inside the gate of the managed update that calls it.

### TakeOverRestoreOutcome

| Field | Type | Meaning |
|-------|------|---------|
| Status | enum | `Restored`, `NotFound`, `Refused`, `Failed`, `PartialRestore`. |
| BackupId | string | The id from the request. |
| Restored | int | Number of objects put back. |
| Reasons | list of TakeOverRestoreReason | One for each refused object. |

`TakeOverRestoreReason`: `Code` (`object_missing`, `object_changed`, `owner_changed`), `Type`, `Name`, `Message`.

## Registry

No change to the format of `gamepacks\registry.json`. A take-over adds an owner entry for each object (type:name key, gamepack, version, id) and an `Objects` ref in the gamepack record. A restore removes both for the restored objects.

## State transitions of one object

```text
user-owned --(install, takeOver on)--> gamepack-owned   [backup stored first]
gamepack-owned --(restore, strict checks pass)--> user-owned with the old content
gamepack-owned --(failed install or failed managed call)--> user-owned with the old content   [undo or force restore]
```

## Managed update report (feature 148, change)

| Field | Type | Meaning |
|-------|------|---------|
| TakenOver | int | Number of objects taken over (planned for a dry run). 0 when the option is off. |
| TakeOverBackupId | string? | The take-over backup id. Not set when nothing was replaced, for a dry run, and for a failed call. |
| TakeOvers | list of TakeOverEntry | The dry-run list. Empty otherwise. |

`BackupId` keeps its meaning: the queue template backup.

## Validation rules

- The option applies to an update (install), not to a rollback.
- A take-over needs an owner of kind user. Any other owner is a conflict (FR-005).
- An object whose type and name are not in the gamepack does not change (FR-006).
- The restore id must pass `IsValidId` before any file access.

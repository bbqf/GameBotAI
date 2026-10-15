# Data Model: Gamepack Install

## GamepackFile (request body)

| Field | Type | Rule |
|-------|------|------|
| manifest | GamepackManifest | Required |
| sequences | CommandSequence[] | Optional, default empty. Names unique (no case). |
| commands | Command[] | Optional, default empty. Names unique (no case). |
| images | GamepackImage[] | Optional, default empty. Ids unique (no case). |

A file with all three lists empty is valid. It installs a gamepack with no objects.

## GamepackManifest

| Field | Type | Rule |
|-------|------|------|
| name | string | Required. 1 to 64 characters from letters, digits, `_`, `-`, `.`. Must not start with `.`. Not a reserved Windows device name (CON, PRN, AUX, NUL, COM1-9, LPT1-9, with or without extension). Matching ignores case. Other names are a structure error (FR-019). |
| version | string | Required, not white space. No order rule. |
| changelog | string | Optional, default empty. |
| requiredApiMajor | integer | Optional. Stored and shown when present. Never checked (FR-002). |

## GamepackImage

| Field | Type | Rule |
|-------|------|------|
| id | string | Required. Valid image id (existing id rule). The image name is its id (FR-024). It is the key for conflict, owner, and id-keeping rules. |
| contentType | string | Required. An image type: `image/png` or `image/jpeg`. |
| filename | string | Optional. |
| dataBase64 | string | Required. Valid base64. Decoded size at most 10,000,000 bytes. |

All image rules are structure rules (FR-023). The dry run checks them. The total request size is at most 100 MB. A larger request gets a 413 problem response before parse.

## Object key and identity

`type:name`, with type `sequence`, `command`, or `image`. Name is `Name` for a sequence and a command, and `id` for an image. Compare names without case. Identity is type and name only (clarification 1). The id of a stored object is not part of the identity.

## Id retention (FR-020)

When an install updates an object with the same type and name, the stored id stays the same as the existing object. The file id is ignored for the stored id. The apply path rewrites references in the file objects from the file id to the kept id. A new object takes the file id.

## Registry (`<dataRoot>/gamepacks/registry.json`)

| Field | Type | Note |
|-------|------|------|
| gamepacks | map name to InstalledGamepack | |
| owners | map objectKey to OwnerEntry | Missing key = user owner (FR-005). The owner read also checks that the object exists; an entry without an object is not shown. A hand delete of an owned object removes its entry at once (FR-015). |

### InstalledGamepack

| Field | Type | Note |
|-------|------|------|
| name, version, changelog, requiredApiMajor | | From the manifest |
| installedAtUtc | datetime | |
| objects | list of {type, name, id} | Objects this version owns |
| previous | PreviousVersion or null | At most one (FR-010) |

### PreviousVersion

Same fields as InstalledGamepack without `previous`. The full object content is in `gamepacks/previous/<name>.json` (same shape as GamepackFile, image bytes in base64). The install writes this file from the live objects of the current version before it changes them (FR-022). The content includes hand edits.

### OwnerEntry

`{ gamepack: string, version: string, id: string }`.

## Owner shapes (two forms)

| Where | Shape |
|-------|-------|
| Object read (sequence, command, image) | Structured: `{ "kind": "user" }` or `{ "kind": "gamepack", "name": "...", "version": "..." }` |
| Problem report (conflict) | Text: `user` or `<name> <version>` |

## GamepackProblem

| Field | Type | Note |
|-------|------|------|
| category | `structure`, `reference`, `conflict` | |
| objectType | `manifest`, `sequence`, `command`, `image` | |
| name | string or null | |
| message | string | STE |
| owner | string or null | For a conflict only. Text form. |

## Write gate (test seam)

`IGamepackWriteGate.BeforeWrite(operation, key)`. Operations: `create`, `update`, `delete`, `previous-file`, `registry`, `restore`. Production uses a no-op. A test gate throws at the Nth call.

## State changes

- Install, new gamepack: record created, no previous.
- Install, same name and other version: previous = captured live content of the current version; current = new; dropped objects removed.
- Install, same name and same version: current updated; previous is not replaced (FR-010).
- Rollback: the kept previous content is validated (conflict and reference), then applied; previous = null; objects that the undone version added are removed.
- Failure at any step: objects, registry, and previous file return to the state before the request. If the restore fails, the result is HTTP 500 with `partial_restore` and the error is logged (FR-021).
- Hand delete of an owned object: the owner entry is removed at once. The next install re-creates the object; a rollback restores it.
- Hand changes during an install are not blocked. They can be lost or can fail (accepted limit).

## Validation rules (from the spec)

- Structure: file parse, manifest fields, name rule (FR-019), shape of each object, duplicate names, image content type, base64, and size (FR-023).
- Reference: step targets exist in the file or the installation, after removals (clarification 3).
- Conflict: same type and name with a user or other-gamepack owner (FR-006). No other conflict kind (F1).
- The same checks run for the dry run, the install, and the rollback (FR-014, FR-018).

# API Contract: Authoring Backup & Restore

**Branch**: `057-authoring-backup-restore` | **Date**: 2026-06-06

---

## `POST /api/authoring/backup`

Assembles a zip archive of the selected commands, sequences, and their referenced images. Returns the archive as a binary stream.

### Request

**Content-Type**: `application/json`

```json
{
  "commandIds": ["cmd-abc", "cmd-def"],
  "sequenceIds": ["seq-xyz"]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `commandIds` | `string[]` | No | IDs of commands to include |
| `sequenceIds` | `string[]` | No | IDs of sequences to include (commands they reference are added automatically) |

At least one non-empty list is required. IDs not found in the repository are silently skipped.

### Response — 200 OK

**Content-Type**: `application/zip`
**Content-Disposition**: `attachment; filename="gamebot-backup-YYYYMMDDHHMMSS.zip"`

Binary zip archive body. See [data-model.md](../data-model.md) for archive structure.

### Response — 400 Bad Request

```json
{ "error": "At least one commandId or sequenceId must be provided." }
```

---

## `POST /api/authoring/restore/dry-run`

Validates a backup archive and returns a conflict report. **No data is modified.**

### Request

**Content-Type**: `multipart/form-data`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `archive` | file | Yes | The zip backup archive to validate |

### Response — 200 OK

```json
{
  "hasConflicts": true,
  "conflictingCommandNames": ["AttackSequence", "Heal"],
  "conflictingSequenceNames": ["DailyRoutine"],
  "conflictingImageIds": ["img_heal_icon"],
  "totalCommands": 5,
  "totalSequences": 2,
  "totalImages": 8
}
```

| Field | Description |
|-------|-------------|
| `hasConflicts` | `true` if any conflicting names/IDs were found |
| `conflictingCommandNames` | Command names in archive that match existing commands by name |
| `conflictingSequenceNames` | Sequence names in archive that match existing sequences by name |
| `conflictingImageIds` | Image IDs in archive that match existing image IDs |
| `totalCommands` | Number of commands in the archive |
| `totalSequences` | Number of sequences in the archive |
| `totalImages` | Number of images in the archive |

### Response — 400 Bad Request

```json
{ "error": "Invalid or unrecognised backup archive format." }
```

Returned for: missing `archive` field, file is not a valid zip, manifest is missing, manifest version is unsupported.

---

## `POST /api/authoring/restore/apply`

Applies a backup archive. Overwrites any conflicting objects. The operation is as atomic as possible: on failure, all changes are rolled back. Due to file-based storage constraints, image rollback is best-effort — the system re-saves pre-loaded originals and reports any rollback failure explicitly.

**The client is expected to have already called `/dry-run` and received user confirmation before calling this endpoint.**

### Request

**Content-Type**: `multipart/form-data`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `archive` | file | Yes | The zip backup archive to restore |

### Response — 200 OK

```json
{
  "restoredCommands": 5,
  "restoredSequences": 2,
  "restoredImages": 8,
  "rolledBack": false,
  "errorMessage": null
}
```

| Field | Description |
|-------|-------------|
| `restoredCommands` | Number of commands written (including overwrites) |
| `restoredSequences` | Number of sequences written (including overwrites) |
| `restoredImages` | Number of images written (including overwrites) |
| `rolledBack` | `true` if the apply partially failed and a rollback was attempted |
| `errorMessage` | Non-null only when `rolledBack` is `true` or an error occurred |

### Response — 400 Bad Request

```json
{ "error": "Invalid or unrecognised backup archive format." }
```

### Response — 500 Internal Server Error

```json
{
  "restoredCommands": 0,
  "restoredSequences": 0,
  "restoredImages": 0,
  "rolledBack": true,
  "errorMessage": "Restore failed during file move phase. Rollback attempted. Original data restored."
}
```

Returned when the apply failed after staging writes had begun and a rollback was performed (or attempted).

---

## New Route Constants (ApiRoutes.cs)

```csharp
internal const string AuthoringBackup         = Base + "/authoring/backup";
internal const string AuthoringRestoreDryRun  = Base + "/authoring/restore/dry-run";
internal const string AuthoringRestoreApply   = Base + "/authoring/restore/apply";
```

---

## Frontend Service (`backup.ts`) Signatures

```typescript
// POST /api/authoring/backup — triggers browser file download
export async function downloadBackup(selection: BackupSelection): Promise<void>

// POST /api/authoring/restore/dry-run
export async function validateRestore(file: File): Promise<ConflictReport>

// POST /api/authoring/restore/apply
export async function applyRestore(file: File): Promise<RestoreResult>
```

All functions use `buildApiUrl()` and `buildAuthHeaders()` from `lib/api.ts`, following the existing pattern in `services/images.ts`.

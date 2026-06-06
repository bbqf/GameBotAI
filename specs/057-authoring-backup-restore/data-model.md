# Data Model: Authoring Backup & Restore

**Branch**: `057-authoring-backup-restore` | **Date**: 2026-06-06

---

## Archive Format

The backup archive is a zip file containing the following entries.

### `manifest.json`

```json
{
  "version": "1.0",
  "createdAt": "2026-06-06T10:00:00Z",
  "commandCount": 3,
  "sequenceCount": 2,
  "imageCount": 7
}
```

| Field | Type | Description |
|-------|------|-------------|
| `version` | `string` | Archive format version — currently `"1.0"` |
| `createdAt` | ISO 8601 | UTC timestamp when the archive was created |
| `commandCount` | `int` | Number of `commands/` entries |
| `sequenceCount` | `int` | Number of `sequences/` entries |
| `imageCount` | `int` | Number of `images/` entries |

### `commands/{id}.json`

Verbatim JSON serialisation produced by `FileCommandRepository` — same format already on disk. One file per command. The `id` in the filename matches `Command.Id`.

### `sequences/{id}.json`

Verbatim JSON serialisation produced by `FileSequenceRepository`. One file per sequence. The `id` in the filename matches `CommandSequence.Id`.

### `images/{id}.ext`

Binary image file (`.png` or `.jpeg`). The `id` matches `ImageAsset.Id`. Extension is derived from `ContentType`.

---

## Backend DTOs

### `BackupRequestDto`

Sent as the JSON body of `POST /api/authoring/backup`.

```csharp
public sealed class BackupRequestDto {
  public IReadOnlyList<string> CommandIds { get; init; } = Array.Empty<string>();
  public IReadOnlyList<string> SequenceIds { get; init; } = Array.Empty<string>();
}
```

| Field | Validation |
|-------|-----------|
| `CommandIds` | May be empty if only sequences selected; IDs not found in repository are silently skipped |
| `SequenceIds` | May be empty if only commands selected; at least one of the two lists must be non-empty |

### `ConflictReportDto`

Returned by `POST /api/authoring/restore/dry-run`.

```csharp
public sealed class ConflictReportDto {
  public bool HasConflicts { get; init; }
  public IReadOnlyList<string> ConflictingCommandNames { get; init; } = Array.Empty<string>();
  public IReadOnlyList<string> ConflictingSequenceNames { get; init; } = Array.Empty<string>();
  public IReadOnlyList<string> ConflictingImageIds { get; init; } = Array.Empty<string>();
  public int TotalCommands { get; init; }
  public int TotalSequences { get; init; }
  public int TotalImages { get; init; }
}
```

### `RestoreResultDto`

Returned by `POST /api/authoring/restore/apply`.

```csharp
public sealed class RestoreResultDto {
  public int RestoredCommands { get; init; }
  public int RestoredSequences { get; init; }
  public int RestoredImages { get; init; }
  public bool RolledBack { get; init; }    // true if apply partially failed and rollback was attempted
  public string? ErrorMessage { get; init; }
}
```

---

## `BackupService` Methods

Lives in `GameBot.Service.Services`. Injected repositories: `ICommandRepository`, `ISequenceRepository`, `IImageRepository`.

### `CreateBackupAsync`

```csharp
public async Task CreateBackupAsync(
    BackupRequestDto request,
    Stream outputStream,
    CancellationToken ct = default)
```

Steps:
1. For each `commandId` in `request.CommandIds`: load command, collect `ImageReferenceExtractor.ExtractImageIds(command)`
2. For each `sequenceId` in `request.SequenceIds`: load sequence, collect `SequenceStep.CommandId` references (recursively into `Body`), load those commands, collect image IDs
3. Deduplicate all command IDs, sequence IDs, image IDs
4. Open `ZipArchive` on `outputStream` in `Create` mode
5. Write `manifest.json`
6. For each command: write raw JSON file from disk (or re-serialise from loaded model) to `commands/{id}.json`
7. For each sequence: write raw JSON to `sequences/{id}.json`
8. For each image ID: open read stream from `IImageRepository.OpenReadAsync(id)` and copy to `images/{id}.ext`

### `DryRunRestoreAsync`

```csharp
public async Task<ConflictReportDto> DryRunRestoreAsync(
    Stream archiveStream,
    CancellationToken ct = default)
```

Steps:
1. Open `ZipArchive` on `archiveStream` in `Read` mode
2. Parse `manifest.json` — validate `version == "1.0"`, throw `BackupFormatException` if unsupported
3. Read command names from `commands/*.json` entries
4. Read sequence names from `sequences/*.json` entries
5. Read image IDs from `images/*` entry filenames
6. Cross-check: scan each `commands/*.json` and `sequences/*.json` entry for `ReferenceImageId` / `GateConfig.TargetId` references; verify a corresponding `images/{id}.*` entry exists in the archive; throw `BackupFormatException` listing all missing IDs if any are absent
7. Load existing: `ICommandRepository.ListAsync()`, `ISequenceRepository.ListAsync()`, `IImageRepository.ListIdsAsync()`
8. Compute conflicts: commands by `Name`, sequences by `Name`, images by `Id`
9. Return `ConflictReportDto`

### `ApplyRestoreAsync`

```csharp
public async Task<RestoreResultDto> ApplyRestoreAsync(
    Stream archiveStream,
    CancellationToken ct = default)
```

Steps:
1. Parse archive into memory: deserialise all commands, sequences; buffer all image byte streams + metadata
2. Validate manifest version — throw `BackupFormatException` if unsupported
3. Cross-check missing images: scan each `commands/*.json` and `sequences/*.json` entry for `ReferenceImageId` / `GateConfig.TargetId` references; verify a corresponding `images/{id}.*` entry exists in the archive; throw `BackupFormatException` listing all missing IDs if any are absent
4. Resolve existing objects by name (commands/sequences) and by ID (images) that will be overwritten
5. Pre-load originals of all to-be-overwritten objects in memory for rollback — **for images this means reading the existing byte stream via `IImageRepository.OpenReadAsync` before any writes begin**
6. Create temp staging directory; write all commands and sequences to temp JSON files
7. Write all images directly via `IImageRepository.SaveAsync(..., overwrite: true)` (images bypass temp staging — they are written in-place; rollback is best-effort via re-save of pre-loaded originals)
8. If any staging write (step 6) fails → delete temp dir, return with error (no data changed; images not yet written)
9. Delete conflicting command/sequence originals from real storage; move staged files to real storage locations
10. If any move fails → attempt rollback: re-save pre-loaded original images via `SaveAsync`; re-create pre-loaded original commands/sequences; clean up temp dir; return `RestoreResultDto { RolledBack = true }`
11. Clean up temp dir; return `RestoreResultDto` with counts

---

## `ImageReferenceExtractor` (static helper)

Lives in `GameBot.Service.Services` (or a nested namespace).

```csharp
internal static class ImageReferenceExtractor {
    public static IEnumerable<string> ExtractImageIds(Command command)
    public static IEnumerable<string> ExtractImageIds(IEnumerable<SequenceStep> steps)
}
```

**Command extraction**:
- `command.Detection?.ReferenceImageId`
- For each step in `command.Steps`:
  - `CommandStepType.PrimitiveTap`: `step.PrimitiveTap?.DetectionTarget.ReferenceImageId`
  - `CommandStepType.WaitForImage`: `step.WaitForImage?.DetectionTarget?.ReferenceImageId`

**Sequence step extraction** (recursive):
- `step.WaitForImage?.DetectionTarget?.ReferenceImageId`
- `step.Gate?.TargetId`
- For `Loop` steps: recurse into `step.Body`

---

## Conflict Resolution Logic (Apply)

| Object type | Conflict key | Delete strategy | Create strategy |
|-------------|-------------|----------------|----------------|
| Command | `Name` (exact, case-sensitive) | `ICommandRepository.DeleteAsync(existingId)` | `ICommandRepository.AddAsync(archiveCommand)` |
| Sequence | `Name` (exact, case-sensitive) | `ISequenceRepository.DeleteAsync(existingId)` | `ISequenceRepository.CreateAsync(archiveSequence)` |
| Image | `Id` (exact) | Overwritten via `IImageRepository.SaveAsync(..., overwrite: true)` | Same |

Non-conflicting objects (no existing object with same name/ID) are created directly without a delete step.

---

## Frontend Types

### `backup.ts` exports

```typescript
export interface BackupSelection {
  commandIds: string[];
  sequenceIds: string[];
}

export interface ConflictReport {
  hasConflicts: boolean;
  conflictingCommandNames: string[];
  conflictingSequenceNames: string[];
  conflictingImageIds: string[];
  totalCommands: number;
  totalSequences: number;
  totalImages: number;
}

export interface RestoreResult {
  restoredCommands: number;
  restoredSequences: number;
  restoredImages: number;
  rolledBack: boolean;
  errorMessage?: string;
}

export async function downloadBackup(selection: BackupSelection): Promise<void>
export async function validateRestore(file: File): Promise<ConflictReport>
export async function applyRestore(file: File): Promise<RestoreResult>
```

# Research: Authoring Backup & Restore

**Branch**: `057-authoring-backup-restore` | **Date**: 2026-06-06

All decisions resolved. No NEEDS CLARIFICATION items remain.

---

## Decision 1: Zip library

**Decision**: Use `System.IO.Compression.ZipArchive` (BCL, .NET 9.0)

**Rationale**: Already available in the runtime — no new NuGet dependency. Supports streaming write via `ZipArchiveMode.Create` against any writable `Stream`, satisfying the requirement to pipe directly to the HTTP response without buffering the entire archive on disk. `ZipArchiveMode.Read` works equivalently for restore parsing.

**Alternatives considered**: `SharpZipLib`, `DotNetZip` — rejected because they add package overhead when the BCL covers all required functionality.

---

## Decision 2: Conflict detection key for commands and sequences

**Decision**: Command conflicts detected by `Command.Name`; sequence conflicts detected by `CommandSequence.Name`. Comparison is case-sensitive and exact (matching the spec's Assumptions).

**Rationale**: `ICommandRepository` and `ISequenceRepository` expose no find-by-name methods; detection requires `ListAsync()` followed by an in-memory LINQ filter. At typical scale (≤100 commands, ≤50 sequences) this is negligible overhead. Using `Name` (not `Id`) matches user expectation — if two systems have the same command name but different machine IDs, they still conflict.

**Alternatives considered**: Conflict by `Id` — rejected because two different instances of the tool will generate different IDs for the same logical command; name-based matching is more meaningful for cross-machine backup/restore.

---

## Decision 3: Conflict detection key for images

**Decision**: Image conflicts detected by `ImageAsset.Id` (the filename without extension stored in `FileImageRepository`).

**Rationale**: Images have no user-visible unique name — `ImageAsset.Filename` is optional metadata. The identity of an image in the repository is its `Id`. FR-010 explicitly includes "same identifier" for images. `IImageRepository.ListIdsAsync()` makes this efficient.

**Alternatives considered**: Hash-based comparison — rejected as overkill for a local tool where the image ID already uniquely identifies the asset.

---

## Decision 4: Atomicity approach for restore apply

**Decision**: Two-phase write using a per-operation temp directory: write all archive objects to a temp staging area first; if all staging writes succeed, delete conflicting originals and move staged files to their real paths; on any move failure, attempt best-effort rollback (pre-load originals in memory before delete, re-create them if move fails).

**Rationale**: `FileCommandRepository` uses direct `File.Create` (not atomic rename) so there is no existing atomic primitive. A temp-stage-then-rename approach is the closest file-system-level atomicity available on Windows without a transaction file system. The spec requires "fully atomic" (A1 clarification); rollback from memory covers the most dangerous failure mode (partial overwrite). The temp directory is cleaned up regardless of outcome.

**Alternatives considered**: Write-in-place — rejected because a failure after first file written leaves partial state. Full transactional rollback via a WAL — rejected as disproportionate complexity for a local, single-user tool.

---

## Decision 5: Archive file format

**Decision**: Zip archive with the following layout:
```
manifest.json              ← version, timestamp, counts
commands/{id}.json         ← one file per command, raw JSON as stored by FileCommandRepository
sequences/{id}.json        ← one file per sequence, raw JSON as stored by FileSequenceRepository
images/{id}.png            ← binary image, extension matches ContentType
images/{id}.jpeg           ← (or jpeg — whichever the repository recorded)
```

**Rationale**: Re-using the same JSON serialisation format already on disk avoids any translation layer for commands and sequences — the files can be copied verbatim into and out of the archive. The manifest provides version detection for future format changes.

**Alternatives considered**: A custom binary format — rejected as unnecessary complexity. A single flat JSON export — rejected because it cannot embed binary image data without Base64 encoding (which inflates size significantly).

---

## Decision 6: Backup endpoint HTTP method and body

**Decision**: `POST /api/authoring/backup` with `application/json` body `{ "commandIds": [...], "sequenceIds": [...] }`.

**Rationale**: The selection payload (two ID arrays) must be sent to the server before the zip is streamed back. `GET` with a body is ambiguous in HTTP semantics; `POST` is the idiomatic choice for an action that produces a resource (the archive). The response is `application/zip` streamed directly, matching the existing `Results.Stream()` pattern used by `GET /api/images/{id}`.

**Alternatives considered**: `GET` with query-string IDs — rejected because URL length limits restrict the number of selectable IDs, and query strings are awkward for arrays. `POST` returning a presigned download URL — rejected as unnecessary complexity for a local tool.

---

## Decision 7: Restore upload pattern (two separate uploads)

**Decision**: Dry-run and apply each accept the zip as a multipart file upload (`multipart/form-data`). The user uploads the file twice — once for dry-run, once for apply.

**Rationale**: The frontend already has the file in memory (via `<input type="file">`); re-uploading it on confirm is cheap for typical archive sizes. This avoids server-side temporary file storage between dry-run and apply (which would require cleanup logic, session IDs, and TTL management).

**Alternatives considered**: Server-side session holding the archive between dry-run and apply — rejected because it requires session state management, temp-file cleanup, and introduces TOCTOU concerns if the server restarts between steps.

---

## Decision 8: Transitive command inclusion for sequences

**Decision**: When a sequence is selected for backup, all commands referenced by any `SequenceStep.CommandId` at any nesting depth (including `Body` steps in `Loop` steps) are automatically included in the archive, along with their images.

**Rationale**: A sequence archive is useless on restore without its referenced commands. Automatic inclusion prevents broken references and matches the spec's FR-001 ("commands transitively referenced by selected sequences").

**Alternatives considered**: Only include explicitly selected commands — rejected because this produces archives that fail restore validation silently (missing command references in sequences).

---

## Decision 9: Frontend download mechanism

**Decision**: `fetch` the backup endpoint → receive response as `Blob` → `URL.createObjectURL(blob)` → programmatically click a hidden `<a download="...">` element → `URL.revokeObjectURL` after click.

**Rationale**: This is the standard browser-side file download pattern when the content requires authentication headers (which the existing `buildAuthHeaders()` utility provides). Direct `<a href="...">` links cannot attach auth headers.

**Alternatives considered**: `window.open` with a query-string token — rejected because it would require embedding auth tokens in the URL. Server-side presigned URL — rejected as unnecessary for a local tool.

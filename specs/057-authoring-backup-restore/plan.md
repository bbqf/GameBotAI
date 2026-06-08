# Implementation Plan: Authoring Backup & Restore

**Branch**: `057-authoring-backup-restore` | **Date**: 2026-06-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/057-authoring-backup-restore/spec.md`

## Summary

Add selective backup and atomic restore of commands, sequences, and their referenced images to the Authoring section of the GameBot web UI. A user selects objects via a new "Backup & Restore" tab, downloads a server-assembled zip archive, and can later restore it with server-side dry-run conflict detection and overwrite confirmation. The implementation spans three new ASP.NET Core endpoints, a `BackupService` orchestration class, a new React page, a TypeScript service module, and corresponding tests.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0 (backend); TypeScript 5 / React 18 (frontend)
**Primary Dependencies**: `System.IO.Compression` (ZipArchive — BCL, no new NuGet); `@testing-library/react`, Jest (frontend tests)
**Storage**: Existing file-based repos — `FileCommandRepository`, `FileSequenceRepository`, `FileImageRepository` all used read-only (plus overwrite writes for restore apply)
**Testing**: xUnit + integration tests (backend); Jest + React Testing Library (frontend)
**Target Platform**: Windows desktop — locally served web UI (ASP.NET Core + React/Vite)
**Project Type**: Web application — ASP.NET Core Minimal API backend + React/Vite frontend
**Performance Goals**: Backup zip assembly completes in ≤30 s for a typical authoring dataset (≤100 commands, ≤50 sequences, ≤200 images); restore dry-run and apply each complete within the same budget (SC-001/SC-002)
**Constraints**: No new NuGet packages; `System.IO.Compression.ZipArchive` is in .NET BCL; all file I/O is local; single concurrent user; `ICommandRepository` and `ISequenceRepository` have no find-by-name methods — name lookups require `ListAsync()` + in-memory filter
**Scale/Scope**: Typically ≤100 commands, ≤50 sequences, ≤200 images; single user

## Constitution Check

*GATE: Must pass before implementation.*

| Principle | Status | Notes |
|---|---|---|
| I. Code Quality | ✅ Pass | CamelCase methods only; `BackupService` ≤50 LOC per method via private helpers; no dead code planned |
| II. Testing | ✅ Pass | Unit tests for `BackupService` (archive composition, image-ref extraction, conflict detection); integration tests for all three endpoints; React component tests for `BackupRestorePage` required |
| III. UX Consistency | ✅ Pass | Follows existing tab/page pattern from `Nav.tsx`; error messages actionable with remediation hints |
| IV. Performance | ✅ Pass | ≤30 s budget declared; streaming zip (write-to-stream, no temp copy on backup); perf note required in PR for archive assembly path |

No violations. No complexity-tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/057-authoring-backup-restore/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── contracts/
│   └── api-backup-restore.md
└── tasks.md             ← Phase 2 output (/speckit-tasks)
```

### Source Code

```text
src/GameBot.Service/
├── ApiRoutes.cs                          ← ADD 3 new route constants
├── Endpoints/
│   └── BackupRestoreEndpoints.cs         ← NEW: 3 minimal API handlers
├── Services/
│   └── BackupService.cs                  ← NEW: orchestration service
└── Contracts/Backup/
    ├── BackupRequestDto.cs               ← NEW: selection payload
    ├── ConflictReportDto.cs              ← NEW: dry-run response
    └── RestoreResultDto.cs               ← NEW: apply response

tests/
├── unit/
│   └── BackupServiceTests.cs             ← NEW: unit tests
└── integration/
    └── BackupRestoreEndpointsTests.cs    ← NEW: integration tests

src/web-ui/src/
├── components/
│   └── Nav.tsx                           ← MODIFY: add 'Backup & Restore' tab
├── pages/
│   └── BackupRestorePage.tsx             ← NEW: backup & restore UI
├── services/
│   └── backup.ts                         ← NEW: API client
└── App.tsx                               ← MODIFY: wire new tab
```

**Structure Decision**: Web application (backend + frontend split). Backend adds one new service class and one new endpoints file, following the existing `{Feature}Service.cs` / `{Feature}Endpoints.cs` pattern. Frontend follows the existing `Nav.tsx` tab + `*Page.tsx` pattern exactly.

## Phase 0: Research

See [research.md](research.md) — all decisions resolved, no NEEDS CLARIFICATION items remain.

## Phase 1: Design

See [data-model.md](data-model.md) and [contracts/api-backup-restore.md](contracts/api-backup-restore.md).

### Key Design Decisions

**Backup endpoint** (`POST /api/authoring/backup`):
- Accepts JSON body `{ commandIds: [...], sequenceIds: [...] }`
- `BackupService.CreateBackupAsync` collects the selected commands and their images, collects the selected sequences (plus the commands they transitively reference and those commands' images), deduplicates, then streams a zip directly to the HTTP response
- Zip structure: `manifest.json`, `commands/{id}.json`, `sequences/{id}.json`, `images/{id}.ext`
- Response: `application/zip`, `Content-Disposition: attachment; filename="gamebot-backup-YYYYMMDDHHMMSS.zip"`

**Dry-run endpoint** (`POST /api/authoring/restore/dry-run`):
- Accepts multipart upload of zip file
- `BackupService.DryRunRestoreAsync` reads the manifest and object names from the archive, calls `ICommandRepository.ListAsync()` and `ISequenceRepository.ListAsync()` and `IImageRepository.ListIdsAsync()`, compares by name (commands/sequences) and by ID (images), returns a `ConflictReportDto`
- No data is written; the zip stream is consumed and discarded

**Apply endpoint** (`POST /api/authoring/restore/apply`):
- Accepts multipart upload of zip file (re-upload, same archive)
- `BackupService.ApplyRestoreAsync` performs the full atomic restore:
  1. Parse all objects out of the archive into memory
  2. For each command/sequence in archive: find existing by name → collect IDs to delete
  3. Write all archive objects to a staging temp directory
  4. If all staging writes succeed: delete old conflicting files, move staged files to real locations
  5. If any move fails: attempt rollback by moving staged back and re-saving pre-loaded originals; report failure with rollback status
- Images use `IImageRepository.SaveAsync(id, stream, contentType, filename, overwrite: true)`

**Image reference extraction** (static helper, `ImageReferenceExtractor`):
- For each `CommandStep`:
  - `PrimitiveTap?.DetectionTarget.ReferenceImageId`
  - `WaitForImage?.DetectionTarget?.ReferenceImageId`
- `Command.Detection?.ReferenceImageId`
- For each `SequenceStep` (recursive into `Body` for loop steps):
  - `WaitForImage?.DetectionTarget?.ReferenceImageId`
  - `GateConfig?.TargetId` (also an image ID)
  - `CommandId` → resolved to Command → then apply command extraction

**Frontend flow**:
- `Nav.tsx`: add `'Backup & Restore'` to `AuthoringTab` union and `tabs` array
- `App.tsx`: add tab render branch `{tab === 'Backup & Restore' && <BackupRestorePage />}`
- `BackupRestorePage.tsx`: two sections
  - **Backup**: fetches commands + sequences lists; checkboxes with "Select all"; "Download Backup" button (disabled if nothing selected) → calls `backup.ts:downloadBackup()` → blob download via `URL.createObjectURL` + hidden `<a>` click
  - **Restore**: file `<input type="file" accept=".zip">` → on file selected, calls `backup.ts:validateRestore()` → displays conflict report → confirm/cancel modal → on confirm calls `backup.ts:applyRestore()`
- Loading states per phase; actionable error messages for every failure path

## Complexity Tracking

No constitution violations requiring justification.

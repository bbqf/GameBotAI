# Tasks: Authoring Backup & Restore

**Input**: Design documents from `specs/057-authoring-backup-restore/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/api-backup-restore.md ✅

**Tech stack**: C# 12 / .NET 9.0 (ASP.NET Core Minimal APIs) + TypeScript 5 / React 18 (Vite)
**Tests**: Required by constitution (≥80% line / ≥70% branch on touched areas)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks in same phase)
- **[Story]**: User story this task belongs to (US1/US2/US3)
- Exact file paths included in every task description

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add route constants, DTO contracts, and a custom exception — all pre-conditions for every subsequent phase.

- [X] T001 Add `AuthoringBackup`, `AuthoringRestoreDryRun`, `AuthoringRestoreApply` constants to `src/GameBot.Service/ApiRoutes.cs`
- [X] T002 [P] Create `src/GameBot.Service/Contracts/Backup/BackupRequestDto.cs` with `CommandIds` and `SequenceIds` string-list properties
- [X] T003 [P] Create `src/GameBot.Service/Contracts/Backup/ConflictReportDto.cs` with `HasConflicts`, `ConflictingCommandNames`, `ConflictingSequenceNames`, `ConflictingImageIds`, `TotalCommands`, `TotalSequences`, `TotalImages` properties
- [X] T004 [P] Create `src/GameBot.Service/Contracts/Backup/RestoreResultDto.cs` with `RestoredCommands`, `RestoredSequences`, `RestoredImages`, `RolledBack`, `ErrorMessage` properties
- [X] T005 [P] Create `src/GameBot.Service/Services/BackupFormatException.cs` — custom exception for unrecognised or version-mismatched archive format

**Checkpoint**: Route constants and DTO types available for use in all subsequent phases.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core service skeleton, endpoints registration, and frontend scaffolding that ALL user stories depend on. No user story work begins until this phase is complete.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 Create `src/GameBot.Service/Services/ImageReferenceExtractor.cs` — static internal class with stub methods `ExtractImageIds(Command)` and `ExtractImageIds(IEnumerable<SequenceStep>)` returning empty sequences
- [X] T007 Create `src/GameBot.Service/Services/BackupService.cs` — class with constructor injecting `ICommandRepository`, `ISequenceRepository`, `IImageRepository`; stub out `CreateBackupAsync`, `DryRunRestoreAsync`, `ApplyRestoreAsync` method signatures per data-model.md
- [X] T008 Register `BackupService` as a singleton in `src/GameBot.Service/Program.cs` (matching the existing `AddSingleton` pattern used for all repository registrations — avoids captive dependency)
- [X] T009 Create `src/GameBot.Service/Endpoints/BackupRestoreEndpoints.cs` — `MapBackupRestoreEndpoints` extension method that registers the three routes (`POST /api/authoring/backup`, `POST /api/authoring/restore/dry-run`, `POST /api/authoring/restore/apply`) as stub handlers returning `Results.StatusCode(501)`; call `MapBackupRestoreEndpoints()` from `src/GameBot.Service/Program.cs`
- [X] T010 [P] Add `'Backup & Restore'` to `AuthoringTab` union type and `tabs` array in `src/web-ui/src/components/Nav.tsx`
- [X] T011 [P] Create `src/web-ui/src/pages/BackupRestorePage.tsx` — scaffold with two empty sections (`<section>` for Backup and `<section>` for Restore) and a placeholder heading each
- [X] T012 [P] Add `{tab === 'Backup & Restore' && <BackupRestorePage />}` render branch to the authoring section in `src/web-ui/src/App.tsx`; import `BackupRestorePage`
- [X] T013 [P] Create `src/web-ui/src/services/backup.ts` — skeleton module exporting `BackupSelection`, `ConflictReport`, `RestoreResult` interfaces and stub `downloadBackup`, `validateRestore`, `applyRestore` async functions (throw `Error('not implemented')`)

**Checkpoint**: Service skeleton compiles, endpoints return 501, new tab appears in Authoring nav and renders the empty page.

---

## Phase 3: User Story 1 — Backup Download (Priority: P1) 🎯 MVP

**Goal**: User selects commands and/or sequences, clicks "Download Backup", and receives a valid zip archive.

**Independent Test**: Select at least one command (with an image-referencing step) and one sequence, click "Download Backup", open the zip, and verify it contains `manifest.json`, `commands/{id}.json`, `sequences/{id}.json`, `images/{id}.ext` for all referenced images.

### Implementation

- [X] T014 [US1] Implement `ImageReferenceExtractor.ExtractImageIds(Command command)` in `src/GameBot.Service/Services/ImageReferenceExtractor.cs` — collect `ReferenceImageId` from `Command.Detection`, `PrimitiveTap.DetectionTarget`, and `WaitForImage.DetectionTarget` across all steps
- [X] T015 [US1] Implement `ImageReferenceExtractor.ExtractImageIds(IEnumerable<SequenceStep> steps)` in `src/GameBot.Service/Services/ImageReferenceExtractor.cs` — collect `WaitForImage.DetectionTarget?.ReferenceImageId`, `Gate?.TargetId`, and recurse into `step.Body` for `Loop` steps
- [X] T016 [US1] Implement `BackupService.CreateBackupAsync(BackupRequestDto, Stream, CancellationToken)` in `src/GameBot.Service/Services/BackupService.cs`:
  - Load selected commands and sequences; skip IDs not found
  - Expand sequences: collect all `SequenceStep.CommandId` values (recursive into `Body`), load those commands if not already selected
  - Deduplicate command IDs, sequence IDs, image IDs
  - Open `ZipArchive` on output stream in `Create` mode; write `manifest.json`, `commands/{id}.json` (re-serialised), `sequences/{id}.json`, `images/{id}.ext` (streamed from `IImageRepository.OpenReadAsync`)
- [X] T017 [US1] Replace stub backup handler in `src/GameBot.Service/Endpoints/BackupRestoreEndpoints.cs` with full implementation: deserialise `BackupRequestDto`, validate at least one ID provided (return 400 otherwise), call `BackupService.CreateBackupAsync` writing to response body stream, set `Content-Type: application/zip` and `Content-Disposition: attachment; filename="gamebot-backup-{timestamp}.zip"`
- [X] T018 [P] [US1] Write unit tests for `ImageReferenceExtractor` (both overloads: command with multiple step types, sequence with nested loop body) in `tests/unit/ImageReferenceExtractorTests.cs` (new file — kept separate to allow parallel execution with T019)
- [X] T019 [P] [US1] Write unit tests for `BackupService.CreateBackupAsync` (mock repos: verify zip entries match selected objects + transitively included commands + images) in `tests/unit/BackupServiceTests.cs`
- [X] T020 [US1] Write integration test for `POST /api/authoring/backup` in `tests/integration/BackupRestoreEndpointsTests.cs`: seed commands and sequences, POST with selection, assert 200 + `application/zip` response, parse zip and verify manifest and entry names
- [X] T021 [P] [US1] Implement backup section UI in `src/web-ui/src/pages/BackupRestorePage.tsx`: fetch commands and sequences lists on mount; render two checkbox groups (Commands, Sequences) with Select All; "Download Backup" button disabled when nothing selected; show loading spinner while fetching lists
- [X] T022 [US1] Implement `downloadBackup(selection: BackupSelection): Promise<void>` in `src/web-ui/src/services/backup.ts` using `buildApiUrl`, `buildAuthHeaders`, `fetch` → `response.blob()` → `URL.createObjectURL` → hidden `<a download>` click → `URL.revokeObjectURL`
- [X] T023 [US1] Wire "Download Backup" button in `src/web-ui/src/pages/BackupRestorePage.tsx` to call `downloadBackup`, show loading state during request, show actionable error message on failure
- [X] T024 [US1] Write frontend tests for backup section in `src/web-ui/src/pages/__tests__/BackupRestorePage.test.tsx`: renders command/sequence lists, Select All works, button disabled when nothing selected, calls `downloadBackup` with correct IDs on click

**Checkpoint**: User can navigate to "Backup & Restore" tab, select objects, and download a valid zip archive. Backend integration test passes.

---

## Phase 4: User Story 2 — Restore (No Conflicts) (Priority: P1)

**Goal**: User uploads a backup archive, server finds no conflicts, user confirms, all objects are restored.

**Independent Test**: On an empty or clean instance, upload a previously downloaded backup and confirm. Verify all commands, sequences, and images appear in their respective authoring lists with correct data.

### Implementation

- [X] T025 [US2] Implement `BackupService.DryRunRestoreAsync(Stream, CancellationToken)` in `src/GameBot.Service/Services/BackupService.cs`:
  - Open `ZipArchive` in `Read` mode; parse `manifest.json` — throw `BackupFormatException` if missing or `version != "1.0"`
  - Read command names from `commands/*.json` entries; read sequence names from `sequences/*.json` entries; collect image IDs from `images/*` filenames
  - Cross-check: scan each `commands/*.json` and `sequences/*.json` entry for `ReferenceImageId` / `GateConfig.TargetId` references and verify a corresponding `images/{id}.*` entry exists in the archive; throw `BackupFormatException` listing missing IDs if any are absent
  - Call `ICommandRepository.ListAsync()`, `ISequenceRepository.ListAsync()`, `IImageRepository.ListIdsAsync()`
  - Compute `ConflictReportDto` (commands by Name, sequences by Name, images by Id)
- [X] T026 [US2] Replace stub dry-run handler in `src/GameBot.Service/Endpoints/BackupRestoreEndpoints.cs` with full implementation: accept multipart upload, call `DryRunRestoreAsync`, return `ConflictReportDto` as JSON; return 400 with error message on `BackupFormatException`
- [X] T027 [US2] Implement `BackupService.ApplyRestoreAsync(Stream, CancellationToken)` in `src/GameBot.Service/Services/BackupService.cs`:
  - Parse entire archive into memory (deserialise commands, sequences; buffer **all image byte streams + metadata** before any writes)
  - Validate manifest version (throw `BackupFormatException` on mismatch)
  - Cross-check missing images: same check as T025 — verify every image ID referenced in the archive's command/sequence JSON has a corresponding `images/{id}.*` entry; throw `BackupFormatException` listing missing IDs if any are absent (guards against callers that skip dry-run)
  - Resolve conflicting existing objects by name (commands/sequences) and by ID (images); **pre-load all conflicting originals in memory for rollback — for images this means reading the existing byte stream via `IImageRepository.OpenReadAsync` before overwriting**
  - Write all commands and sequences to a temp staging directory under `Path.GetTempPath()`; write images via `IImageRepository.SaveAsync(..., overwrite: true)`
  - If staging writes succeed: delete conflicting command/sequence originals, move staged files to real storage; images are already written
  - If any move fails: attempt rollback — re-save pre-loaded original images via `SaveAsync`, re-create pre-loaded original commands/sequences; clean up temp dir; return `RestoreResultDto { RolledBack = true }` (note: rollback is best-effort for images since `SaveAsync` already completed)
  - On full success: clean up temp dir; return `RestoreResultDto` with counts
- [X] T028 [US2] Replace stub apply handler in `src/GameBot.Service/Endpoints/BackupRestoreEndpoints.cs` with full implementation: accept multipart upload, call `ApplyRestoreAsync`, return `RestoreResultDto` as JSON; return 400 on `BackupFormatException`; return 500 on `RolledBack = true`
- [X] T029 [P] [US2] Write unit tests for `BackupService.DryRunRestoreAsync` in `tests/unit/BackupServiceTests.cs`: valid archive no conflicts → empty report; valid archive with name matches → populated conflict lists; missing manifest → `BackupFormatException`
- [X] T030 [US2] Write unit tests for `BackupService.ApplyRestoreAsync` (no-conflict scenario: mock repos, verify `AddAsync` called for each object, `DeleteAsync` not called) in `tests/unit/BackupServiceTests.cs` (sequential after T029 — same file)
- [X] T031 [US2] Write integration tests for dry-run (no-conflict) and apply (no-conflict) in `tests/integration/BackupRestoreEndpointsTests.cs`: seed no existing objects, upload archive, assert dry-run returns `hasConflicts: false`, apply returns 200 with counts, verify objects exist in repos after apply
- [X] T032 [P] [US2] Implement restore section UI in `src/web-ui/src/pages/BackupRestorePage.tsx`: file input (`accept=".zip"`), "Upload & Check" button; on file selected call `validateRestore()`, show loading state; on success show a confirmation dialog with archive summary (counts)
- [X] T033 [US2] Implement `validateRestore(file: File): Promise<ConflictReport>` and `applyRestore(file: File): Promise<RestoreResult>` in `src/web-ui/src/services/backup.ts` using `FormData` + `buildApiUrl` + `buildAuthHeaders`
- [X] T034 [US2] Wire confirmation dialog "Confirm Restore" button to call `applyRestore`, show loading state, show success message with counts or actionable error message on failure in `src/web-ui/src/pages/BackupRestorePage.tsx`
- [X] T035 [US2] Write frontend tests for restore flow (no-conflict path) in `src/web-ui/src/pages/__tests__/BackupRestorePage.test.tsx`: file input renders, `validateRestore` called on upload, confirmation dialog shown with counts, `applyRestore` called on confirm, success message shown

**Checkpoint**: Full restore round-trip works on a clean instance. Integration test passes. User can see success confirmation in the UI.

---

## Phase 5: User Story 3 — Restore with Conflicts (Priority: P1)

**Goal**: When the dry-run finds conflicts, the UI shows exactly which objects will be overwritten; user can confirm overwrite or cancel with no data changed.

**Independent Test**: Restore a backup on an instance that has at least one command and one sequence with matching names. Verify the conflict report lists their names, confirm overwrites them, and cancel leaves the originals intact.

### Implementation

- [X] T036 [US3] Extend confirmation dialog in `src/web-ui/src/pages/BackupRestorePage.tsx` to render conflict details when `conflictReport.hasConflicts` is true: show lists of `conflictingCommandNames`, `conflictingSequenceNames`, `conflictingImageIds` with a warning that these will be overwritten; render cancel button that closes dialog and makes no API call
- [X] T037 [US3] Write integration tests for apply with conflicting objects in `tests/integration/BackupRestoreEndpointsTests.cs`: seed commands/sequences matching archive names, run dry-run (assert conflicts returned), run apply (assert 200), verify existing objects replaced with archive data and non-conflicting archive objects created
- [X] T038 [P] [US3] Write unit tests for `BackupService.ApplyRestoreAsync` in `tests/unit/BackupServiceTests.cs`:
  - Conflict scenario: mock repos, verify `DeleteAsync` called for conflicting originals, `AddAsync` called for all archive objects
  - Rollback-failure scenario: simulate a file-move failure after staging; assert `RestoreResultDto.RolledBack == true`, pre-loaded original commands/sequences re-created via repo, pre-loaded original image bytes re-saved via `SaveAsync`
- [X] T039 [US3] Write frontend tests for restore flow (conflict path) in `src/web-ui/src/pages/__tests__/BackupRestorePage.test.tsx`: dialog shows conflict lists, "Cancel" closes dialog without calling `applyRestore`, "Confirm Overwrite" calls `applyRestore`

**Checkpoint**: All three user stories fully functional. Conflict report is surfaced clearly. Cancel leaves data unchanged. Integration tests cover both conflict and no-conflict scenarios.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Error handling completeness, edge cases, build validation.

- [X] T040 [P] Verify all three endpoints return 400 with `{ "error": "..." }` for: invalid zip, missing `archive` field, unsupported manifest version, and missing images referenced in archive (per spec edge case) in `src/GameBot.Service/Endpoints/BackupRestoreEndpoints.cs`; add integration test cases for each scenario in `tests/integration/BackupRestoreEndpointsTests.cs`
- [X] T041 [P] Add empty-selection guard to backup UI in `src/web-ui/src/pages/BackupRestorePage.tsx`: if commands and sequences lists are both empty, show empty-state message and disable "Download Backup" button (per edge case in spec)
- [X] T042 Add rollback failure reporting to `BackupRestorePage.tsx`: when `applyRestore` returns `{ rolledBack: true }`, show a distinct warning message explaining partial failure and suggesting manual verification (sequential after T041 — same file)
- [X] T043 Final quality gate — complete all of the following before marking the feature done:
  - Run `dotnet test` and `npx jest` (+ `npm run build`); address any failures, linting, or type errors
  - Add XML doc-comments (`<summary>`, `<param>`, `<returns>`, `<exception>`) to all public/internal methods in `BackupService.cs` and `ImageReferenceExtractor.cs` per constitution Principle I
  - Record a perf note in the PR description with measured archive assembly time for the typical dataset (≤100 commands, ≤50 sequences, ≤200 images) per constitution Principle IV; flag if ≥30 s (SC-001/SC-002)
  - Run a manual smoke test with a seeded typical dataset; record actual backup and restore durations in the PR perf note

**Checkpoint**: All quality gates pass. Feature is shippable.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately; T002, T003, T004, T005 fully parallel
- **Phase 2 (Foundational)**: Depends on Phase 1 — blocks all user story phases; T010–T013 parallel with T006–T009
- **Phase 3 (US1)**: Depends on Phase 2 completion
- **Phase 4 (US2)**: Depends on Phase 2 completion; may proceed in parallel with Phase 3 on the backend side (different service methods), but frontend restore section depends on the `BackupRestorePage` scaffold from T011
- **Phase 5 (US3)**: Depends on Phase 4 (extends same UI component and same endpoints)
- **Phase 6 (Polish)**: Depends on Phases 3–5

### User Story Dependencies

- **US1**: Depends only on Phase 2 — independently deliverable (backup without restore)
- **US2**: Depends only on Phase 2 — backend independently implementable after foundation; frontend depends on US1's page scaffold
- **US3**: Depends on US2 — extends the restore confirmation dialog and adds conflict-scenario integration tests

### Within Each Phase

- Backend service method → backend endpoint handler (T014–T016 before T017)
- Unit tests written alongside service implementation (T018, T019 parallel with T016)
- Frontend service module (`backup.ts`) function before wiring to UI component
- Integration tests after endpoint handlers are complete

---

## Parallel Execution Examples

### Phase 3 (US1) Parallel Opportunities

```
Parallel group A (backend):
  T014 — ImageReferenceExtractor.ExtractImageIds(Command)
  T015 — ImageReferenceExtractor.ExtractImageIds(SequenceStep[])

Sequential after A:
  T016 — BackupService.CreateBackupAsync
  T017 — Backup endpoint handler

Parallel group B (backend tests):
  T018 — Unit tests: ImageReferenceExtractor
  T019 — Unit tests: CreateBackupAsync

Parallel group C (frontend, independent of A/B):
  T021 — Backup selection UI
  T022 — downloadBackup() service function
```

### Phase 4 (US2) Parallel Opportunities

```
Parallel group A (service methods):
  T025 — DryRunRestoreAsync (can start after T016 complete)
  T027 — ApplyRestoreAsync (can start after T016 complete)

Parallel group B (tests alongside implementation):
  T029 — Unit tests: DryRunRestoreAsync  [parallel with T032 — different files]

Sequential after T029:
  T030 — Unit tests: ApplyRestoreAsync (no-conflict)  [same file as T029; not [P]]

Parallel group C (frontend, independent):
  T032 — Restore section UI
  T033 — validateRestore() + applyRestore() service functions
```

---

## Implementation Strategy

### MVP First (US1 Only — Phases 1–3)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational)
2. Complete Phase 3 (US1 — Backup Download)
3. **STOP and VALIDATE**: Select objects, download zip, inspect archive contents
4. Demo backup capability independently

### Incremental Delivery

1. Phases 1–3 → Backup working → Demo/validate
2. Phase 4 → Conflict-free restore → Demo full round-trip on clean instance
3. Phase 5 → Conflict overwrite → Demo overwrite confirmation flow
4. Phase 6 → Polish and quality gates → Ship

---

## Notes

- `[P]` tasks touch different files and have no unmet dependencies within the phase
- `[Story]` labels map tasks to spec.md user stories for traceability
- `BackupService.ApplyRestoreAsync` is the most complex task — temp-staging approach requires careful temp-dir cleanup in all code paths (success, staging failure, move failure, rollback)
- Images are identified by ID for conflict detection; commands/sequences by Name (case-sensitive) per spec Assumptions
- CamelCase methods only per constitution (no underscores)
- Run `dotnet test` and `npx jest` after each phase checkpoint before proceeding

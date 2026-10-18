# Implementation Plan: Fix gamepack install file replace failure

**Branch**: `157-fix-gamepack-install-replace-file` | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/157-fix-gamepack-install-replace-file/spec.md`. Closes #333.

## Summary

A real gamepack install with take-over fails on Windows with the message "Unable to remove the file to be replaced". The message is the .NET text for Windows error 1175. It comes from `File.Replace`, which `ImageRepository`, `ImageAlternatesRepository` and `GamepackRegistryStore` call once, with no retry. The applier then hides the file name, and the response never sends the list of files in an unknown state.

Technical approach (details in [research.md](research.md)):

- Add one static helper, `AtomicFile.ReplaceFile`, in `GameBot.Domain`. It uses `File.Move(overwrite: true)`, retries only sharing and lock violations (5 tries, 50 ms to 800 ms), and throws one fixed message: `Cannot replace file '<full path>' for <kind> '<id>': <reason>`.
- Use the helper in every install-path write that calls `File.Replace` or `WriteAtomic` (FR-008a): image save, alternates save, registry `Save`, registry `Update`, and `WritePrevious`.
- Make the sequence, command and template writes use the same message text for I/O errors.
- Send `failedRestoreKeys` in the `partial_restore` response. Each entry is `<kind>:<id> (<full file path>)`. The install code gets each path from the store that wrote the file, through a small read-only path method on each store.
- Keep the backup-and-restore model, the dry run, the file format and the signature rules.

## Technical Context

**Language/Version**: C# on .NET (the repo target framework), ASP.NET Core minimal API
**Primary Dependencies**: none new. Uses `System.IO` only.
**Storage**: files under the data directory (images, sequences, commands, templates, `gamepacks\registry.json`, `gamepacks\previous\<name>.json`)
**Testing**: xUnit. Unit tests in `tests\unit`, contract tests in `tests\contract`, integration tests in `tests\integration`. Lock tests need Windows and skip on other systems.
**Target Platform**: Windows service host. CI runs on `windows-latest` (the task list states this).
**Project Type**: web-service (`GameBot.Service`) with a domain library (`GameBot.Domain`)
**Performance Goals**: no new goal. A lock adds at most 1.55 s per file, and only when a lock exists.
**Constraints**: no change to the dry run, the gamepack format, the signature rules, the `partial_restore` name or its HTTP status. No behavior change for `AtomicFileWriter` and `ReferenceImageStore`.
**Scale/Scope**: 259 objects per install (PNS 1.0.0: 12 sequences, 130 commands, 117 images, 1 seed template)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists. Status: build and tests pass on the branch base.

*NON-NEGOTIABLE*: All text in this plan and its artifacts obeys Simplified Technical English (Principle VI). Status: pass. Code comments and the error message use STE.

- I. Code quality: one small helper replaces three copies of a fragile call. Pass.
- II. Testing: a root-cause test fails before the fix (SC-004). Tests cover the 259-object install, read-only target, mixed install, `failed` with a working restore, and `partial_restore` with the key list. Pass.
- III. UX consistency / phone check: no web UI change. Not applicable.
- IV. Performance: the retry is bounded and only runs for sharing and lock violations. Pass.
- V. Living documentation: update `docs\architecture.md` (persistence note) and its "Last reviewed" date, and the OpenAPI schema filter for `failedRestoreKeys`. Pass.
- VI. STE: pass.

Post-design re-check: pass. No violation. The Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/157-fix-gamepack-install-replace-file/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── install-error.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Utils/AtomicFile.cs                   # new helper
├── Images/ImageRepository.cs             # SaveAsync uses the helper; add read-only path method
├── Images/ImageAlternatesRepository.cs   # SetAlternates uses the helper; add read-only path method
├── Gamepacks/GamepackRegistryStore.cs    # WriteAtomic uses the helper (Save, Update, WritePrevious)
├── Gamepacks/GamepackApplier.cs          # build error text and restore-key entries with paths
├── Commands/ QueueTemplates/             # sequence, command and template file repositories: read-only path method
src/GameBot.Service/
├── Endpoints/GamepacksEndpoints.cs       # WriteFailed carries failedRestoreKeys (all call sites)
└── (OpenAPI schema filter)               # describe failedRestoreKeys
tests/
├── unit/                                 # AtomicFile, ImageRepository, alternates, registry store, applier
├── contract/                             # failed and partial_restore responses
└── integration/                          # 259-object install, mixed install, read-only target
docs/architecture.md                      # persistence note and Last reviewed
specs/STATUS.md                           # feature entry (FR-014)
```

**Structure Decision**: Single web-service solution with a domain library, as already in the repo. The helper lives in `GameBot.Domain` so the three stores can share it.

## Design decisions (summary)

- **New helper, not `AtomicFileWriter` (FR-013)**: `AtomicFileWriter` calls `File.Replace` first (the failing call), then falls back to a non-atomic delete and move with an unfiltered catch. It has no retry and no file-named error. Callers already hold a temp file. A change would alter log-writer behavior, which the spec forbids. See research D-001.
- **Write coverage (FR-008a)**: the task list re-runs a search for `WriteAtomic` and `File.Replace` in the gamepack code. The known list is `Save`, `Update` (kind `registry`) and `WritePrevious` (kind `previous`) in `GamepackRegistryStore`, plus the image and alternates repositories.
- **Path source for restore keys (FR-009)**: add a small read-only method on each store that returns the full file path for an id (or name). The applier calls it when it builds a restore-key entry. Registry and previous use the paths the registry store already holds (`RegistryPath`, `PreviousPath`).
- **Single prefix (FR-008)**: the helper throws the final message. The applier does not wrap an image error again. A test checks this.
- **Call sites of `WriteFailed` (spec assumption)**: the task list re-checks every call site (take-over restore, install, rollback, catalog install) so each carries the key list and the message.

## Complexity Tracking

No violations.

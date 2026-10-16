# Implementation Plan: Take over same-named user objects on gamepack install

**Branch**: `149-gamepack-takeover-user-objects` | **Date**: 2026-10-16 | **Spec**: `specs/149-gamepack-takeover-user-objects/spec.md`
**Input**: Feature specification from `/specs/149-gamepack-takeover-user-objects/spec.md`

## Summary

A gamepack install can take over user objects (sequences, commands, images) that have the same type and name, when the client sends the option `takeOver` (default off). The object keeps its id and name, so links by name stay valid. The object gets the gamepack content and the gamepack owns it.

Technical approach (see `research.md`):

- The validator marks a user-owned same-name object as a take-over when the option is on. A conflict with another gamepack stays a conflict. The status codes stay as today (422 for an install, 200 with `valid: false` for a dry run).
- Before the first object write, the applier stores one backup file for all replaced objects. The file lives in `<dataRoot>\gamepacks\takeover-backups\`. The install stays atomic: a failed step restores the undo snapshots and removes the backup.
- A new route `POST /api/gamepacks/takeover-backups/{backupId}/restore` puts the old content back. It has exactly three checks: `object_missing`, `object_changed`, `owner_changed`.
- The dry run lists each object to take over with the mark `identical` or `different`.
- The install result, the managed update report, and the catalog install result carry `takenOver` and `takeOverBackupId`. The routes install, managed update, catalog install, and catalog update accept the option. The managed rollback does not.
- A managed update that fails after the install calls an internal forced restore (step `takeover_restore`) before the gamepack rollback. Force skips `owner_changed` only. It runs under the same gate as the managed update.

## Technical Context

**Language/Version**: C# 12, .NET 8 (existing solution)
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, Swashbuckle (existing). No new package.
**Storage**: Files in the data root: `gamepacks\registry.json` (no format change) and the new `gamepacks\takeover-backups\<id>.json`.
**Testing**: xUnit. Unit, contract, and integration projects under `C:\src\GameBot\tests\`.
**Target Platform**: Windows service (GameBot.Service).
**Project Type**: Web service with domain library (backend only).
**Performance Goals**: A take-over install of the same gamepack takes at most 2 times the time of a normal install on the same machine. The test measures both runs on the same machine in one test and compares them. It has no fixed number of seconds (SC-001, Principle IV).
**Constraints**: Atomic install and restore. No queue stop or start by the option. One gate for install, rollback, restore, and managed update. The force restore uses the gate of the managed update that calls it.
**Scale/Scope**: 13 sequences and 131 commands (144 objects) in one call. One backup file for all of them.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Constitution Principle VI).

*Phone check (Principle III)*: The feature has no web UI change. Phone support level: not applicable.

| Principle | Result |
|-----------|--------|
| I. Code quality | Pass. Small new types in `Gamepacks\TakeOver\`. The public members have comments. The backup id is checked against a strict regex (no path traversal). |
| II. Testing | Pass. Unit, contract, and integration tests for each story, the fault-injection tests, the upgrade test, the large case, and the restore checks. The tests with the option off stay unchanged (SC-002). |
| III. UX consistency | Pass. The option follows the style of `dryRun` on each route. Refusals use reason codes and messages with a fix hint. OpenAPI describes all new fields (FR-015). |
| IV. Performance | Pass. Goal: at most 2 times a normal install. A performance test compares the two times (no fixed seconds). |
| V. Living documentation | Pass. The PR updates `docs\architecture.md` (API surface, persistence layout), `docs\gamepacks.md`, and `specs\STATUS.md`. The spec carries a `Status` line. |
| VI. STE | Pass. All new text uses STE. |

Re-check after design: no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/149-gamepack-takeover-user-objects/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── takeover-api.md  # Phase 1 output
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Gamepacks/
├── GamepackInstallOptions.cs          # new: DryRun, TakeOver, SignatureText
├── GamepackContentComparer.cs         # new: canonical content, identical mark, post hash
├── GamepackPlan.cs                    # change: IsTakeOver, TakenOver, TakeOvers
├── GamepackValidator.cs               # change: takeOver flag in Decide
├── GamepackApplier.cs                 # change: store parameter, backup write, RestoreTakeOverAsync
├── GamepackService.cs                 # change: options overload, RestoreTakeOverAsync under the gate
├── TakeOver/
│   ├── ITakeOverBackupStore.cs        # new
│   ├── TakeOverBackupStore.cs         # new: file store, id regex, atomic write
│   ├── TakeOverBackup.cs              # new: backup and entry types
│   ├── TakeOverEntry.cs               # new: dry-run entry
│   └── TakeOverRestoreOutcome.cs      # new: status, reasons, mode (Strict, Force)
├── Catalog/
│   └── GamepackCatalogService.cs      # change: takeOver on install and update; result fields
└── Managed/
    ├── ManagedUpdateRequest.cs        # change: TakeOver
    ├── ManagedUpdateReport.cs         # change: TakenOver, TakeOverBackupId, TakeOvers
    └── GamepackUpdateOrchestrator.cs  # change: pass option, takeover_restore step

src/GameBot.Service/
├── Endpoints/
│   ├── GamepacksEndpoints.cs          # change: takeOver query, restore route, response fields
│   ├── GamepackManagedEndpoints.cs    # change: takeOver query
│   ├── GamepackCatalogEndpoints.cs    # change: takeOver body field, result fields
│   ├── GamepackServiceCollectionExtensions.cs  # change: register the store
│   └── Dto/ManagedUpdateQuery.cs      # change: takeOver
└── Swagger/                           # change: GamepackSchemaFilter, ManagedUpdateSchemaFilter

tests/
├── unit/Gamepacks/                    # validator, applier, comparer, store, restore (strict and force), service, orchestrator
├── contract/Gamepacks/                # install, dry run, restore, managed, catalog install, catalog update, OpenAPI
└── integration/Gamepacks/             # links, upgrade, restore, large case (144), performance (2x compare)

docs/architecture.md, docs/gamepacks.md, specs/STATUS.md   # living documentation
```

**Structure Decision**: Backend only. The new types sit in a new folder `Gamepacks\TakeOver\` next to the existing gamepack code. The take-over backup store is separate from the feature 148 `ManagedBackupStore`, because the two have different content and retention.

## Complexity Tracking

No violation. The table is empty.

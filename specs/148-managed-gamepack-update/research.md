# Research: Managed gamepack update

All items below are decided. No "NEEDS CLARIFICATION" item stays open.

## R1. Where the flow lives

- **Decision**: A new domain class `GamepackUpdateOrchestrator` in `src/GameBot.Domain/Gamepacks/Managed/`. It uses a small port `IManagedQueueControl` for queue stop, start, and state. The Service project implements the port with `IQueueExecutionService`.
- **Rationale**: `IQueueExecutionService` is `internal` to `GameBot.Service`. The Domain project cannot see it. The port keeps the flow testable with a fake and keeps `Program.cs` and the endpoint handlers thin (taint analyzers).
- **Alternatives**: Put the whole flow in the endpoint class (rejected: large method, hard to test). Make the interface public (rejected: it widens an internal API for one use).

## R2. The gate and the lock for one managed call

- **Decision**: The orchestrator does NOT hold the `GamepackService` gate for the whole flow. It has its own lock for the whole instance. The lock is one `SemaphoreSlim(1,1)` that the code enters with `Wait(0)`, or an `Interlocked` flag.
- The lock rejects a second managed call for ANY gamepack (FR-015). The reason is `update_in_progress` and the status is 409. Two calls never stop and restart the same queue.
- A dry run takes no lock. If a managed call runs, a dry run lists `update_in_progress` as a problem in its report.
- The calls to `InstallAsync`, `RollbackAsync`, and the importer take the gate by themselves, one at a time.
- **Rationale**: The gate is a `SemaphoreSlim(1,1)` that is not reentrant. `InstallAsync`, `RollbackAsync`, and `QueueSettingsImporter.ImportAsync` (through `RunExclusiveAsync`) each take it. A held gate causes a deadlock.
- A manual install between two steps is still possible. The risk is small. The manual install changes the same gamepack, and the verify step (R7) detects a version change.
- **Alternatives**: A reentrant gate (rejected: it changes feature 143 rules, FR-017 of feature 145). Add a "gate already held" overload for each call (rejected: three API changes).

## R2a. Same version and dry-run rules (clarifications C1, I1, I2, U3)

- **Decision**: The flow finds "same version" with an exact match of the version text of the file and the installed version text (FR-018).
- For the same version, the flow stops no queue, stores no backup, and changes no template. It returns `done` with an empty `stoppedQueues` and no `backupId`.
- A missing template name gives `template_not_found` (400) for a normal call and for a dry run. This reason wins over a signature problem. The report keeps all problems.
- A dry run collects all problems in one report. The problems are: source (file or catalog entry), signature, scope (missing template), and conflict (`update_in_progress` and the gamepack conflicts of feature 143).
- A dry run gives 200 for file, signature, and validation problems. It gives a client error only for a malformed request or a missing template. A normal call with a signature problem gives 422.
- **Rationale**: One rule for the scope check, and the dry run gives the full list of problems (FR-013).
- **Alternatives**: Stop at the first dry-run problem (rejected: FR-013).

## R3. How the name of the gamepack and the new sequences are found

- **Decision**: Read the sequence names from the gamepack file with `GamepackFileSerializer.Parse` (the same parser as the install). The installed set is the `Objects` of type `sequence` in the registry record. A "new sequence" is a name in the new set and not in the old set (case-insensitive). For a first install, the old set is empty and no template references the gamepack, so no booking happens (clarification 3).
- **Rationale**: One source of truth. A dry run needs the new set before any write, and the parser gives it.
- **Alternatives**: Diff the sequence repository before and after (rejected: a dry run cannot do it).

## R4. Affected queues and templates

- **Decision**: A template is affected when one entry has a `SequenceId` that is an id of a sequence in the installed `Objects` list. A queue is affected when its `LinkedTemplateId` is an affected template. The optional template list (names, case-insensitive) limits both. A name that matches no template is rejected before any change (`template_not_found`). A listed template that is not affected is listed as `notAffected`.
- **Rationale**: Matches the spec assumption "a queue references a sequence when an entry of its template refers to it". The run-time entries of a queue come from its template at start (`QueueExecutionService.StartAsync` calls `SetEntries` from the template), so a restart picks up the new entries.
- **Alternatives**: Check the run-time entries of a queue (rejected: they are not the persisted truth).

## R5. Stop with a time limit

- **Decision**: For each running affected queue, call `StopAsync` and wait with `Task.WaitAsync(timeout)`. The timeout is 60 s (`ManagedUpdateOptions.StopTimeout`, a fixed value; a test can set a smaller value). A queue that exceeds it fails the step `stop_timeout`. The flow then restarts the queues that it stopped and ends with no change to the gamepack or the templates.
- **Rationale**: `StopAsync` has no time limit today. It can wait for a long step. A fixed limit is the clarified rule (Session 2026-10-16, Q1).
- **Alternatives**: A request option for the limit (rejected: the spec says one fixed rule).

## R6. Backup

- **Decision**: Call `QueueSettingsExporter.ExportAsync`. Then reduce the document to the affected queue templates. Keep only the `QueueTemplates` of the affected set. Drop the `Queues` that do not link to them.
- `SettingsDocument` members are init-only, so the store builds a new document.
- Write the document (`SettingsDocumentJson.Serialize`) to `<dataRoot>\gamepacks\backups\<id>.json`. Use write-to-temp and atomic replace.
- The id is `<UTC yyyyMMddTHHmmssfff>-<gamepack name>`. The gamepack name is validated and has no path character.
- If the update affects no queue template, write no backup. The report has no `backupId`.
- The store keeps the 20 newest files. It deletes older files after a successful write. The class is `ManagedBackupStore`. It has an interface for tests.
- **Rationale**: The export is the building block that feature 145 gives (FR-005). It takes no lock. Gamepack-owned objects are name references only, so the backup is small.
- **Alternatives**: Keep the backup in memory (rejected: the client needs the id to restore by hand after a crash). No pruning (rejected: unbounded disk use).

## R7. Restore on failure

- **Decision**: Do these restore steps in this order. Record each step in the report.
  1. If the install step wrote a different version, put back the installed state that the flow recorded before the call. For an update, call `RollbackAsync`. Then write back the registry record `Previous` and the previous file that the flow read before the install. For a managed rollback, do not call `RollbackAsync` a second time. Use `RestorePreviousMetadataAsync` with the recorded snapshot.
  2. Import the backup document with `QueueSettingsImporter.ImportAsync`. The import is idempotent by name. It restores the template entries. Skip this step if no backup exists.
  3. Start each queue that the flow stopped.
  4. First install: a failed first install fails only before or during the install step. The restore returns to "no gamepack installed". If the install step wrote files, the flow removes the files that the install step created. The applier restore already does this for a normal failure. The flow checks the registry and the file list and removes what is left. The report states the restore. A test covers it.
- If a restore step fails, the flow goes on with the next step. It lists the object in `failedObjects`. The report has `restored: false` when one step failed.
- **Rationale**: The applier (feature 143) restores a failed install by itself (status `Failed` or `PartialRestore`). Step 1 applies only when the install worked and a later step failed. A first install has no later step that can fail. A same-version call stops and books nothing, so it needs no step 1.
- **Alternatives**: Restore templates from in-memory copies (rejected: the clarified rule says import the backup). A new "uninstall" call (rejected: no uninstall exists, FR-017 of 143).

## R8. Booking of disabled entries

- **Decision**: For each affected template, append one `QueueTemplateEntry` per new sequence with `Enabled = false`, `ScheduleType` at its default (`OncePerRun`), no timer fields, and no parameter values. Skip a sequence that the template already references. Write with `IQueueTemplateRepository.UpdateAsync`. Existing entries are not touched. An entry whose sequence id is not in the new object list is reported in `removedSequenceEntries` and kept.
- **Rationale**: FR-007, FR-008, FR-012. The default schedule type is what the class gives, and the disabled flag keeps the entry from running (see the `Enabled` comment in `QueueTemplateEntry`).
- **Alternatives**: Pick a schedule from the gamepack (rejected: the platform does not guess a schedule, clarification 2).

## R9. Catalog source

- **Decision**: Add `GamepackCatalogService.FetchAsync(name, version, ct)` that returns the file text and the optional signature text, with the same checks as `InstallAsync` (entry listed, size limits, SHA-256). `InstallAsync` calls it. The orchestrator takes the text and the signature as input and does not know the catalog.
- **Rationale**: No duplicate of the 147 download rules, and no change to the catalog protocol (FR-017). A `CatalogException` maps to the same HTTP codes as the 147 routes, before any queue stops.
- **Alternatives**: Let the orchestrator call `InstallAsync` of the catalog service (rejected: it would install before the stop and the backup).

## R10. Signature and check before the stop

- **Decision**: Before the stop, run `GamepackService.InstallAsync(text, dryRun: true, signature)`. It gives the signature result and all validation problems and changes nothing. If it is not valid, the call ends with the problems and no queue stops (US1 scenario 6, US4 scenario 2). The real install repeats the check inside the gate.
- **Rationale**: Reuse. The dry run of 143 and 146 is the plan. Its counts give created, updated, and removed.
- **Alternatives**: A new validator path (rejected: duplicate of 143).

## R11. API shape

- **Decision**:
  - `POST /api/gamepacks/update` with the same body rules as `POST /api/gamepacks/install` (JSON file or multipart `gamepack` and `signature`). Query: `dryRun` (bool) and `templates` (repeatable, template names).
  - `POST /api/gamepacks/catalog/update` with JSON `{ name, version, dryRun, templates }`.
  - `POST /api/gamepacks/{name}/rollback?managed=true` with the optional `dryRun` and `templates` query values. Without `managed`, nothing changes (FR-014).
  - Status codes:
    - 200 for `done` and `planned`.
    - 400 for a malformed request or a bad template name. A bad file gives 400 for a normal call.
    - 422 for check problems and for a rejected signature (as in 143).
    - 404 or 409 for catalog failures (as in 147).
    - 409 `update_in_progress`.
    - 500 with the report when `result` is `failed`.
  - A dry run with file, signature, or validation problems gives 200 (as in 143). An error body has one field `reason`. See the contract for the full table.
- **Rationale**: Mirrors routes 143 and 147, so a client reuses its code. The existing token rule covers the routes (Assumptions). The body of the install route is the gamepack file, so options go in the query.
- **Alternatives**: One route with a JSON envelope that holds the file (rejected: it breaks parity with install and doubles the escape work for a large file).

## R12. Report and failure reasons

- **Decision**: One report type with a fixed list of reason names (see `contracts/managed-update-api.md`). The orchestrator catches each known failure and puts it in the report. An unknown exception becomes the reason `unexpected` and still triggers the restore. The route never gives an unhandled error for a known failure (FR-016).
- **Rationale**: Clear reasons, and the restore runs for every failure after the start.

## R13. Logging and performance

- **Decision**: One application log message for the start and one for the end of each managed call (gamepack name, version, mode, result, restored). No file text and no token. Goal: the flow adds under 2 s of its own time to the stop, install, and start time for 20 queues and 20 templates (tested with fakes). No new hot path.
- **Rationale**: Constitution Principle IV needs a stated goal.

## R14. Web UI

- **Decision**: No UI change (Assumptions). Phone check: not applicable.

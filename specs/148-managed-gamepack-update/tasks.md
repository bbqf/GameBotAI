# Tasks: Managed gamepack update

**Input**: Design documents in `C:\src\GameBot\specs\148-managed-gamepack-update\` (plan.md, spec.md, research.md, data-model.md, contracts/managed-update-api.md, quickstart.md)
**Prerequisites**: plan.md, spec.md
**Tests**: Included. The plan requires unit, contract, and integration tests for each FR, SC, and clarification.

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- **[P]**: The task can run in parallel (different files, no dependency on an incomplete task).
- **[Story]**: US1 to US5 (maps to the user stories in spec.md).
- All paths are relative to `C:\src\GameBot`.

---

## Phase 1: Setup

**Purpose**: Create the folders and the shared types.

- [x] T001 Create the folder `src\GameBot.Domain\Gamepacks\Managed\` and the test folders `tests\unit\Gamepacks\Managed\`, `tests\contract\Gamepacks\`, `tests\integration\Gamepacks\` if they do not exist.
- [x] T002 [P] Create `ManagedUpdateOptions` (StopTimeout, default 60 s) in `src\GameBot.Domain\Gamepacks\Managed\ManagedUpdateOptions.cs`.
- [x] T003 [P] Create `ManagedUpdateRequest` (Mode, FileText, SignatureText, CatalogName, CatalogVersion, GamepackName, DryRun, Templates) in `src\GameBot.Domain\Gamepacks\Managed\ManagedUpdateRequest.cs`, per data-model.md. The `DryRun` flag and the `Templates` list exist from the start.
- [x] T004 [P] Create `ManagedUpdateReport`, `QueueRef`, `EntryRef`, `RestoreReport`, `RestoreStep`, and the reason name constants in `src\GameBot.Domain\Gamepacks\Managed\ManagedUpdateReport.cs`, per data-model.md.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Ports, stores, scope logic, route parameters, and changes to existing classes that all stories use.

- [x] T005 [P] Create the port `IManagedQueueControl` (list queues with state and template, stop with limit, start) in `src\GameBot.Domain\Gamepacks\Managed\IManagedQueueControl.cs`.
- [x] T006 [P] Create the port `IManagedBackupStore` (write, read, id check) in `src\GameBot.Domain\Gamepacks\Managed\IManagedBackupStore.cs`.
- [x] T007 Implement `ManagedBackupStore` in `src\GameBot.Domain\Gamepacks\Managed\ManagedBackupStore.cs`: path `<dataRoot>\gamepacks\backups\<backupId>.json`, id format `<yyyyMMddTHHmmssfff>-<gamepack key>`, id check before any read, write-to-temp with atomic replace, keep the newest 20 files.
- [x] T008 [P] Implement `ManagedUpdateScope` in `src\GameBot.Domain\Gamepacks\Managed\ManagedUpdateScope.cs`: affected templates and queues, new sequences, removed sequences, `notAffectedTemplates`, and the missing-template check (`template_not_found`). A first install has no new sequence for booking. Template names match without case.
- [x] T009 [P] Implement `TemplateEntryBooker` in `src\GameBot.Domain\Gamepacks\Managed\TemplateEntryBooker.cs`: append one entry per new sequence (enabled off, no schedule, no parameter values), no duplicate entry, keep all existing entries, report entries whose sequence was removed.
- [x] T010 Add `GetInstalledSnapshot(name)` and `RestorePreviousMetadataAsync(snapshot)` (under the gate) to `src\GameBot.Domain\Gamepacks\GamepackService.cs`. Install, rollback, and list do not change.
- [x] T011 Split `InstallAsync` into `FetchAsync` and the install call in `src\GameBot.Domain\Gamepacks\Catalog\GamepackCatalogService.cs`. Behavior and log messages stay the same.
- [x] T012 Implement the `GamepackUpdateOrchestrator` skeleton in `src\GameBot.Domain\Gamepacks\Managed\GamepackUpdateOrchestrator.cs`: Include these parts. One lock for the whole instance: a second managed call gets `update_in_progress`, the code releases the lock in `finally`, and a dry run takes no lock. The step order of the plan flow. The check step (install dry run). The scope step. The result types. The story phases fill in the stop, backup, install, booking, restore, restart, and dry-run plan steps.
- [x] T013 Implement `ManagedQueueControl` (`IManagedQueueControl` on `IQueueExecutionService`) in `src\GameBot.Service\Services\QueueExecution\ManagedQueueControl.cs`, with the 60 s stop limit.
- [x] T014 Register the new services in `src\GameBot.Service\Endpoints\GamepackServiceCollectionExtensions.cs` (orchestrator as a singleton so the lock is instance-wide, port implementations, options, backup store). Keep `Program.cs` thin.
- [x] T015 [P] Create the response types in `src\GameBot.Service\Endpoints\Dto\ManagedUpdateReportDto.cs` and the report-to-HTTP status mapping (contract status table) as named methods.
- [x] T016 [P] Create the route parameter binding in `src\GameBot.Service\Endpoints\Dto\ManagedUpdateQuery.cs`: the `dryRun` flag, the repeatable `templates` values, and the `managed` flag, plus the catalog body type `{ name, version, dryRun, templates }`. Map them to `ManagedUpdateRequest`. Both update routes and the rollback route use them.
- [x] T017 Add a DI test in `tests\unit\Gamepacks\Managed\ManagedUpdateRegistrationTests.cs` (as for feature 147). Check that the new services resolve from the container. Check that the orchestrator is a singleton. Depends on T014.
- [x] T018 Add unit tests for the route parameter binding in `tests\unit\Gamepacks\Managed\ManagedUpdateQueryTests.cs`. Check these cases: `dryRun` default false, repeatable `templates`, `managed` default false, and a catalog body with a missing `name` or `version`. Depends on T016.

**Checkpoint**: Foundation ready. Story work can start.

---

## Phase 3: User Story 1 - Update a gamepack in one call (Priority: P1) MVP

**Goal**: One call stops the affected queues, stores a backup, installs, and restarts only the queues that the flow stopped.

**Independent Test**: Install v1, make two templates that reference its sequences, start one queue, update to v2. The report shows the stopped and restarted queue, the backup id, and the installed v2.

### Tests for User Story 1

- [x] T019 [P] [US1] Unit tests for scope rules (running vs stopped queue, template not referencing the gamepack, template list limit, `notAffectedTemplates`, first install, missing template name) in `tests\unit\Gamepacks\Managed\ManagedUpdateScopeTests.cs`.
- [x] T020 [P] [US1] Unit tests for `ManagedBackupStore` (write, read, id check, retention of 20) in `tests\unit\Gamepacks\Managed\ManagedBackupStoreTests.cs`.
- [x] T021 [P] [US1] Unit tests for the orchestrator happy path with a fake queue control, in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorTests.cs`. Check the order of the steps. Check that only the recorded queues restart. Check that a queue that the user stopped is not started, and that its template still gets the new disabled entries. Check that no backup is written when the update affects no queue template.
- [x] T022 [P] [US1] Unit test for the equal-version no-op, in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorSameVersionTests.cs`. The version text of the file equals the installed version text (exact match, FR-018). The fake queue control gets no stop call. No backup is written. No template changes. `result` is `done`, `stoppedQueues` is empty, and `backupId` is absent.
- [x] T023 [P] [US1] Unit test for the instance-wide lock (clarification U1, FR-015): hold a managed call for gamepack A at a gate in the fake, start a managed call for gamepack B on the same orchestrator, and check that the second call gets `update_in_progress` and stops no queue. Also check that the lock is released after the first call ends (with success and with failure), and that a dry run takes no lock. File: `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorLockTests.cs`.
- [x] T024 [P] [US1] Report completeness test (SC-006): for a done update, check that the report has every item of FR-011 (stopped queues, restarted queues, backup id, created, updated, and removed counts, new disabled entries, entries with a removed sequence) with the expected values, in `tests\unit\Gamepacks\Managed\ManagedUpdateReportCompletenessTests.cs`.
- [x] T025 [P] [US1] Contract tests for `POST /api/gamepacks/update` and `POST /api/gamepacks/catalog/update` in `tests\contract\Gamepacks\GamepackManagedUpdateContractTests.cs`. Check these cases: 200, 400, 404, 409 `update_in_progress`, 413, the 422 mapping, the API token rule, an equal version (200 with no change), and a JSON report with every FR-011 field. Check that an error body has one `reason` field. Add a compatibility assertion for FR-017: the catalog protocol of feature 147 (the catalog routes and the catalog index format) does not change.
- [x] T026 [P] [US1] Integration tests with real stores in a temp data root and a fake queue control (file source, catalog source with the folder catalog, signature "required" rejection, template list scope, two parallel calls for two different gamepacks with one 409) in `tests\integration\Gamepacks\GamepackManagedUpdateIntegrationTests.cs`.

### Implementation for User Story 1

- [x] T027 [US1] Implement the same-version short path in `GamepackUpdateOrchestrator`: when the version text of the file equals the installed version text (exact match, FR-018), end after the check. Do no stop, no backup, and no change. Return `done` with an empty `stoppedQueues` and no `backupId`.
- [x] T028 [US1] Implement the stop step in `GamepackUpdateOrchestrator`: stop each running affected queue with the 60 s limit, record each stopped queue. On a timeout: restart the stopped queues and end with `stop_timeout`.
- [x] T029 [US1] Implement the backup step in `GamepackUpdateOrchestrator`. Export the affected templates with the feature 145 export. Store the file. Return `backupId`. If the update affects no queue template, write no backup and return no `backupId`. If the step fails, restart the stopped queues and end with `backup_failed`.
- [x] T030 [US1] Implement three steps in `GamepackUpdateOrchestrator`. First, the install step with the signature policy of feature 146. Do the version check before the install. Second, the restart step. Restart only the recorded queues. Put a queue that does not start in `notRestartedQueues`. Start the other queues. Third, the full report assembly with all FR-011 items. A queue in `notRestartedQueues` keeps `result` at `done`. NOTE: US1 alone has no restore for `install_failed`. Do not ship the MVP alone. Ship it with US3.
- [x] T031 [US1] Implement the routes `POST /api/gamepacks/update` and `POST /api/gamepacks/catalog/update` in `src\GameBot.Service\Endpoints\GamepackManagedEndpoints.cs`. The update route takes a JSON or multipart body, `dryRun`, repeatable `templates`, and a 100 MB limit. Do route mapping and handler methods in this one task. Define named handler methods and map them in the same file. Use `FetchAsync` for the catalog source. Map the catalog failures as in feature 147. Log the start and the end of each call. Do not log secrets.

**Checkpoint**: US1 works on its own. Do not ship it alone (see T030).

---

## Phase 4: User Story 2 - New entries stay disabled (Priority: P1)

**Goal**: New sequences get disabled entries. Existing entries do not change.

**Independent Test**: Update to a version with one new sequence. Each affected template has one new entry with the enabled flag off. All old entries are the same.

### Tests for User Story 2

- [x] T032 [P] [US2] Unit tests for `TemplateEntryBooker` (new disabled entry, existing entries unchanged, no duplicate entry, removed-sequence entry kept and listed, first install books nothing) in `tests\unit\Gamepacks\Managed\TemplateEntryBookerTests.cs`.
- [x] T033 [P] [US2] Integration test in `tests\integration\Gamepacks\GamepackManagedEntriesIntegrationTests.cs` (SC-003). After an update, 0 new entries are enabled. 100% of the entries that existed keep their enabled flag, schedule, and parameter values. Add a case: a stopped queue is not started, and its template still gets the new disabled entries.

### Implementation for User Story 2

- [x] T034 [US2] Add the booking step to `GamepackUpdateOrchestrator` (after the install, before the restart): call `TemplateEntryBooker`, save templates through `IQueueTemplateRepository`, fill `newEntries` and `removedSequenceEntries` in the report.

**Checkpoint**: US1 and US2 work together.

---

## Phase 5: User Story 3 - Safe failure and restore (Priority: P1)

**Goal**: A failed step restores the previous version and queue state, and restarts the stopped queues.

**Independent Test**: Make the install fail after the stop. The old version stays, templates are the same, stopped queues run again, and the report states the restore.

### Tests for User Story 3

- [x] T035 [P] [US3] Unit tests for the restore path with fakes (install fails, booking fails, restore step fails and lists `failedObjects`, stop timeout restarts stopped queues with no gamepack or template step) in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorRestoreTests.cs`.
- [x] T036 [P] [US3] Test for a failed first install (SC-004). No gamepack is installed before the call. The install step fails after it wrote files. Check that the registry has no gamepack. Check that the created files are removed. Check that `restore.happened` is true. Write a unit test with fakes in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorFirstInstallFailureTests.cs`. Write an integration test with an injected write fault in `tests\integration\Gamepacks\GamepackManagedFirstInstallRestoreIntegrationTests.cs`.
- [x] T037 [P] [US3] Integration tests with an injected write fault through `IGamepackWriteGate` (version and templates equal the state before the call, SC-004), a stop timeout, and a failed restart of one queue that does not stop the others, in `tests\integration\Gamepacks\GamepackManagedRestoreIntegrationTests.cs`.
- [x] T038 [P] [US3] Contract test for the 500 `failed` report with `restore` and for `reason` `unexpected` in `tests\contract\Gamepacks\GamepackManagedFailureContractTests.cs`.

### Implementation for User Story 3

- [x] T039 [US3] Implement the restore step in `GamepackUpdateOrchestrator` (research R7). For an update, roll back the gamepack to the previous version and call `RestorePreviousMetadataAsync`. For a managed rollback, do not call the rollback a second time. Put back the installed state that the flow recorded before the call. Import the backup (feature 145) for the affected templates. Skip the import if no backup exists. Restart the stopped queues. For a first install, return to "no gamepack installed" and remove the files that the install step created. Record each step and each failed object in `RestoreReport`.
- [x] T040 [US3] Catch known failures in the orchestrator and give each a reason name (`install_failed`, `booking_failed`, `unexpected`). Make sure the lock is released in `finally` and no known failure gives an unhandled error (FR-016).

**Checkpoint**: A failed update leaves a safe state.

---

## Phase 6: User Story 4 - Dry run (Priority: P2)

**Goal**: A dry run returns the plan and all problems and changes nothing. The `dryRun` flag and the route parameters exist from Phase 2. This phase adds only the dry-run plan logic.

**Independent Test**: Send an update with `dryRun`. The report lists planned stops, objects, and new entries. Version, queues, templates, and backups did not change.

### Tests for User Story 4

- [x] T041 [P] [US4] Unit tests that a dry run calls no stop, no backup write, no install, no template save (fake spies), and returns `planned` or `rejected` with all problems, in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorDryRunTests.cs`.
- [x] T042 [P] [US4] Test for a dry run with a missing template in the list. The status is the client error with the reason `template_not_found`, as for a normal call. This reason wins over a signature problem. The report also lists all other problems. Write a unit test in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorDryRunScopeTests.cs`. Write a contract test in `tests\contract\Gamepacks\GamepackManagedDryRunContractTests.cs`.
- [x] T043 [P] [US4] Test that a dry run reports all problems together in one report. The problems are: a source problem, a signature problem, a missing template, and `update_in_progress`. A dry run with a file, signature, or validation problem gives 200. A normal call with the same signature problem gives 422. A malformed request gives a client error for a dry run. Write a unit test in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorDryRunProblemsTests.cs`. Write a contract test in `tests\contract\Gamepacks\GamepackManagedDryRunContractTests.cs`.
- [x] T044 [P] [US4] Integration test that state is equal before and after a dry run, and no backup file exists (SC-005), in `tests\integration\Gamepacks\GamepackManagedDryRunIntegrationTests.cs`.

### Implementation for User Story 4

- [x] T045 [US4] Implement the dry-run branch in `GamepackUpdateOrchestrator`. After the check and scope steps, return `planned` with the planned stops, the counts, and the new entries. Return no backup id. Collect ALL problems (source, signature, scope, conflict) and the signature block into one report. Return `rejected` if a problem exists. A missing template gives `template_not_found`, and the report keeps the other problems. If a managed call runs, list the problem `update_in_progress`. Take no lock.

**Checkpoint**: Dry run works for update routes.

---

## Phase 7: User Story 5 - Managed rollback (Priority: P2)

**Goal**: `POST /api/gamepacks/{name}/rollback?managed=true` uses the same flow with the rollback as the install step.

**Independent Test**: After an update, send a managed rollback. Queues stop, backup is stored, previous version returns, queues run again, and the report has the same items.

### Tests for User Story 5

- [x] T046 [P] [US5] Unit tests for the rollback mode (check parses the previous file, 404 `gamepack_not_found`, 409 `no_previous_version`, `rollback_refused`, booking for sequences that the rolled-back version adds, a failed rollback restores the state, a failure after a good rollback puts back the recorded state with no second rollback call) in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorRollbackTests.cs`.
- [x] T047 [P] [US5] Contract tests that the rollback route without `managed` returns the old `GamepackRollbackResponse`, and that `managed=true` returns a `ManagedUpdateReport` (including `dryRun`) in `tests\contract\Gamepacks\GamepackManagedRollbackContractTests.cs`.
- [x] T048 [P] [US5] Integration test for managed rollback after an update, and a failed rollback that restores the state, in `tests\integration\Gamepacks\GamepackManagedRollbackIntegrationTests.cs`.

### Implementation for User Story 5

- [x] T049 [US5] Add the `Rollback` mode to `GamepackUpdateOrchestrator`: the check validates the previous file, the install step calls the rollback, errors map to `gamepack_not_found`, `no_previous_version`, and `rollback_refused`.
- [x] T050 [US5] Change the rollback handler in `src\GameBot.Service\Endpoints\GamepacksEndpoints.cs` to read `managed`, `dryRun`, and `templates` with the Phase 2 binding. With `managed=true` call the orchestrator. Without it, keep the existing behavior.

**Checkpoint**: All five stories work.

---

## Phase 8: Polish and cross-cutting concerns

- [x] T051 [P] Create `ManagedUpdateSchemaFilter` with STE descriptions for the new schemas and the three routes in `src\GameBot.Service\Swagger\ManagedUpdateSchemaFilter.cs`, and register it. Add a contract test of the OpenAPI document in `tests\contract\Gamepacks\GamepackManagedOpenApiContractTests.cs`.
- [x] T052 [P] Add a timing test with fakes (20 queues, 20 templates: flow overhead and dry run under 2 s) in `tests\unit\Gamepacks\Managed\ManagedUpdatePerformanceTests.cs`.
- [x] T053 [P] Update `docs\gamepacks.md` (managed update, managed rollback, report, reasons) in STE.
- [x] T054 [P] Update `docs\architecture.md` (API surface, "Last reviewed"), `CHANGELOG.md`, and `specs\STATUS.md`. Set the Status in `specs\148-managed-gamepack-update\spec.md`.
- [x] T055 Run the steps in `specs\148-managed-gamepack-update\quickstart.md` against a local instance and fix any difference. (Done as automated checks: each quickstart step has a contract or integration test. A manual walk on a live instance is still pending, as for feature 147.)
- [x] T056 Run the full build and the full test suites (unit, contract, integration). Fix all failures. Check that new classes have 80% line and 70% branch coverage.
- [x] T057 Bump `patch` in `installer\versioning\version.override.json` per the constitution rule (`git fetch origin master`, compare with `origin/master`, set `updatedBy` and `updatedAtUtc`) before the push.

---

## Dependencies and execution order

- Phase 1, then Phase 2 (blocks all stories). T016 (route parameters with `dryRun`) is in Phase 2, so US1 route tests and US5 route work need no later task.
- Inside Phase 2: T017 depends on T014. T018 depends on T016.
- US1 (Phase 3) first. It builds the same-version path, the stop, backup, install, and restart steps, and the update routes.
- US2 depends on US1 (booking goes between install and restart).
- US3 depends on US1 and US2 (the restore undoes the stop, backup, install, and booking steps).
- US4 depends on the check and scope steps of Phase 2 and on the route shape of US1. Only the dry-run plan logic (T045) is new.
- US5 depends on US1 to US3 (same flow, rollback mode).
- Phase 8 depends on all stories. T056 and T057 are last.
- Tasks in `GamepackUpdateOrchestrator.cs` (T012, T027 to T030, T034, T039, T040, T045, T049) edit one file. Do them in order, not in parallel.

## Parallel examples

- Phase 1: T002, T003, T004 together.
- Phase 2: T005, T006, T008, T009, T015, T016 together after T001 to T004. Run T017 after T014. Run T018 after T016.
- US1 tests: T019 to T026 together.
- US3 tests: T035 to T038 together.
- US4 tests: T041 to T044 together.
- Phase 8: T051 to T054 together.

## Implementation strategy

- **MVP check point**: Phase 1, Phase 2, and US1. Then stop and check. Do not ship this alone.
- **Safe increment**: add US2 and US3 next. US1 alone has no restore for `install_failed`. A failed update can leave queues stopped. Ship US1 only with US2 and US3.
- **Then**: US4 and US5 in priority order, then Phase 8.

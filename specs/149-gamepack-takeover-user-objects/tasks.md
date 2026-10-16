# Tasks: Take over same-named user objects on gamepack install

**Input**: Design documents in `C:\src\GameBot\specs\149-gamepack-takeover-user-objects\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/takeover-api.md, quickstart.md

**Tests**: Included. The constitution (Principle II) and the plan require unit, contract, and integration tests.

**Not a task**: The version bump of `installer\versioning\version.override.json`. The pipeline does it before the push.

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- **[P]**: The task can run in parallel (different files, no open dependency).
- **[Story]**: User story label (US1 to US4). Only in user story phases.
- All source paths are under `C:\src\GameBot\src\`. All test paths are under `C:\src\GameBot\tests\`.
- Conflict status: a conflict with another gamepack gives 422 (real install) or 200 with `valid: false` (dry run). Never 409 for an install conflict. The 409 status is only for a refused restore.

---

## Phase 1: Setup

**Purpose**: Create the shared types and folders.

- [ ] T001 Create the folder `src\GameBot.Domain\Gamepacks\TakeOver\` and add the record `TakeOverEntry` (Type, Name, Identical) in `src\GameBot.Domain\Gamepacks\TakeOver\TakeOverEntry.cs`
- [ ] T002 [P] Add the types `TakeOverBackup` and `TakeOverBackupEntry` (fields as in data-model.md) in `src\GameBot.Domain\Gamepacks\TakeOver\TakeOverBackup.cs`
- [ ] T003 [P] Add `TakeOverRestoreOutcome` (Status enum, BackupId, Restored, Reasons), `TakeOverRestoreReason` (Code, Type, Name, Message), and the enum `TakeOverRestoreMode` (Strict, Force) in `src\GameBot.Domain\Gamepacks\TakeOver\TakeOverRestoreOutcome.cs`
- [ ] T004 [P] Add the record `GamepackInstallOptions` (DryRun, TakeOver, SignatureText) in `src\GameBot.Domain\Gamepacks\GamepackInstallOptions.cs`

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Core pieces that every story needs: the comparer, the backup store, the plan flags, the gate operations, the applier constructor change, and the validator decision.

- [ ] T005 [P] Write unit tests for the comparer (sequence, command, image, "identical" and "different", volatile members ignored, image file name ignored) in `tests\unit\Gamepacks\GamepackContentComparerTests.cs`
- [ ] T006 [P] Write unit tests for the backup store (id regex, reject path characters, write, read, delete, missing file, temp-file move, no retention) in `tests\unit\Gamepacks\TakeOverBackupStoreTests.cs`
- [ ] T007 [P] Implement `GamepackContentComparer` (canonical text with `CanonicalJson`, `Identical`, and a content hash function for the post-install hash) in `src\GameBot.Domain\Gamepacks\GamepackContentComparer.cs`
- [ ] T008 [P] Add the interface `ITakeOverBackupStore` (IsValidId, Write, Read, Delete) in `src\GameBot.Domain\Gamepacks\TakeOver\ITakeOverBackupStore.cs`
- [ ] T009 Implement `TakeOverBackupStore` (folder `gamepacks\takeover-backups`, id `to-yyyyMMddTHHmmssfff-<8 hex>`, strict regex check before every file access, temp file then move) in `src\GameBot.Domain\Gamepacks\TakeOver\TakeOverBackupStore.cs`
- [ ] T010 Register `ITakeOverBackupStore` in `src\GameBot.Service\Endpoints\GamepackServiceCollectionExtensions.cs`
- [ ] T011 [P] Add `IsTakeOver` to `PlannedObject`, add `TakenOver` and `TakeOvers` to `GamepackPlan`, and make `Updated` count only writes with `ExistingId` set and `IsTakeOver` false in `src\GameBot.Domain\Gamepacks\GamepackPlan.cs`
- [ ] T012 Add `TakeOverBackup` and `TakeOverRestore` to `GamepackWriteOperations` (fault-injection gate), and make the `GamepackApplier` constructor take the optional parameter `ITakeOverBackupStore` (default null). Without a store and with a take-over in the plan, the apply returns `Failed`. Update every existing place that builds the applier (production DI in `src\GameBot.Service\Endpoints\GamepackServiceCollectionExtensions.cs` and the test helpers under `tests\`) so the tests build the applier in one way. Edit `src\GameBot.Domain\Gamepacks\GamepackApplier.cs`
- [ ] T013 Write unit tests for the validator: user owner with takeOver on is a take-over, other gamepack owner stays a conflict with takeOver on and off, same gamepack owner is an update and not a take-over (the count excludes it), takeOver off keeps the conflict, duplicate user names take the first, objects not in the pack do not change, same name with different types matched by type, in `tests\unit\Gamepacks\GamepackValidatorTakeOverTests.cs`
- [ ] T014 Add the `takeOver` parameter (default false) to `GamepackValidator.ValidateAsync`; in `Decide` mark a user-owned match as a take-over that keeps the user object id; fill `GamepackPlan.TakeOvers` with the comparer result in `src\GameBot.Domain\Gamepacks\GamepackValidator.cs`

**Checkpoint**: Foundation ready. User stories can start.

---

## Phase 3: User Story 1 - Install over hand-made objects (Priority: P1) MVP

**Goal**: An install with `takeOver` on makes the pack own same-named user objects. Names and links stay valid.

**Independent Test**: Make a sequence and a command by hand and a queue template that references the sequence. Install a pack with the same type and name, `takeOver` on. The install works, the pack owns the objects, and the queue template link is valid.

### Tests for User Story 1

- [ ] T015 [P] [US1] Contract tests for `POST /api/gamepacks/install` (conflict status is 422, never 409): `takeOver` off gives the user conflict as today (422); on gives 200 with the `takenOver` count; conflict with another pack gives 422 (real install) with `takeOver` on and off, never 409; missing value equals off; user object not in the pack unchanged, in `tests\contract\Gamepacks\GamepackTakeOverInstallContractTests.cs`
- [ ] T016 [P] [US1] Integration test: queue template reference by name stays valid after a take-over of a sequence, and ownership is the pack, in `tests\integration\Gamepacks\GamepackTakeOverLinkTests.cs`
- [ ] T017 [P] [US1] Unit tests for the applier: take-over write uses the update call with the user object id, registry gets owner entries and `Objects` refs, outcome `TakenOver` equals replaced count (SC-007), in `tests\unit\Gamepacks\GamepackApplierTakeOverTests.cs`
- [ ] T018 [P] [US1] Integration test for the upgrade take-over: version 1 of a pack owns object A; version 2 adds object B and a user object B exists; the upgrade takes B over, `TakenOver` is 1, A counts as updated and not taken over (FR-003, FR-012), in `tests\integration\Gamepacks\GamepackTakeOverUpgradeTests.cs`
- [ ] T019 [P] [US1] Integration test for the large case: install a pack over 13 user sequences and 131 user commands with `takeOver` on; check `TakenOver` is 144, every link by name (queue templates and sequence-to-sequence/command refs) is valid, and no queue changes its run state (SC-001, SC-008), in `tests\integration\Gamepacks\GamepackTakeOverLargeCaseTests.cs`
- [ ] T020 [P] [US1] Contract tests for `POST /api/gamepacks/catalog/install` with the body field `takeOver` (on, off, missing; conflict with another pack gives 422 on and off, never 409). The result has `takenOver`, and `takeOverBackupId` when an object was replaced; the backup-id checks pass after T035. Then restore that backup with the restore route and check the old content returns, in `tests\contract\Gamepacks\GamepackCatalogTakeOverContractTests.cs`

### Implementation for User Story 1

- [ ] T021 [US1] Add `TakenOver` to `GamepackApplyOutcome`; let `GamepackApplier` write take-over objects through the update path and set the registry owners in `src\GameBot.Domain\Gamepacks\GamepackApplier.cs`
- [ ] T022 [US1] Add the `InstallAsync(json, GamepackInstallOptions, ct)` overload; keep the two old overloads and make them call the new one; pass `TakeOver` to the validator; add `TakenOver` to `GamepackInstallOutcome`, under the existing gate, in `src\GameBot.Domain\Gamepacks\GamepackService.cs`
- [ ] T023 [US1] Read the `takeOver` query value on the install route and call the new overload in `src\GameBot.Service\Endpoints\GamepacksEndpoints.cs`
- [ ] T024 [US1] Add `takenOver` (always present) to the install response DTO in `src\GameBot.Service\Endpoints\Dto\` (the install response file)
- [ ] T025 [US1] Add the `takeOver` body field to the catalog install request and pass it to the install in `src\GameBot.Domain\Gamepacks\Catalog\GamepackCatalogService.cs` and `src\GameBot.Service\Endpoints\GamepackCatalogEndpoints.cs`

**Checkpoint**: US1 works alone. The install takes over objects (no backup yet).

---

## Phase 4: User Story 2 - Backup and restore of replaced objects (Priority: P1)

**Goal**: One backup for all replaced objects, an atomic install, and a restore by id.

**Independent Test**: Install with `takeOver` on over three user objects. Read the backup id. Restore. The objects have the old content and the owner is the user. A forced failure leaves no change.

### Tests for User Story 2

- [ ] T026 [P] [US2] Unit tests for the applier backup: backup written before the first object write, one id for all objects, no id when nothing is replaced, backup write failure gives `Failed` with no change, failed apply restores snapshots and deletes the backup, partial restore keeps the file, using the fault-injection gate (SC-004), in `tests\unit\Gamepacks\GamepackApplierTakeOverBackupTests.cs`
- [ ] T027 [P] [US2] Unit tests for `RestoreTakeOverAsync` in strict mode: success, `object_missing`, `object_changed`, `owner_changed`, unknown id, malformed id, atomic on a forced fault; and a test that every reason code in any refusal is one of exactly `object_missing`, `object_changed`, `owner_changed` (no other code exists), in `tests\unit\Gamepacks\GamepackRestoreTakeOverTests.cs`
- [ ] T028 [P] [US2] Unit tests for the second restore and the force mode: a second restore of the same backup gives `owner_changed` and changes nothing; force mode skips `owner_changed` only and still refuses on `object_missing` and `object_changed`; the force restore runs under the gate that the caller already holds (it does not take the gate a second time and does not deadlock) while the public restore takes the gate itself (FR-016), in `tests\unit\Gamepacks\GamepackRestoreTakeOverForceTests.cs`
- [ ] T029 [P] [US2] Contract tests for `POST /api/gamepacks/takeover-backups/{backupId}/restore`: 200, 404 for unknown and malformed id, 409 with reasons, a second restore gives 409 with `owner_changed`, 500 for a failed write; check that the public route never uses force, in `tests\contract\Gamepacks\GamepackTakeOverRestoreContractTests.cs`
- [ ] T030 [P] [US2] Integration test: install, restore, objects have old content and owner user (SC-005); the second restore gives `owner_changed` and changes nothing; a forced failure in the middle of an install leaves 0 changed objects, in `tests\integration\Gamepacks\GamepackTakeOverRestoreTests.cs`

### Implementation for User Story 2

- [ ] T031 [US2] In `GamepackApplier.ApplyAsync`: after the undo snapshots, write one backup (old content, post-install hashes) before the first write; on a failed step restore the snapshots and delete the backup (best effort, logged); set `TakeOverBackupId` only on success and when at least one object was replaced, in `src\GameBot.Domain\Gamepacks\GamepackApplier.cs`
- [ ] T032 [US2] Add `GamepackApplier.RestoreTakeOverAsync(backupId, mode, ct)` (strict checks `object_missing`, `object_changed`, `owner_changed`; `Force` skips `owner_changed` only; snapshots; registry owner and object ref removal; the backup file stays; atomic) in `src\GameBot.Domain\Gamepacks\GamepackApplier.cs`
- [ ] T033 [US2] Add `TakeOverBackupId` to `GamepackInstallOutcome` and add `GamepackService.RestoreTakeOverAsync` (strict only) under the install gate (FR-016) in `src\GameBot.Domain\Gamepacks\GamepackService.cs`
- [ ] T034 [US2] Add the restore route and its response DTO (404, 409 with reasons, 200, 500) in `src\GameBot.Service\Endpoints\GamepacksEndpoints.cs` and `src\GameBot.Service\Endpoints\Dto\`; keep the handler a named method; the route never passes force
- [ ] T035 [US2] Add `takeOverBackupId` (omitted when null) to the install response DTO in `src\GameBot.Service\Endpoints\Dto\`, and to the catalog install result (it returns the same install result with `takenOver` and `takeOverBackupId`) in `src\GameBot.Domain\Gamepacks\Catalog\GamepackCatalogService.cs` and `src\GameBot.Service\Endpoints\GamepackCatalogEndpoints.cs`
- [ ] T036 [US2] Add log events (next free id in the gamepack 14xx range) for take-over, backup written, backup deleted, restore; log gamepack name, count, and backup id only, in `src\GameBot.Domain\Gamepacks\GamepackApplier.cs`

**Checkpoint**: US1 and US2 work. A take-over is atomic and can be undone.

---

## Phase 5: User Story 3 - Preview with dry run (Priority: P2)

**Goal**: A dry run with `takeOver` on lists each object to take over, marked identical or different, and changes nothing.

**Independent Test**: Two user objects, one identical and one different. Dry run with `takeOver` on lists both with the right mark. No object, owner, or backup changes.

### Tests for User Story 3

- [ ] T037 [P] [US3] Contract tests for dry run: list with marks, no change and no backup file (SC-006), dry run with `takeOver` off reports the conflict (200, `valid: false`) and no list, dry run with another-pack conflict gives 200 with `valid: false`, problems, and no list, with the option on and off, in `tests\contract\Gamepacks\GamepackTakeOverDryRunContractTests.cs`
- [ ] T038 [P] [US3] Unit test: dry run count equals the count of the real install and writes no backup file, in `tests\unit\Gamepacks\GamepackServiceTakeOverDryRunTests.cs`

### Implementation for User Story 3

- [ ] T039 [US3] Add `TakeOvers` to `GamepackInstallOutcome` and fill it for a dry run with the option on and no problem; set `TakenOver` to the planned count, in `src\GameBot.Domain\Gamepacks\GamepackService.cs`
- [ ] T040 [US3] Add `takeOvers` (type, name, identical; omitted when null) to the install response DTO in `src\GameBot.Service\Endpoints\Dto\`

**Checkpoint**: US3 works with US1.

---

## Phase 6: User Story 4 - Take-over in the managed update (Priority: P2)

**Goal**: The managed update and the catalog update accept `takeOver`, pass it on, report the count, and restore the user objects when a later step fails. They do not stop or start a queue because of the option. The managed rollback does not accept it.

**Independent Test**: A managed update with `takeOver` on over a conflicting user object works, and the report has the count. A managed dry run lists the objects.

### Tests for User Story 4

- [ ] T041 [P] [US4] Unit tests for the orchestrator: pass-through in the check and install steps, report fields, force restore of the take-over backup before the gamepack rollback when a step after the install fails (step `takeover_restore`, no `TakeOverBackupId` in the failed report), the force restore runs under the managed update gate, no extra queue stop or start (SC-008), the rollback path never passes the option, in `tests\unit\Gamepacks\Managed\GamepackUpdateOrchestratorTakeOverTests.cs`
- [ ] T042 [P] [US4] Contract tests for the managed update route: query `takeOver`, the report fields `takenOver`, `takeOverBackupId`, `takeOvers`, option off unchanged (SC-002), conflict with another pack gives 422 (install) or 200 with `valid: false` (dry run), and the managed rollback route has no `takeOver` option, in `tests\contract\Gamepacks\GamepackManagedTakeOverContractTests.cs`
- [ ] T043 [P] [US4] Contract tests for `POST /api/gamepacks/catalog/update` with the body field `takeOver` (on, off, missing; report fields; dry run list) in `tests\contract\Gamepacks\GamepackCatalogUpdateTakeOverContractTests.cs`

### Implementation for User Story 4

- [ ] T044 [US4] Add `TakeOver` to `ManagedUpdateRequest` in `src\GameBot.Domain\Gamepacks\Managed\ManagedUpdateRequest.cs`
- [ ] T045 [US4] Add `TakenOver`, `TakeOverBackupId`, `TakeOvers` to `ManagedUpdateReport` in `src\GameBot.Domain\Gamepacks\Managed\ManagedUpdateReport.cs`
- [ ] T046 [US4] In `GamepackUpdateOrchestrator`: pass `TakeOver` to the dry-run check and to the install step; fill the report; on failure after the install, call the force restore (before the gamepack rollback or removal) and add the step `takeover_restore`; the rollback never passes the option. Expose the force restore on `GamepackService` for this internal call only; it runs inside the gate that the managed update holds and does not take the gate again, in `src\GameBot.Domain\Gamepacks\Managed\GamepackUpdateOrchestrator.cs` and `src\GameBot.Domain\Gamepacks\GamepackService.cs`
- [ ] T047 [US4] Read `takeOver` from the query in `src\GameBot.Service\Endpoints\GamepackManagedEndpoints.cs` and in `src\GameBot.Service\Endpoints\Dto\ManagedUpdateQuery.cs`; add the new fields to the managed report DTO in `src\GameBot.Service\Endpoints\Dto\`
- [ ] T048 [US4] Add the `takeOver` body field to the catalog update request and pass it to the managed update in `src\GameBot.Domain\Gamepacks\Catalog\GamepackCatalogService.cs` and `src\GameBot.Service\Endpoints\GamepackCatalogEndpoints.cs`

**Checkpoint**: All four stories work.

---

## Phase 7: Polish and cross-cutting

- [ ] T049 [P] Describe the option (query and body), the dry-run list and marks, the count, the backup id, and the restore route (404, 409 reasons, second restore) in the OpenAPI schema filters and examples in `src\GameBot.Service\Swagger\` (FR-015); include the new fields in the report completeness test of feature 148; add an OpenAPI contract test in `tests\contract\Gamepacks\GamepackTakeOverOpenApiTests.cs`
- [ ] T050 [P] Add a performance test: in one test, on the same machine, with fresh data folders and a warm-up run, install the same pack once as a normal install (no conflict, option off) and once with `takeOver` on over 13 sequences and 131 commands (with the backup write); assert that the take-over time is at most 2 times the normal install time. Use no fixed number of seconds (SC-001), in `tests\integration\Gamepacks\GamepackTakeOverPerformanceTests.cs`
- [ ] T051 [P] Add a regression run: all existing gamepack install and managed update tests pass with the option off (SC-002); fix any break in `tests\`
- [ ] T052 [P] Add a new section "Take-over" in `docs\gamepacks.md`
- [ ] T053 [P] Update the API surface, the persistence layout (`gamepacks\takeover-backups`), and the last-reviewed date in `docs\architecture.md`
- [ ] T054 [P] Set the status of feature 149 in `specs\STATUS.md`
- [ ] T055 Run `dotnet build` and `dotnet test` for the solution; fix all failures (constitution gate)
- [ ] T056 Run the steps in `specs\149-gamepack-takeover-user-objects\quickstart.md` and confirm they work (the quickstart must show 422 for a conflict, not 409)

---

## Dependencies and execution order

- Phase 1 has no dependency. Phase 2 needs Phase 1 and blocks all stories.
- The applier constructor change (T012) is in Phase 2, so all story tests build the applier in one way.
- T010 runs before T012. Both tasks edit `GamepackServiceCollectionExtensions.cs`, so they are not parallel.
- US1 (Phase 3) needs Phase 2. It is the MVP.
- US2 (Phase 4) needs Phase 2 and builds on the applier and service edits of US1 (T021, T022).
- US3 (Phase 5) needs Phase 2 and T022 (the service overload).
- US4 (Phase 6) needs US1, US2, and US3 (it passes the option, the backup id, and the list). T048 also needs T025.
- Phase 7 needs all stories.
- Inside a story: tests first (they must fail), then domain, then service, then endpoints.
- Tasks that edit the same file are not parallel: `GamepackApplier.cs` (T012, T021, T031, T032, T036), `GamepackService.cs` (T022, T033, T039, T046), `GamepacksEndpoints.cs` (T023, T034), `GamepackCatalogService.cs` and `GamepackCatalogEndpoints.cs` (T025, T048), the install response DTO (T024, T035, T040).

## Parallel examples

- Phase 2: T005, T006, T007, T008, T011 together; then T009, T012, T013; then T014.
- US1: T015, T016, T017, T018, T019, T020 together (tests).
- US2: T026, T027, T028, T029, T030 together (tests).
- US4: T041, T042, T043 together (tests).
- Phase 7: T049 to T054 together.

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1. This gives the take-over that the requester needs.
- Next: US2 (backup and restore) before any release, because a take-over without an undo is not safe. Treat US1 and US2 as one release.
- Then US3 and US4 as increments, then Phase 7.

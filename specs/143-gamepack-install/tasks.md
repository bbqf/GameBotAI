# Tasks: Gamepack Install

**Input**: Design documents from `/specs/143-gamepack-install/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/gamepack-api.md, quickstart.md

**Tests**: Included. The plan and the constitution (Principle II) require unit, contract, and integration tests. Coverage target: 80% line and 70% branch for new code.

**Organization**: Tasks are grouped by user story. All text is in ASD-STE100 STE. Every task makes a file or a changed file, or runs a command with a pass or fail result.

**MVP**: US1 and US2 ship together (clarification D1). US1 alone is not releasable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an open task)
- **[Story]**: User story of the task (US1 to US5)

All paths are relative to `C:\src\GameBot`.

## Phase 1: Setup

- [x] T001 Create the empty source and test folders by adding the first file of each in the tasks below: `src/GameBot.Domain/Gamepacks/`, `tests/unit/Gamepacks/`, `tests/contract/Gamepacks/`, `tests/integration/Gamepacks/`. Confirm that `dotnet build` of the solution still passes with the new `Gamepacks` folder in `src/GameBot.Domain/Gamepacks/GamepackFile.cs` (see T002).

---

## Phase 2: Foundational (blocks all user stories)

- [x] T002 [P] Create the file model in `src/GameBot.Domain/Gamepacks/GamepackFile.cs` (`GamepackFile`, `GamepackManifest`, `GamepackImage` records; fields per data-model.md; lists default to empty; `requiredApiMajor` optional)
- [x] T003 [P] Create `src/GameBot.Domain/Gamepacks/GamepackNameRules.cs` (FR-019: 1 to 64 characters of letters, digits, `_`, `-`, `.`; no leading `.`; case-insensitive comparer; reserved Windows device names CON, PRN, AUX, NUL, COM1-9, LPT1-9 with or without extension) and unit tests in `tests/unit/Gamepacks/GamepackNameRulesTests.cs` (valid names, empty, 65 characters, leading `.`, bad characters, `Con`, `con.txt`, `COM9`, `LPT1.log`, `COM10` valid, two names that differ only in case are equal)
- [x] T004 [P] Create `src/GameBot.Domain/Gamepacks/GamepackProblem.cs` (category structure/reference/conflict, objectType manifest/sequence/command/image, name, STE message, owner text) and a validation result type in the same folder
- [x] T005 Create `src/GameBot.Domain/Gamepacks/GamepackRegistry.cs` (InstalledGamepack, PreviousVersion, OwnerEntry, owners map; object key `type:name` compared without case), `IGamepackRegistryStore.cs`, and `GamepackRegistryStore.cs` (read and write `<dataRoot>/gamepacks/registry.json` through write-to-temp and atomic replace; read and write `gamepacks/previous/<name>.json`; missing file = empty registry)
- [x] T006 [P] Create `src/GameBot.Domain/Gamepacks/GamepackOwnerResolver.cs` (owner of an object key from the registry; missing key = user (FR-005); an owner entry whose object does not exist is not shown as an owner)
- [x] T007 [P] Unit tests for the registry store and owner resolver in `tests/unit/Gamepacks/GamepackRegistryStoreTests.cs` (empty start, round trip, missing owner = user, atomic replace, previous file round trip, owner entry without object gives user)
- [x] T008 Create the test seam `src/GameBot.Domain/Gamepacks/IGamepackWriteGate.cs` (`BeforeWrite(operation, key)`; operations `create`, `update`, `delete`, `previous-file`, `registry`, `restore`) with a no-op production class `NoOpGamepackWriteGate`, and a test gate `tests/unit/Gamepacks/ThrowingWriteGate.cs` that throws at the Nth call, or only for `restore` operations
- [x] T009 Register the gamepack services in `src/GameBot.Service/GameBotServiceSetup.cs` through one `AddGamepacks` extension method (registry store, owner resolver, no-op write gate, validator, applier, service). Keep `Program.cs` thin. Run `dotnet build` and confirm it passes.

**Checkpoint**: Foundation ready, including the fault-injection seam.

---

## Phase 3: User Story 1 - Install a gamepack as one unit (P1) - MVP part 1

**Goal**: Install all objects of a gamepack, or none. Show the owner on read.
**Independent Test**: Install a valid gamepack into an empty installation. All objects exist and show the gamepack name and version as owner. A failure at any write leaves the installation as before.

### Tests for US1

- [x] T010 [P] [US1] Unit tests for structure and reference checks in `tests/unit/Gamepacks/GamepackValidatorStructureTests.cs` (missing name or version, bad name per FR-019, duplicate names without case, bad image content type, bad base64, decoded image over 10,000,000 bytes, bad image id, missing command or image reference, reference to an object that the install removes, empty lists valid)
- [x] T011 [P] [US1] Unit tests for the applier in `tests/unit/Gamepacks/GamepackApplierTests.cs` (create; update with id retention and rewritten references per FR-020; drop of owned objects per FR-009; capture of live content including hand edits per FR-022; same-version re-install keeps the old previous version per FR-010)
- [x] T012 [P] [US1] Fault-injection tests in `tests/unit/Gamepacks/GamepackApplierFaultInjectionTests.cs` using the gate from T008: throw at every Nth gate call (create, update, delete, previous-file, registry) and check that objects, registry, and previous file equal the state before the request (SC-001); throw in the `restore` step and check the `partial_restore` result and the logged failed object (FR-021)
- [x] T013 [P] [US1] Contract tests in `tests/contract/Gamepacks/GamepackInstallContractTests.cs` (200 on valid install; 422 with problems; 400 on invalid JSON; 500 with `partial_restore` in the body; response shape per contracts/gamepack-api.md)
- [x] T014 [P] [US1] Contract tests in `tests/contract/Gamepacks/GamepackImageRulesContractTests.cs` (image with a non-image content type, bad base64, and decoded size over 10,000,000 bytes each give a structure problem; request over 100 MB gives a 413 problem response and changes nothing; reserved name `CON` and a name with a bad character give a structure problem)
- [x] T015 [P] [US1] Contract tests in `tests/contract/Gamepacks/OwnerFieldContractTests.cs` (sequence, command, and image single and list reads show `owner`; a hand-made object shows `kind: user`; a write request ignores `owner`)
- [x] T016 [P] [US1] Integration tests in `tests/integration/Gamepacks/GamepackInstallIntegrationTests.cs` on real stores (fresh install; update v1 to v2 with a changed owner version; dropped object removed; existing object keeps its id; same-version re-install keeps the old previous version; SC-002)

### Implementation for US1

- [x] T017 [US1] Implement structure and reference checks in `src/GameBot.Domain/Gamepacks/GamepackValidator.cs` (collect all problems; use `GamepackNameRules`; image rules of FR-023 and FR-024; references resolve against the file or the installation after removals; one class for dry run, install, and rollback)
- [x] T018 [US1] Implement `src/GameBot.Domain/Gamepacks/GamepackApplier.cs` (capture the live content of the current version to the previous file; undo snapshot of each affected object, the registry, and the old previous file; call the write gate before each object write, each delete, the previous-file write, and the registry write; write objects, then the previous file, then the registry last; restore in reverse order on any exception; keep ids and rewrite references per FR-020; on a failed restore log the failed object and return `partial_restore`)
- [x] T019 [US1] Implement `src/GameBot.Domain/Gamepacks/GamepackService.cs` (one `SemaphoreSlim` gate for install and rollback; parse, validate, apply; map failures to a clear result)
- [x] T020 [US1] Create `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs` and `src/GameBot.Service/Endpoints/Dto/GamepackDtos.cs` with `POST /api/gamepacks/install` (200, 422, 400, 413, 500 per contract; 100 MB request limit as a 413 problem response; 500 with `result: partial_restore`) and map the route from `GameBotServiceSetup.cs`
- [x] T021 [P] [US1] Add the read-only `owner` field (structured `kind`, `name`, `version`) to the read DTO and mapping in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`
- [x] T022 [P] [US1] Add the read-only `owner` field to the read DTO and mapping in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs`
- [x] T023 [P] [US1] Add the read-only `owner` field to the read DTO and mapping in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`
- [x] T024 [US1] Run `dotnet test` for the US1 tests (T010 to T016) and fix failures until all pass

**Checkpoint**: US1 tests pass. Do not release yet: US2 is part of the MVP.

---

## Phase 4: User Story 2 - Protect user objects and objects of other gamepacks (P1) - MVP part 2

**Goal**: Refuse an install that overwrites a user object or an object of another gamepack. Report every conflict. Keep hand handling unchanged.
**Independent Test**: Make a sequence by hand, install a gamepack with the same sequence name. The install is refused and names the conflict with owner `user`.

- [x] T025 [P] [US2] Unit tests in `tests/unit/Gamepacks/GamepackValidatorConflictTests.cs` (user conflict; other-gamepack conflict with owner text `<name> <version>`; three conflicts all reported (SC-003); same gamepack is an update; case-insensitive name match; same id with another name is NOT a conflict per F1; image conflict by image id)
- [x] T026 [P] [US2] Integration tests in `tests/integration/Gamepacks/GamepackProtectionIntegrationTests.cs` (refused install changes nothing; hand-made object unchanged after install (FR-016); hand delete of an owned object removes its owner entry at once, then a hand-made object with the same name reads `kind: user`, and the next install is then refused with a conflict (US2 scenario 6, FR-015, FR-005))
- [x] T027 [P] [US2] Contract tests in `tests/contract/Gamepacks/HandEditContractTests.cs` (hand edit of an owned object is accepted and the owner record stays; hand create with the name of an owned object of the same type gives 409 and the owner record stays; hand delete of an owned object works as before)
- [x] T028 [US2] Add conflict checks to `src/GameBot.Domain/Gamepacks/GamepackValidator.cs` (type and name match without case; owner from the registry; no other conflict kind; collect every conflict)
- [x] T029 [US2] In `src/GameBot.Domain/Gamepacks/GamepackApplier.cs`, make the applier remove and overwrite only objects that the registry maps to the same gamepack
- [x] T030 [US2] Add the hand-delete hook: in the delete routes of `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, `src/GameBot.Service/Endpoints/CommandsEndpoints.cs`, and `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`, call a new `RemoveOwner(type, name)` method (add it to `GamepackRegistryStore.cs` and `GamepackService.cs`) after a successful delete, so the owner entry is removed at once
- [x] T031 [US2] Add the narrow 409 guard on hand create in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, `src/GameBot.Service/Endpoints/CommandsEndpoints.cs`, and `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`: refuse a create whose type and name match an owned object; do not change the edit routes
- [x] T032 [US2] Run `dotnet test` for T010 to T016 and T025 to T027 together and fix failures until all pass

**Checkpoint**: US1 and US2 work together. This is the MVP.

---

## Phase 5: User Story 3 - List installed gamepacks (P2)

**Goal**: One request lists name, version, and changelog of each gamepack.
**Independent Test**: Install two gamepacks, then request the list.

- [x] T033 [P] [US3] Contract tests in `tests/contract/Gamepacks/GamepackListContractTests.cs` (empty list `[]`; two entries with name, version, changelog, requiredApiMajor, hasPreviousVersion, installedAtUtc; SC-006)
- [x] T034 [US3] Add a list method to `src/GameBot.Domain/Gamepacks/GamepackService.cs` and `GET /api/gamepacks` to `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs`
- [x] T035 [US3] Run `dotnet test` for T033 and fix failures until it passes

---

## Phase 6: User Story 4 - Roll back to the previous version (P2)

**Goal**: Return a gamepack to its kept previous version as one atomic unit, after a conflict and reference check.
**Independent Test**: Install v1, then v2, then roll back. Objects and owner records equal the live state captured just before the v2 install.

- [x] T036 [P] [US4] Contract tests in `tests/contract/Gamepacks/GamepackRollbackContractTests.cs` (200 `rolled_back` with counts; 422 `refused` with problem list; 409 `no_previous_version` for a gamepack installed once; second rollback gives 409; 404 `not_found`; 500 with `partial_restore`)
- [x] T037 [P] [US4] Integration tests in `tests/integration/Gamepacks/GamepackRollbackIntegrationTests.cs` (install-then-rollback equals the captured state including hand edits made before the v2 install, and hand edits made after v2 are lost (SC-004); objects added by v2 removed; changed objects return to v1 content; previous cleared after rollback; fault injection at every Nth gate call leaves the state as before the rollback; restore failure gives `partial_restore`)
- [x] T038 [P] [US4] Rollback conflict-check tests in `tests/integration/Gamepacks/GamepackRollbackConflictTests.cs` (FR-018: a hand-made object now has the name of a previous-version object, so the rollback is refused with the problem list and nothing changes; a previous-version object now owned by another gamepack, refused; a broken reference in the previous content, refused)
- [x] T039 [US4] Add a rollback method to `src/GameBot.Domain/Gamepacks/GamepackApplier.cs` and `src/GameBot.Domain/Gamepacks/GamepackService.cs` that first runs `GamepackValidator` (structure, reference, conflict) on the kept previous content, refuses with the problem list on any problem, else applies the previous content through the same apply path (same write gate, same undo snapshot), removes objects that the undone version added, and then clears `previous`
- [x] T040 [US4] Add `POST /api/gamepacks/{name}/rollback` to `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs` (200 `rolled_back`, 422 `refused`, 409 `no_previous_version`, 404 `not_found`, 500 with `partial_restore`; same gate lock; name match ignores case)
- [x] T041 [US4] Run `dotnet test` for T036 to T038 and fix failures until all pass

---

## Phase 7: User Story 5 - Check a gamepack with a dry run (P3)

**Goal**: Report all problems and the planned counts, and change no data.
**Independent Test**: Dry run on a file with a broken reference, a structure error, and a conflict. All three are reported; stored files are the same before and after.

- [x] T042 [P] [US5] Tests in `tests/integration/Gamepacks/GamepackDryRunIntegrationTests.cs` (stored files byte-equal before and after (SC-005); dry run result equals the real install result for the same input (FR-014); invalid dry run gives 200 with `valid: false`; counts shown for a valid dry run; image and name rules are checked in a dry run)
- [x] T043 [US5] Add the `dryRun` query flag to the install route in `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs` and the stop-before-apply path in `src/GameBot.Domain/Gamepacks/GamepackService.cs`, with planned created, updated, and removed counts
- [x] T044 [US5] Run `dotnet test` for T042 and fix failures until it passes

---

## Phase 8: Polish and cross-cutting

- [x] T045 [P] Add OpenAPI schema descriptions for the new DTOs and the `owner` field in `src/GameBot.Service/Swagger/GamepackSchemaFilter.cs` (follow the existing `ISchemaFilter` pattern; no XML comments), register it, and add `tests/contract/Gamepacks/GamepackOpenApiTests.cs` that checks the three routes and the `owner` schema are in the OpenAPI document
- [x] T046 [P] Write `docs/gamepacks.md` in STE (file format, three routes, owner field with both shapes, conflict rules, rollback check, dry run, image and name rules, 100 MB limit and 413, `partial_restore`; a note to avoid hand changes during an install; a note that references from user objects to a dropped object are not checked) (FR-017)
- [x] T047 [P] Update `docs/architecture.md`: add the gamepack API surface, the persistence layout (`gamepacks/registry.json`, `gamepacks/previous/<name>.json`), and set the "Last reviewed" date (FR-017, Principle V)
- [x] T048 [P] Add a row for feature 143 to `specs/STATUS.md` (FR-017)
- [x] T049 [P] Add an entry for the gamepack install to `CHANGELOG.md` in STE
- [x] T050 [P] Add a soft performance test in `tests/integration/Gamepacks/GamepackPerformanceTests.cs` (install of 100 objects in under 5 seconds; generous threshold; not a hard CI gate)
- [x] T051 Run the steps in `specs/143-gamepack-install/quickstart.md` against a test host; fix each difference in code or in quickstart.md
- [x] T052 Build the solution and run the full test suite with the repository coverage run (`dotnet test` with coverage); fix failures; confirm coverage of new code is at least 80% line and 70% branch
- [x] T053 Set the Status in `specs/143-gamepack-install/spec.md` to its done value and update the row in `specs/STATUS.md` (FR-017)
- [x] T054 Bump the version in `installer/versioning/version.override.json` per CLAUDE.md before the push (last task): run `git fetch origin master`, compare the file with `origin/master`, set `patch` to the `origin/master` patch plus 1 (or `"0"` if `minor` or `major` changed), set `updatedBy` and `updatedAtUtc`

---

## Dependencies and Execution Order

- Phase 1, then Phase 2 (blocks all stories). The write gate (T008) must exist before T012.
- US1 (Phase 3) and US2 (Phase 4) are the MVP and ship together. US2 extends the validator and applier of US1, so it follows US1. T030 and T031 edit the same endpoint files as T021 to T023, so they follow those tasks.
- US3 needs only the registry and the service from US1.
- US4 needs the applier from US1 and the conflict check from US2 (rollback uses the same validator).
- US5 needs the validator and applier from US1 and US2.
- Phase 8 follows all stories. T053 follows T052. T054 is last.
- Tasks that edit the same file (`GamepacksEndpoints.cs`, `GamepackValidator.cs`, `GamepackApplier.cs`, `GamepackService.cs`) run in order.

## Parallel Opportunities

- Phase 2: T002, T003, T004 together; T006 and T007 after T005.
- US1: T010 to T016 together; T021, T022, T023 together.
- US2: T025, T026, T027 together.
- US3 and US4 test tasks (T033, T036, T037, T038) can start once US2 is done.
- Phase 8: T045 to T050 together.

## Implementation Strategy

1. MVP: Phase 1, Phase 2, US1, and US2 together. Stop and check that install is atomic (fault injection) and that user objects stay safe. Do not release US1 alone.
2. Add US3 (list), US4 (rollback with conflict check), US5 (dry run).
3. Finish with Phase 8 (docs, `docs/architecture.md`, `specs/STATUS.md`, CHANGELOG, then the version bump last). Do not open the PR until build and tests pass.

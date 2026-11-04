# Tasks: Install the Same Gamepack Version Again (Force Install)

**Input**: Design documents in `C:\src\GameBot\specs\178-force-reinstall-gamepack\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts\force-reinstall-api.md, quickstart.md

**Tests**: Tests are required. Constitution II needs 80% line coverage and 70% branch coverage for touched files.

**Format**: `- [ ] T### [P?] [US?] Description with file path`. `[P]` means the task can run in parallel. It uses different files and has no open dependency.

**Paths**: Domain code is in `C:\src\GameBot\src\GameBot.Domain\Gamepacks\`. Service code is in `C:\src\GameBot\src\GameBot.Service\Endpoints\`. Tests are in `C:\src\GameBot\tests\`.

**STE**: All new text uses ASD-STE100 Simplified Technical English. This covers comments, OpenAPI text, log text, error text, and docs. Each sentence has 20 words or fewer. Each sentence gives one instruction.

**Terms**: The force option is `force`. The saved state of a forced install is the "reinstall point". It is separate from the kept previous version.

**Routes**: There are exactly four routes (FR-004). The managed update (`managed=true`) is a mode of the update routes. It is not a fifth route.

## Phase 1: Setup

- [ ] T001 Build the solution `C:\src\GameBot\GameBot.sln`.
- [ ] T002 Run the tests in `tests\unit\Gamepacks`, `tests\contract\Gamepacks`, and `tests\integration\Gamepacks`.
- [ ] T003 Stop if a result of T001 or T002 is red. Fix the cause before you continue.
- [ ] T004 [P] Read `GamepackService.cs`, `GamepackApplier.cs`, and `GamepackValidator.cs` in the Domain folder. Find the edit points in plan.md "Design Notes".
- [ ] T005 [P] Read `Managed\GamepackUpdateOrchestrator.cs` in the Domain folder. Find where `IsSameVersion` and the rollback check are used.
- [ ] T006 [P] Find the existing `IGamepackWriteGate` type and its test helpers. Note how a test can make a store write fail.

## Phase 2: Foundational (blocks all user stories)

**Goal**: Add the shared domain types and the reinstall point storage.

- [ ] T007 [P] Add `Force` (bool, default false) to `GamepackInstallOptions` in `GamepackInstallOptions.cs`. Add XML docs.
- [ ] T008 [P] Add record `OverwriteEntry(string Type, string Name)` in `GamepackPlan.cs`.
- [ ] T009 In `GamepackPlan.cs`, add `GamepackPlan.Overwrites` with an empty default.
- [ ] T010 [P] Add optional `Reinstall` (`PreviousVersion?`) to `InstalledGamepack` in `GamepackRegistry.cs`.
- [ ] T011 In `GamepackRegistry.cs`, make sure an old registry file without `Reinstall` still loads.
- [ ] T012 [P] Add default members `ReadReinstall`, `WriteReinstall`, `DeleteReinstall`, and `ReinstallFilePath` to `IGamepackRegistryStore.cs`.
- [ ] T013 Implement the four reinstall members in `GamepackRegistryStore.cs`. Use the file `gamepacks\reinstall\<name>.json`.
- [ ] T014 In `GamepackRegistryStore.cs`, make the reinstall write atomic. Make a delete of a missing file succeed.
- [ ] T015 Add enum `ReinstallPointHandling` (`Keep`, `Capture`, `Clear`) in `GamepackApplier.cs`.
- [ ] T016 In `GamepackApplier.cs`, add the optional `reinstall` argument to `ApplyAsync`.
- [ ] T017 In `GamepackApplier.cs`, implement `Keep` (no change) and `Clear` (delete the reinstall point).
- [ ] T018 In `GamepackApplier.cs`, implement `Capture`. Write the live content of the installed version before the first object write.
- [ ] T019 In `GamepackApplier.cs`, use this write order: objects, previous file, reinstall file, registry.
- [ ] T020 In `GamepackApplier.cs`, make the failure restore also restore the reinstall file.
- [ ] T021 Add the `reinstall` argument to `ValidateAsync` in `GamepackValidator.cs`. Without it, set no live content.
- [ ] T022 In `GamepackValidator.cs`, set `LiveSequence` and `LiveCommand` on update writes when `reinstall` is true.
- [ ] T023 In `GamepackValidator.cs`, add method `CompareOverwritesAsync`. It fills `Plan.Overwrites`.
- [ ] T024 In `CompareOverwritesAsync`, list only objects whose stored content differs from the file.
- [ ] T025 In `CompareOverwritesAsync`, do not list an object that equals the file. Do not list a hand-deleted object.
- [ ] T026 In `CompareOverwritesAsync`, do not list take-over objects.
- [ ] T027 [P] Add `ReinstallFileText` to `GamepackService.Snapshot.cs`. It reads the reinstall file text for a restore.
- [ ] T028 [P] Add unit tests for the four reinstall store members in `tests\unit\Gamepacks\GamepackReinstallStoreTests.cs`.
- [ ] T029 [P] Add unit tests for `CompareOverwritesAsync` in `tests\unit\Gamepacks\GamepackOverwriteCompareTests.cs`. Cover equal, differing, and hand-deleted objects.
- [ ] T030 [P] Add unit tests for `ReinstallPointHandling` in `tests\unit\Gamepacks\GamepackApplierReinstallTests.cs`. Cover Keep, Capture, and Clear.
- [ ] T031 In the same test file, add a test for the failure restore of the reinstall file.
- [ ] T032 Build the solution. Run the new unit tests. Fix all failures.

**Checkpoint**: The domain types and the reinstall storage work. User stories can start.

## Phase 3: User Story 1 - Repair an installed pack from its file (P1)

**Goal**: A forced install of the same name and version rewrites every object from the file.

**Independent Test**: Install a pack. Install it again with `force`. Check that every object has the file content.

- [ ] T033 [P] [US1] Add a contract test in `tests\contract\Gamepacks\GamepackForceInstallContractTests.cs`. It posts `POST /api/gamepacks/install` without `force` for the same version.
- [ ] T034 [US1] In the same file, check that the result is the same as before this feature (FR-003, SC-006).
- [ ] T035 [P] [US1] Add an integration test in `tests\integration\Gamepacks\GamepackForceReinstallTests.cs`. It installs a pack, then installs it again with `force`.
- [ ] T036 [US1] In the same file, check that every object has the file content (SC-002).
- [ ] T037 [US1] In the same file, add a test that changes one object by hand. Check that a forced install restores the file content.
- [ ] T038 [US1] In the same file, add the hand-deleted object test (scenario 5, FR-002). Delete one object by hand.
- [ ] T039 [US1] In the hand-deleted object test, run a forced install. Check that the object exists again with the file content.
- [ ] T040 [US1] In the same file, add the failure test (scenario 4, FR-006, SC-003). Use an injected store write fault.
- [ ] T041 [US1] In the failure test, make the store write fail through `IGamepackWriteGate`. Name the fault `FailingWriteGate`.
- [ ] T042 [US1] In the failure test, run a forced install. Check that the install fails and no object changed.
- [ ] T043 [US1] In the failure test, check that no reinstall point stays that hides the earlier state.
- [ ] T044 [US1] Add a test with the same version in the same pack file and no changelog file in the pack (FR-011).
- [ ] T045 [US1] Add a test for a forced install when the pack is not installed. Check that it is a normal first install.
- [ ] T046 [US1] Add a test for `force` with a different version. Check that the update and downgrade rules do not change (FR-010).
- [ ] T047 [US1] In `GamepackService.cs`, read the registry record in `InstallAsync`. A call is a forced reinstall when `Force` is true and the versions are equal.
- [ ] T048 [US1] In `GamepackService.cs`, pass `reinstall` to `ValidateAsync` for a forced reinstall.
- [ ] T049 [US1] In `GamepackService.cs`, call `CompareOverwritesAsync` for a forced reinstall. Keep the take-over path unchanged.
- [ ] T050 [US1] In `GamepackService.cs`, call the applier with `Capture` for a forced reinstall.
- [ ] T051 [US1] In `GamepackService.cs`, call the applier with `Clear` for a normal install of a different version.
- [ ] T052 [US1] In `GamepackService.cs`, keep the old path when `Force` is false.
- [ ] T053 [US1] Add `Force` to the request DTO of `POST /api/gamepacks/install` in `GamepackDtos.cs`. Pass it to the options.
- [ ] T054 [US1] Add the `reinstalled` member to the install response DTO in `GamepackDtos.cs`.
- [ ] T055 [US1] Wire `force` in the endpoint of `POST /api/gamepacks/install` in `GamepacksEndpoints.cs`. Add OpenAPI text in STE.
- [ ] T056 [US1] Run the tests of this phase. Fix all failures.

**Checkpoint**: A forced install of the same version repairs a pack. The failure test passes.

## Phase 4: User Story 2 - See what the forced install overwrites (P2)

**Goal**: The response lists the overwritten objects. A dry run shows the list and changes nothing.

**Independent Test**: Change one object by hand. Run a forced dry run. Check the list and check that nothing changed.

- [ ] T057 [P] [US2] Add a contract test in `GamepackForceInstallContractTests.cs`. A forced install lists a hand-changed object (type, name) in `overwritten`.
- [ ] T058 [US2] In the same test, check that an object equal to the file is not listed (FR-008).
- [ ] T059 [P] [US2] Add an integration test in `GamepackForceReinstallTests.cs` for a forced dry run (FR-009).
- [ ] T060 [US2] In the dry run test, check that the response has the result and the list.
- [ ] T061 [US2] In the dry run test, check that 0 objects changed and 0 reinstall points exist (SC-005).
- [ ] T062 [US2] Add the `overwritten` member to the install response DTO in `GamepackDtos.cs`.
- [ ] T063 [US2] In `GamepackService.cs`, fill `Overwritten` from `plan.Overwrites` after a forced install.
- [ ] T064 [US2] In the dry run path of `GamepackService.cs`, return `Overwritten` from `plan.Overwrites`.
- [ ] T065 [US2] In the dry run path, do not call the applier. Do not create a reinstall point.
- [ ] T066 [US2] Run the tests of this phase. Fix all failures.

**Checkpoint**: The caller sees the overwritten objects before and after the install.

## Phase 5: User Story 3 - Undo a forced install (P2)

**Goal**: The rollback operation restores the state before the forced install.

**Independent Test**: Change an object by hand. Run a forced install. Run a rollback. Check the hand-made change.

- [ ] T067 [P] [US3] Add an integration test in `tests\integration\Gamepacks\GamepackForceRollbackTests.cs`. It changes an object, forces a reinstall, and rolls back.
- [ ] T068 [US3] In the same test, check that the object has the hand-made change again (SC-004).
- [ ] T069 [US3] In the same test, check that the version is still X and the kept previous version did not change.
- [ ] T070 [P] [US3] Add a test for `RemoveAsync` and `RemoveInstalledAsync`. Check that the reinstall file is deleted.
- [ ] T071 [US3] In `GamepackService.cs`, make `RollbackAsync` use the reinstall point first. Then use the previous version.
- [ ] T072 [US3] In `GamepackService.cs`, delete the reinstall file in `RemoveAsync` and `RemoveInstalledAsync`.
- [ ] T073 [US3] In `GamepackUpdateOrchestrator.cs`, make the managed rollback check accept a reinstall point as a rollback source.
- [ ] T074 [US3] Add `hasReinstallPoint` to the list DTO in `GamepackDtos.cs` (FR-012).
- [ ] T075 [US3] Fill `hasReinstallPoint` in the list endpoint in `GamepacksEndpoints.cs`.
- [ ] T076 [US3] Add a contract test for `hasReinstallPoint` in `GamepackForceInstallContractTests.cs`. Check the value before and after a forced install.
- [ ] T077 [US3] Run the tests of this phase. Fix all failures.

**Checkpoint**: A forced install is reversible.

## Phase 6: User Story 4 - Force option on the other routes (P3)

**Goal**: The same `force` option works on the update route and on both catalog routes.

**Independent Test**: Install from the catalog. Install the same version with `force` on each route.

- [ ] T078 [P] [US4] Add a contract test for exactly four routes with `force`. List the four routes of FR-004.
- [ ] T079 [P] [US4] Add an integration test for `POST /api/gamepacks/catalog/install` with `force` in `tests\integration\Gamepacks\GamepackForceCatalogTests.cs`.
- [ ] T080 [US4] In the same file, add a test for `POST /api/gamepacks/catalog/update` with `force` and an equal latest version.
- [ ] T081 [US4] In the same file, add a test for `POST /api/gamepacks/update` with `force`.
- [ ] T082 [US4] In the same file, add a test for the managed update (`managed=true`) with `force`. Check that it installs the equal version again (FR-005).
- [ ] T083 [US4] In the same file, add a test for an update route without `force` and with an equal version. Check that nothing happens.
- [ ] T084 [US4] In `GamepackUpdateOrchestrator.cs`, make `Force` set `IsSameVersion` to false.
- [ ] T085 [US4] In `GamepackUpdateOrchestrator.cs`, pass `Force` to the dry run check and to the install.
- [ ] T086 [US4] Add `Force` to the request DTO of the update route in `GamepackDtos.cs`. Wire it in `GamepacksEndpoints.cs`.
- [ ] T087 [US4] Add `Force` to the catalog DTOs in `GamepackCatalogDtos.cs`.
- [ ] T088 [US4] Wire `force` in the catalog install and catalog update endpoints in `GamepackCatalogEndpoints.cs`.
- [ ] T089 [US4] Wire `force` in the managed mode in `GamepackManagedEndpoints.cs`.
- [ ] T090 [US4] Add OpenAPI text in STE for `force` on all four routes.
- [ ] T091 [US4] Run the tests of this phase. Fix all failures.

**Checkpoint**: All four routes accept `force`.

## Phase 7: Performance (SC-007)

- [ ] T092 Add a timed test in `tests\integration\Gamepacks\GamepackForceReinstallPerfTests.cs`. Mark it `[Trait("Category","Performance")]`.
- [ ] T093 In the timed test, use a pack with 200 objects. Do one warm-up run.
- [ ] T094 In the timed test, do at least 5 runs each of normal install and forced install.
- [ ] T095 In the timed test, compare the medians. Assert that the ratio is 1.5 or less.
- [ ] T096 Make the timed test write the ratio and both medians to `specs\178-force-reinstall-gamepack\quickstart.md`.
- [ ] T097 Run the timed test locally. Keep it out of CI unless it is stable.

## Phase 8: Polish and cross-cutting

- [ ] T098 [P] Review `C:\src\GameBot\docs\architecture.md`. Add the reinstall point and `force` in STE.
- [ ] T099 [P] Update the API docs under `C:\src\GameBot\docs\` for `force`, `reinstalled`, `overwritten`, and `hasReinstallPoint`.
- [ ] T100 [P] Add an entry to `C:\src\GameBot\CHANGELOG.md` for the force install. Write it in STE.
- [ ] T101 Add row 178 to `C:\src\GameBot\specs\STATUS.md`.
- [ ] T102 Run the full build and all tests. Fix all failures.
- [ ] T103 Check coverage of touched files. The goal is 80% line and 70% branch.
- [ ] T104 Run `C:\src\GameBot\scripts\ci\measure-shards.ps1`. Check that the new tests keep the shards balanced.
- [ ] T105 Adjust the shard setup if the balance check fails. Run the script again.
- [ ] T106 Copy the SC-007 ratio and both medians to the PR text.
- [ ] T107 Run the steps in `quickstart.md`. Fix any difference.
- [ ] T108 Set the `Status` line of `spec.md` to "Implemented".
- [ ] T109 Run `git fetch origin master`. Compare `installer\versioning\version.override.json` with `origin/master`.
- [ ] T110 If `minor` or `major` changed, set `patch` to "0". If not, set `patch` to the `origin/master` value plus 1.
- [ ] T111 Set `updatedBy` and `updatedAtUtc` in `installer\versioning\version.override.json`.
- [ ] T112 Push the branch and open the PR. Do this only after T111.

## Dependencies

- Phase 1 comes first. Phase 2 blocks all user stories.
- US1 (Phase 3) is the MVP. US2 and US3 need US1 service code.
- US3 needs the `Capture` code from Phase 2 and US1.
- US4 needs US1 and the orchestrator change.
- Phase 7 needs US1. Phase 8 comes last. T109 to T112 are the last tasks.

## Parallel examples

- Phase 2: T007, T008, T010, T012, and T027 touch different files.
- Phase 2 tests: T028, T029, and T030 touch different files.
- Phase 3 tests: T033 and T035 touch different files.
- Phase 8: T098, T099, and T100 touch different files.

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1. Stop and check the repair of a pack.
- Then add US2 and US3 (safety), then US4 (more routes), then the timed test and polish.

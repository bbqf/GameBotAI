# Tasks: Seed queue templates in a gamepack

**Input**: Design documents in `C:\src\GameBot\specs\150-gamepack-seed-templates\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts\seed-templates-api.md, quickstart.md
**Tests**: Required (FR-013). Each acceptance scenario has one test. No test is repeated in two stories.
**Format**: `- [ ] T### [P?] [US?] Description with file path`.
**[P] rule**: [P] marks a task that has no dependency on an unfinished task. Tasks of an earlier phase are finished before a phase starts. A task with a "Depends on" note has no [P]. Two [P] tasks never edit the same file.
**Language**: Write all new text (comments, docs, messages) in ASD-STE100 Simplified Technical English.

Paths: `D` = `C:\src\GameBot\src\GameBot.Domain\Gamepacks`, `Q` = `C:\src\GameBot\src\GameBot.Domain\QueueSettings`, `S` = `C:\src\GameBot\src\GameBot.Service`, `T` = `C:\src\GameBot\tests`.

## Phase 1: Setup

- [ ] T001 Read `C:\src\GameBot\specs\150-gamepack-seed-templates\research.md` and the files `D\GamepackApplier.cs`, `D\GamepackValidator.cs`, `D\GamepackPlan.cs`, `D\GamepackService.cs`, and `S\Endpoints\QueueTemplatesEndpoints.cs`. Find the current template rules and the apply/restore flow. No file change.
- [ ] T002 [P] Add `D\Seeds\SeedPlan.cs` with `SeedPlan`, `SeedPlanItem` (Name, Outcome, Entries with SequenceId, TemplateId), `SeedOutcomes` (`create`, `skip_exists`), and `SeedSkipReasons` (`Exists` = `skipped_exists`), per data-model.md. Add STE comments on public members.
- [ ] T003 [P] Add `D\Seeds\SeedSkip.cs` with record `SeedSkip(Name, Reason)`.

## Phase 2: Foundational (blocks all user stories)

- [ ] T004 [P] Add `Q\QueueTemplateRules.cs` (namespace `GameBot.Domain.QueueSettings`): shared checks for name pattern and length, unique names without case, non-empty entry sequence, schedule type and timer rules, and parameter names (`ParameterNameRules.ValidateName`). Move the logic out of `S\Endpoints\QueueTemplatesEndpoints.cs`. Do not put the class in a Seeds folder.
- [ ] T005 Change `S\Endpoints\QueueTemplatesEndpoints.cs` to call `QueueTemplateRules`. No behavior change. Depends on T004.
- [ ] T006 Add unit tests for `QueueTemplateRules` in `T\unit\QueueSettings\QueueTemplateRulesTests.cs` (each rule, valid and invalid). Run the existing queue template contract tests to prove no behavior change. Depends on T004.
- [ ] T007 [P] Add member `SeedTemplates` (`IReadOnlyList<QueueTemplateDoc>`, default empty) to `D\GamepackFile.cs`.
- [ ] T008 Change `D\GamepackFileSerializer.cs` to read `seedTemplates` (member name without case) and to write it only when not empty. Add unit tests in `T\unit\Gamepacks\GamepackFileSerializerSeedTests.cs`: round trip, missing member, byte-equal output for a file without seeds (SC-004). Depends on T007.
- [ ] T009 [P] Add constant `SeedTemplate = "seedTemplate"` to `GamepackObjectTypes` in `D\GamepackProblem.cs`. Do not add it to the owner types.
- [ ] T010 [P] Add `Seeds` (`SeedPlan`, default empty), `SeedsToCreate`, and `SeedsSkipped` to `D\GamepackPlan.cs`.
- [ ] T011 [P] Add `SeedTemplatesCreated` and `SeedTemplatesSkipped` (always set, default empty) to `GamepackInstallOutcome` in `D\GamepackService.cs`. Add the same two members to `GamepackApplyOutcome` in `D\GamepackApplier.cs`.
- [ ] T012 [P] Add DTO members `seedTemplatesCreated` and `seedTemplatesSkipped`, and type `GamepackSeedSkipDto` (`name`, `reason`), in `S\Endpoints\Dto\GamepackDtos.cs`, `S\Endpoints\Dto\ManagedUpdateReportDto.cs`, and `S\Endpoints\Dto\GamepackCatalogDtos.cs`. Always serialize both arrays.

**Checkpoint**: Solution builds. Existing gamepack and queue template tests pass.

## Phase 3: User Story 1 - New user gets a starting template (P1)

**Goal**: A first install creates each seed whose name is free. It skips a seed whose name exists. A file without seeds installs as before.
**Independent test**: Install a gamepack with one seed on an instance with no template of that name. The template exists and is in `seedTemplatesCreated`.

- [ ] T013 [P] [US1] Write unit tests in `T\unit\Gamepacks\GamepackValidatorSeedTests.cs` for a first install: `create` for a free name, `skip_exists` for a used name (compared without case); entry sequence names resolve to ids from the file or the instance; invalid seed gives a `structure` problem; duplicate seed names give a `structure` problem; a file without seeds gives an empty plan.
- [ ] T014 [P] [US1] Write unit tests in `T\unit\Gamepacks\GamepackApplierSeedTests.cs`: the applier writes the planned templates (new id, name, entries, no owner entry) and returns their names.
- [ ] T015 [P] [US1] Write a unit test in `T\unit\Gamepacks\GamepackApplierSeedRaceTests.cs` for FR-016: the plan says `create`, then the name is used before the write. The applier writes nothing for that seed, leaves the existing template unchanged, and reports `skipped_exists`.
- [ ] T016 [P] [US1] Write contract tests in `T\contract\Gamepacks\GamepackSeedInstallContractTests.cs`: scenarios 1.1, 1.2, 1.3 through `POST /api/gamepacks`, and the same through the catalog install route. Check that both arrays are always present, and that a file without `seedTemplates` gives the same result as before (SC-004). Do not test `reference` problems here (see T029).
- [ ] T017 [P] [US1] Change `D\GamepackValidator.cs`, in this one task: validate seeds with `QueueTemplateRules` (`structure`, object type `seedTemplate`) on every install; do the first-install registry lookup (no record for the name) with the `seeds` parameter; handle both branches. First install: resolve entry sequences (file first, then instance), set `reference` problems, and build the `SeedPlan` with `create` or `skip_exists` from the template store (read once, only when the file has seeds). Not first install: return an empty `SeedPlan`.
- [ ] T018 [P] [US1] Change `D\GamepackApplier.cs`: add the optional template repository parameter. In `ApplyAsync` create the planned seed templates after the object writes and before the previous-file and registry writes. Check that the name is still free just before each write (FR-016). If the name is used, skip the write and add a `skipped_exists` item to the outcome. Keep the ids of the created templates.
- [ ] T019 [US1] Change `D\GamepackService.cs` to copy the seed lists from the plan and the apply outcome into `GamepackInstallOutcome`, for the install and the dry run (the dry run takes the lists from the plan and does not call the applier). Merge the plan skips with the write-time skips. Check that DI gives the template repository to the applier (`S\Endpoints\GamepackServiceCollectionExtensions.cs`). Depends on T017, T018.
- [ ] T020 [US1] Change `S\Endpoints\GamepacksEndpoints.cs` (`ToInstallResponse`, shared by the install and the dry run) and `S\Endpoints\GamepackCatalogEndpoints.cs` to fill the two lists in the response. Depends on T019.
- [ ] T021 [US1] Change `D\Catalog\GamepackCatalogService.cs` and `D\Managed\ManagedUpdateReport.cs` to carry the two lists. Depends on T019.

**Checkpoint**: US1 tests pass. MVP is complete.

## Phase 4: User Story 2 - User adjustments stay safe (P1)

**Goal**: Only a first install touches seeds. A failed install leaves no seed. A later install ignores seeds (FR-015).
**Independent test**: Install, change the template, update with a changed seed: the template is unchanged. Delete the template and install again: it is not created.

- [ ] T022 [P] [US2] Write contract tests in `T\contract\Gamepacks\GamepackSeedLaterInstallContractTests.cs`: scenarios 2.1 to 2.5 (user change kept, deleted template not created again, new seed in a later version not created, rollback keeps the template, settings export lists it as an ordinary template with no gamepack link). Add a managed update test that checks both lists are present and empty.
- [ ] T023 [P] [US2] Write integration tests in `T\integration\Gamepacks\GamepackSeedLifecycleTests.cs`: first install, later install, delete then install. Add a fault-injection test: force a failure after seeds are created and check that no seed template stays (FR-014).
- [ ] T024 [P] [US2] Write unit tests in `T\unit\Gamepacks\GamepackValidatorSeedLaterInstallTests.cs` for FR-015: a bad seed structure still gives a `structure` problem when a registry record exists; a seed that names a missing sequence gives no `reference` problem; a rollback runs no seed check and plans no seed. Do not repeat the empty-plan test of T013.
- [ ] T025 [P] [US2] Write unit tests in `T\unit\Gamepacks\GamepackApplierSeedUndoTests.cs` for the best-effort undo (FR-014): after a failure, the applier deletes the created seed templates by id; when one removal throws, the applier logs the error and still removes the other created templates; a rollback deletes no seed template (FR-007).
- [ ] T026 [P] [US2] Change `D\GamepackValidator.cs`: skip the seed reference check on a later install, and add the guard that runs no seed check on a rollback (`seeds: false`). The registry lookup and the empty plan are in T017. Structure check stays on every install, dry run, and update (FR-005, FR-015).
- [ ] T027 [P] [US2] Change `D\GamepackApplier.cs`: on any failure delete the seed templates that this install created, by id, in the existing restore path (FR-014). Log each removal error and continue with the next id and the object restore. Delete nothing on a rollback (FR-007).
- [ ] T028 [US2] Change `D\GamepackService.cs` so the update and rollback paths pass the right first-install state and return empty seed lists. Depends on T026, T027.

**Checkpoint**: US1 and US2 tests pass.

## Phase 5: User Story 3 - Dry run shows the seeds (P2)

**Goal**: A dry run lists the seeds it would create and skip, reports `reference` problems, and changes no state.
**Independent test**: Run a dry run with a seed. The lists match the real install. No template is created.

The dry-run result wiring is part of T019 and T020 (shared with the install). No new wiring task is needed.

- [ ] T029 [P] [US3] Write contract tests in `T\contract\Gamepacks\GamepackSeedDryRunContractTests.cs`: scenarios 3.1, 3.2, 3.3. Scenario 3.1: would create, would skip, lists equal the lists of the real install (SC-003). Scenario 3.2: a seed entry that names a missing sequence gives a `reference` problem, `valid: false` in the dry run, and 422 in the install with no change. This is the only 422 reference test. Scenario 3.3: no seed after an earlier install.
- [ ] T030 [P] [US3] Write a unit test in `T\unit\Gamepacks\GamepackServiceDryRunSeedTests.cs` that checks only this: a dry run with seeds makes no template write (the fake template repository records zero create or delete calls).

**Checkpoint**: All user stories work.

## Phase 6: Signature, OpenAPI, docs, release

- [ ] T031 [P] Write unit tests in `T\unit\Gamepacks\GamepackContentHasherSeedTests.cs` for FR-017. Before you change the hasher, compute the hash and the signature check result of an existing signed fixture without seeds, and write them as literal expected values. Add a test that a signed file gets a seed after signing and then fails the signature check. Add a test for a file with a signed seed entry.
- [ ] T032 Change `D\Signing\GamepackContentHasher.cs` to hash `seedTemplates` (type `seedTemplate`, canonical SHA-256 of the raw JSON). Add no seed entry when the list is empty. Change `D\Signing\SignatureFile.cs` to accept the type. Depends on T031.
- [ ] T033 [P] Update `S\Swagger\GamepackSchemaFilter.cs` and `S\Swagger\ManagedUpdateSchemaFilter.cs` to describe `seedTemplates`, `seedTemplatesCreated`, `seedTemplatesSkipped`, and `GamepackSeedSkipDto`. Add a contract test in `T\contract\Gamepacks\GamepackSeedOpenApiTests.cs` that each new member has a description (FR-012).
- [ ] T034 [P] Update `C:\src\GameBot\docs\gamepacks.md` (file member, first-install rule, result fields, `skipped_exists`, problems, signature) and `C:\src\GameBot\docs\architecture.md` if it lists gamepack file members.
- [ ] T035 [P] Update `C:\src\GameBot\specs\STATUS.md` for feature 150.
- [ ] T036 Run `dotnet build "C:\src\GameBot\GameBot.sln"` and `dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~Gamepack"`. Run the queue template tests. Fix all failures. Do the manual steps in `quickstart.md` (including the SC-005 step) if a service is available. Depends on T001 to T035.
- [ ] T037 Review all new text for STE (Principle VI). Depends on T036.
- [ ] T038 Bump the version in `C:\src\GameBot\installer\versioning\version.override.json` before the push. Run `git fetch origin master` and compare the file with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`. Depends on T037.

## Dependencies and order

- Phase 1 -> Phase 2 -> Phase 3 (US1) -> Phase 4 (US2) -> Phase 5 (US3) -> Phase 6.
- US2 changes the same files as US1 (validator, applier, service), so it runs after US1.
- Phase 2: T005 and T006 after T004. T008 after T007.
- Phase 3: T019 after T017 and T018. T020 and T021 after T019.
- Phase 4: T028 after T026 and T027.
- Phase 5: tests only. They run after T020 and T028.
- Phase 6: T032 after T031. T036 after T001 to T035. T037 after T036. T038 last, before the push.

## Parallel examples

- Phase 1: T002 and T003.
- Phase 2: T004, T007, T009, T010, T011, T012.
- US1: T013 to T018 together (six files).
- US2: T022 to T027 together (T026 and T027 edit different files).
- US3: T029 and T030.
- Phase 6: T031, T033, T034, T035.

## Implementation strategy

- MVP = Phase 1 to 3 (US1): a first install creates seeds and reports them.
- Then US2 (safety, later-install rule, best-effort failure undo). It is also P1 and ships in the same PR.
- Then US3 (dry run tests), the signature (FR-017), OpenAPI, and docs.
- Last, run build and tests, check STE, and bump the version before the push.

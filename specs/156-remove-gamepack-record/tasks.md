# Tasks: Remove an installed gamepack record

**Input**: Design documents in `C:\src\GameBot\specs\156-remove-gamepack-record\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts\remove-gamepack.md, quickstart.md

**Tests**: Required. FR-011 asks for contract tests. FR-007 needs a unit test with a failing store.

**Format**: `- [ ] [TaskID] [P?] [Story?] Description with file path`. [P] means the task can run in parallel (other file, no open dependency).

## Phase 1: Setup

- [ ] T001 Read `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackService.cs` (`RollbackAsync`, `_gate`, store use), `C:\src\GameBot\src\GameBot.Service\Endpoints\GamepacksEndpoints.cs` (rollback and `DELETE keys/{fingerprint}` handlers, `ProblemResult`, `WriteFailed`), and `C:\src\GameBot\tests\contract\Gamepacks\GamepackRollbackContractTests.cs` (host use). Do not change any file. Keep the patterns for the next tasks.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The service method that all three stories use.

- [ ] T002 Add `GamepackRemoveStatus` (`Removed`, `NotFound`, `Failed`) and `GamepackRemoveOutcome` (`Status`, `Name`, `Version`, `OwnersRemoved`, `HadPreviousVersion`, `ErrorMessage`) next to the rollback outcome types in `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackService.cs`, as in data-model.md.
- [ ] T003 Add `GamepackService.RemoveAsync(string name, CancellationToken ct = default)` in `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackService.cs`. Take `_gate`. An invalid name (`GamepackNameRules.IsValid`) or a name with no record (`GamepackNameRules.Key`) gives `NotFound` with no write. Else do one `IGamepackRegistryStore.Update` call that removes the record and every owner entry whose `Gamepack` equals the record name (`OrdinalIgnoreCase`). Do not read the sequence, command, or image stores. After the write works, call `DeletePrevious(name)` when the record had a previous version; on a throw, log a warning and still report `Removed`. A throw from `Update` gives `Failed` with `ErrorMessage` and the registry stays as before (research D-002, D-003, D-004). Use STE in comments.

**Checkpoint**: The service method builds.

---

## Phase 3: User Story 1 - Remove the record of a trial install (Priority: P1) MVP

**Goal**: One request removes the record, and the list no longer shows the pack.

**Independent Test**: Install a pack, send `DELETE /api/gamepacks/{name}`, read `GET /api/gamepacks`: the pack is not in the list.

- [ ] T004 [US1] Add the handler method `RemoveAsync` (a named method, not a large lambda) in `C:\src\GameBot\src\GameBot.Service\Endpoints\GamepacksEndpoints.cs`, and map `DELETE {name}` in the existing group with `.WithName("RemoveGamepack")`. Map `Removed` to `204`, `NotFound` to `404` through `ProblemResult` (`result` = `not_found`, title "No gamepack has this name."), and `Failed` to `500` through `WriteFailed("failed", detail)`.
- [ ] T005 [P] [US1] Create `C:\src\GameBot\tests\contract\Gamepacks\GamepackRemoveContractTests.cs` with a test per case, each with its own `GamepackContractHost`: (a) remove gives `204` with no body and the list does not show the pack; (b) remove works after all content items were deleted by hand (FR-006); (c) with two packs, the other pack stays unchanged in the list.
- [ ] T006 [P] [US1] Create `C:\src\GameBot\tests\unit\Gamepacks\GamepackServiceRemoveTests.cs` with unit tests of `RemoveAsync`: removes the record and all owner entries of the pack (also an entry whose item does not exist), keeps owner entries of other packs, deletes the kept previous file, keeps take-over backup files, and does not touch the content stores.

**Checkpoint**: US1 works alone.

---

## Phase 4: User Story 2 - Install the same name again as a first install (Priority: P1)

**Goal**: After a removal, an install of the same name is a first install.

**Independent Test**: Install, remove, install the same name: no previous version and no update report.

- [ ] T007 [US2] Add contract tests to `C:\src\GameBot\tests\contract\Gamepacks\GamepackRemoveContractTests.cs`: (a) a new install of the same name after a removal is a first install with no previous version; (b) when an object of the removed pack still exists, the new install uses the user-object conflict rule (conflict, and take-over with `takeOver=true` works); (c) a rollback after a removal gives `404 not_found`; (d) a rollback of a pack with no kept previous version still gives `409 no_previous_version` (FR-009); (e) the kept previous version is gone after a removal of a pack that was updated once.
- [ ] T008 [P] [US2] Add a unit test to the existing `C:\src\GameBot\tests\unit\Gamepacks\GamepackValidatorConflictTests.cs` (not to the removal test class) that an item with no owner entry after a removal follows the user-object conflict rule (existing item, no owner entry gives the user-object rule).
- [ ] T009 [P] [US2] Add a unit test to the existing `C:\src\GameBot\tests\unit\Gamepacks\GamepackRestoreTakeOverTests.cs` that a restore of a take-over backup after a removal gives `owner_changed` (existing rule in `GamepackApplier.RestoreTakeOverAsync`, no code change).

**Checkpoint**: US1 and US2 work.

---

## Phase 5: User Story 3 - Clear error for an unknown gamepack (Priority: P2)

**Goal**: An unknown name or a repeat request gives `404`, never `500`.

**Independent Test**: Send the remove request for a name that is not installed: `404` with `result` `not_found`.

- [ ] T010 [US3] Add contract tests to `C:\src\GameBot\tests\contract\Gamepacks\GamepackRemoveContractTests.cs`: (a) unknown name gives `404` with `result` `not_found` and no data change; (b) a second `DELETE` of the same name gives `404`; (c) a name with other letter case matches the record; (d) an invalid name gives `404`; (e) `DELETE` to the fixed paths that exist (for example `keys/{fingerprint}`) still uses its own route (research D-005).
- [ ] T011 [P] [US3] Add unit tests to `C:\src\GameBot\tests\unit\Gamepacks\GamepackServiceRemoveTests.cs` with a store whose `Update` throws: the outcome is `Failed`, the record and owner entries stay as before (FR-007), and the kept previous file stays. Add a test that a failing `DeletePrevious` after a good write still gives `Removed`. Add a test that `RemoveAsync` waits for the gate while an install or rollback holds it (FR-008).

**Checkpoint**: All stories work.

---

## Phase 6: Polish and cross-cutting

- [ ] T012 Add `Produces` for 204, 404, and 500, a summary, and the description from `contracts\remove-gamepack.md` to the `DELETE {name}` mapping in `C:\src\GameBot\src\GameBot.Service\Endpoints\GamepacksEndpoints.cs`. Change the class summary comment that says no route uninstalls a gamepack.
- [ ] T013 [P] Add response examples for the 404 and 500 answers of `RemoveGamepack` in `ApplyGamepackExamples` in `C:\src\GameBot\src\GameBot.Service\Swagger\SwaggerConfig.cs`.
- [ ] T014 [P] Add an OpenAPI test to `C:\src\GameBot\tests\contract\Gamepacks\GamepackOpenApiTests.cs`: the document lists `DELETE /api/gamepacks/{name}` with the operation id `RemoveGamepack`, a summary, a description, and the status codes 204, 404, and 500 (FR-010).
- [ ] T015 [P] Search `C:\src\GameBot\docs\` for a list of gamepack routes. Add the new route in STE if a list exists (research D-009).
- [ ] T016 Run `dotnet build` and the gamepack unit tests and gamepack contract tests only. Confirm that all existing gamepack tests pass with no change (SC-005). Use the steps in `quickstart.md`.
- [ ] T017 Run `git diff --name-only origin/master` and confirm that the list has no file under `C:\src\GameBot\web-ui\` (FR-012). If a web-ui file is listed, revert it.
- [ ] T018 Before the PR push, bump the version in `C:\src\GameBot\installer\versioning\version.override.json` as the project instructions say (compare with `origin/master`, set `patch`, `updatedBy`, `updatedAtUtc`).

---

## Dependencies and order

- Phase 1 then Phase 2 (T002, then T003). Phase 2 blocks all stories.
- US1 (T004 needs T003). T005 needs T004 (route). T006 is a domain unit test and needs only T003, not T004. US2 and US3 contract tests need T004. US2 and US3 unit tests need only T003.
- T017 (web-ui check) runs after all code tasks and before T018 (version bump).
- US2 and US3 are independent of each other. Both extend files that US1 creates (T007 and T010 edit the same test file, so run them in order).
- Polish after the stories. T012 edits the same file as T004, so it runs after T004.

## Parallel examples

- After T003: T006 can start. After T004: T005 can run in parallel with T006 (different files).
- After T003: T008 and T009 are in other files, so they run in parallel with each other. T011 edits the same file as T006, so it runs after T006.
- Polish: T013, T014, and T015 in parallel.

## Implementation strategy

- MVP: Phases 1 to 3 (US1). It gives the route and the list check.
- Then US2 (first install after removal) and US3 (clear errors), then polish.
- Do not commit until the build and the gamepack tests pass.

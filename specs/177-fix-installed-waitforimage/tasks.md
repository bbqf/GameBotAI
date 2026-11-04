# Tasks: Fix inline WaitForImage in installed gamepack sequences

**Input**: Design documents in `C:\src\GameBot\specs\177-fix-installed-waitforimage\` (plan.md, spec.md, research.md, data-model.md, contracts/waitforimage-mapping.md, quickstart.md)
**Bug**: B-039, issue #373
**Tests**: Required. The bug-fix rule needs a failing reproduction test BEFORE the fix. Tests come before the code they test.

## Format

`- [ ] T### [P?] [Story?] Description with exact file path`

- [P] = the task can run in parallel (different file, no open dependency).
- [US1] = installed top-level step. [US2] = nested steps. [US3] = shared rules for save and install.
- Use absolute paths in all commands. Do not put `cd <dir>;` before a command.
- Use the Edit and Write tools for UTF-8 files.
- All new text (code comments, docs, messages) follows ASD-STE100 STE.

## Phase 1: Setup

- [X] T001 Run `dotnet build C:\src\GameBot\GameBot.sln` and the existing gamepack tests. Record that the base state is green. Create the folder `C:\src\GameBot\tests\unit\Gamepacks` if it does not exist.
- [X] T002 [P] Create the real pack JSON test fixture `C:\src\GameBot\tests\contract\Gamepacks\Fixtures\waitforimage-pack.json`. It holds: one top-level WaitForImage step (timeoutMs 3000, referenceImageId "pns-city-anchor", confidence 0.85); one WaitForImage step in a loop Body; one in an if Body; one in an if ElseBody; one three-level nest (loop in if in loop); one step with `until` "absent" and `absentCaptures`; one step with a valid `region`; one non-image step (for example WaitForStill). Mark the file as copy-to-output if the test project needs it (check how other fixtures in `C:\src\GameBot\tests\contract\Gamepacks` are loaded).

## Phase 2: Foundational (reproduction test and type check)

**Rule**: T003 and T004 MUST be written and shown to fail or to give facts BEFORE any fix task in Phase 3 starts.

- [X] T003 Write the failing reproduction test class `C:\src\GameBot\tests\contract\Gamepacks\GamepackWaitForImageInstallContractTests.cs`. Test `Install_InlineWaitForImage_StoresConfiguration`: install the fixture from T002 with a new install, read the stored sequence from the repository, and assert the top-level step has `WaitForImage` not null, `TimeoutMs` 3000, and `DetectionTarget.ReferenceImageId` "pns-city-anchor". Run it with `dotnet test`. Record that it FAILS (the `WaitForImage` value is null). Depends on T002.
- [X] T004 [P] Write the runtime type test `C:\src\GameBot\tests\unit\Gamepacks\WaitForImagePayloadTypeTests.cs`. Read the fixture from T002 with `GamepackFileSerializer` (the stored form `action.parameters` and the API form `primitiveAction.payload`). Assert the runtime type of `Parameters["detectionTarget"]`, `["timeoutMs"]`, `["until"]`, and `["absentCaptures"]`. Expected type: `JsonElement`. Write the real type in the test as the guard. If the type is not `JsonElement`, record it in `C:\src\GameBot\specs\177-fix-installed-waitforimage\research.md` (decision 3) and tell T007 to convert the value to `JsonElement` before reading. Depends on T002.

**Checkpoint**: T003 fails for the right reason. T004 shows the real type.

## Phase 3: User Story 1 - Installed inline WaitForImage waits for the image (P1)

**Goal**: A top-level inline WaitForImage step of an installed sequence has the correct configuration in all install modes.
**Independent test**: T003 passes. The take-over and update tests pass.

### Tests first (write and show them fail before T007 to T011)

- [X] T005 [P] [US1] Write the mapper unit tests `C:\src\GameBot\tests\unit\Gamepacks\WaitForImageConfigMapperTests.cs`: default timeout 1000; `until`; `absentCaptures`; confidence default 0.8; offsets default 0; `FirstMatch` strategy; valid region kept; invalid region gives null region and no throw; missing image id gives null detection target; wrong action type gives null; null payload gives null; value types `int`, `long`, `JsonElement`, numeric `string`; the type found in T004. Depends on T004.
- [X] T006 [P] [US1] Write the reader move-guard tests `C:\src\GameBot\tests\unit\Gamepacks\WaitForImageReadersTests.cs` for `WaitForImageConfigMapper.TryReadInt32` and `TryReadRegion`: same results and same error texts as the old `SequencesEndpoints` methods (`"region must be an object"`, `"region.x must be a whole number"`, missing field stays null, no region gives true and null). Depends on T004.
- [X] T007 [P] [US1] Write the install contract tests in `C:\src\GameBot\tests\contract\Gamepacks\GamepackWaitForImageInstallContractTests.cs` (same class as T003): (a) install with takeOver: the stored content hash equals the `PostHash` and a second dry run reports no change; (b) update: install an old-form sequence (no `waitForImage`, as the bug stored it), then update the pack, then assert the stored step has the configuration, also when the old content looked equal to the pack payload; (c) missing reference image on a WaitForImage step: the install result (problems and status) equals the result before the fix (record the expected values from the current behavior); (d) other step types and commands are unchanged. Depends on T003.

### Implementation

- [X] T008 [US1] Create `C:\src\GameBot\src\GameBot.Domain\Commands\WaitForImageConfigMapper.cs` with `Map`, `TryReadInt32`, `TryReadRegion`, and the `RegionFields` record, per `C:\src\GameBot\specs\177-fix-installed-waitforimage\contracts\waitforimage-mapping.md`. MOVE the logic from `MapWaitForImageConfig`, `MapWaitForImageDetectionTarget`, `TryReadInt32`, and `TryReadRegion` in `C:\src\GameBot\src\GameBot.Service\Endpoints\SequencesEndpoints.cs`. Keep error texts. Keep each method under 50 lines (split the detection target reader). Accept the runtime type from T004. Depends on T005, T006.
- [X] T009 [US1] Change `C:\src\GameBot\src\GameBot.Service\Endpoints\SequencesEndpoints.cs`: the two Map call sites (top-level and nested steps) call `WaitForImageConfigMapper.Map`; `CollectRegionErrors` calls `WaitForImageConfigMapper.TryReadRegion`, wraps the numbers in `PixelRegionDto`, and calls `PixelRegionDto.Validate` (messages unchanged). Remove `MapWaitForImageConfig`, `MapWaitForImageDetectionTarget`, `TryReadInt32`, and the body of `TryReadRegion`. Search `C:\src` to confirm no other caller of the removed methods. Depends on T008.
- [X] T010 [US1] Create `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackWaitForImageMapping.cs` with `Apply(CommandSequence)` per the contract. The first version walks the steps. Depends on T008, T013 (walk tests, see Phase 4).
- [X] T011 [US1] Change `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackValidator.cs`: call `GamepackWaitForImageMapping.Apply(sequence)` right after `GamepackReferences.RewriteCommandIds(sequence, Map)` and before the sequence `PlannedObject` is added. Do not change `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackApplier.cs` or the runner. Depends on T010.
- [X] T012 [US1] Run `dotnet test` for the tests of T003, T005, T006, T007 and the existing feature 130 save-path region tests. All MUST pass. Depends on T009, T011.

**Checkpoint**: US1 works for a new install, take-over, and update.

## Phase 4: User Story 2 - Nested inline WaitForImage steps (P1)

**Goal**: Nested steps at every depth get the configuration.
**Independent test**: The walk unit tests and the nested contract test pass.

- [X] T013 [P] [US2] Write the walk unit tests `C:\src\GameBot\tests\unit\Gamepacks\GamepackWaitForImageMappingTests.cs`: top-level step; loop Body; if Body; if ElseBody; three levels (loop in if in loop); step with no action is skipped with no failure; other step types unchanged; an existing `waitForImage` value from the file is replaced by the payload value; null sequence throws `ArgumentNullException`. These tests MUST fail before T010. Depends on T008 (types exist), and T004.
- [X] T014 [P] [US2] Add the nested install contract test to `C:\src\GameBot\tests\contract\Gamepacks\GamepackWaitForImageInstallContractTests.cs`: install the fixture from T002 and assert each nested step (loop Body, if Body, if ElseBody, three-level nest) has the timeout and image id from the pack. Depends on T003.
- [X] T015 [US2] Run the tests of T013 and T014 after T011. All MUST pass. Depends on T011, T013, T014.

**Checkpoint**: US1 and US2 are complete.

## Phase 5: User Story 3 - Save and install use the same rules (P2)

**Goal**: One shared mapping. Proof by comparing both stored steps.
**Independent test**: The parity test passes.

- [X] T016 [P] [US3] Write the parity contract test class `C:\src\GameBot\tests\contract\Gamepacks\GamepackWaitForImageParityContractTests.cs`. Send one payload (timeout, image id, confidence, offsets, strategy, until, absentCaptures, region) through a pack install (real pack file) and through `POST /api/sequences`. Read both stored sequences. Compare `action` and `waitForImage` field by field (all `DetectionTarget` fields including `Region`). Add a second case for a nested step. A test that compares the mapper with itself does not count. Depends on T002.
- [X] T017 [US3] Run the parity test after T011. It MUST pass with 0 differences. Search `C:\src\GameBot\src` and confirm that only `WaitForImageConfigMapper` reads `timeoutMs`, `absentCaptures`, `detectionTarget`, and `region` payload fields (FR-005d). Depends on T011, T016.

## Phase 6: Polish and living documents

- [X] T018 [P] Check the contract test shard balance. Run `C:\src\GameBot\scripts\ci\measure-shards.ps1` (absolute path). If the new contract test classes break the balance rule, update `C:\src\GameBot\scripts\ci\test-shards.json`. Depends on T017.
- [X] T019 [P] Update `C:\src\GameBot\specs\STATUS.md`: add row `| 177 | Fix Installed WaitForImage | Implemented |` after row 176. Depends on T017.
- [X] T020 [P] Set the `Status` line in `C:\src\GameBot\specs\177-fix-installed-waitforimage\spec.md` to `Implemented`. Depends on T017.
- [X] T021 [P] Review `C:\src\GameBot\docs\architecture.md` (text on WaitForImage and gamepack install). If the text changes, refresh "Last reviewed". If no change is needed, still refresh "Last reviewed". Depends on T017.
- [X] T022 Run the full build and the full test suite: `dotnet build C:\src\GameBot\GameBot.sln` and `dotnet test C:\src\GameBot\GameBot.sln`. Existing tests for commands, WaitForStill, and the save endpoint MUST pass (SC-005). Depends on T018, T019, T020, T021.
- [X] T023 Version bump (constitution, "Version bump"): run `git fetch origin master`. Compare `C:\src\GameBot\installer\versioning\version.override.json` with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`. Depends on T022.
- [ ] T024 (Optional) Manual run for SC-002: install the pack, run the installed sequence with the image on screen, and check the step log shows exitCondition "image_detected", effectiveTimeoutMs 3000, and the image id. Steps are in `C:\src\GameBot\specs\177-fix-installed-waitforimage\quickstart.md`. The stored-configuration tests already prove SC-002, so this task is not a gate. Depends on T022.

## Dependencies (strict order)

1. T001 before all other tasks.
2. T002 before T003, T004, T014, T016.
3. T003 and T004 (Phase 2) before every task in Phases 3 to 6.
4. T003 MUST fail before T008 starts.
5. T005 and T006 before T008 (tests before the mapper).
6. T013 before T010 (tests before the walk). T008 before T013 compiles, so write T013 against the planned signatures and run it after T008.
7. T007 and T014 before T011 (install tests before the call site).
8. T008 before T009 and T010. T010 before T011.
9. T009 and T011 before T012. T011, T013, T014 before T015. T011 and T016 before T017.
10. T017 before T018, T019, T020, T021. These four can run in parallel.
11. T018, T019, T020, T021 before T022. T022 before T023 and T024.

## Parallel examples

- After T002: T003 and T004 together; later T016 can start.
- After T004: T005, T006, T013 (write), T007, T014 together (different files or different methods; T007 and T014 share one class, so merge by hand).
- After T017: T018, T019, T020, T021 together.

## Implementation strategy

- MVP = Phases 1 to 4 (US1 and US2): the fix is one mapper, one walk, and one call. Both stories need the same code, so deliver them together.
- US3 (parity proof and the single-reader rule) closes the work and guards against a return of the bug.
- Do the work in this order: type check and failing test, then mapper tests, then mapper, then walk tests, then walk, then call site, then proof and docs.
- Do not commit until the user asks.

## Counts

- Total tasks: 24 (T024 optional).
- Setup: 2. Foundational: 2. US1: 8 (T005 to T012). US2: 3 (T013 to T015). US3: 2 (T016, T017). Polish: 7 (T018 to T024).

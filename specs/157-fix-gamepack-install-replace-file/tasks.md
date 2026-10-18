# Tasks: Fix gamepack install file replace failure

**Input**: Design documents in `C:\src\GameBot\specs\157-fix-gamepack-install-replace-file\` (spec.md, plan.md, research.md, data-model.md, contracts\install-error.md, quickstart.md)
**Tests**: Required. FR-007, FR-011, FR-012 and SC-004 need tests. The root cause test must fail without the fix.
**CI system**: GitHub Actions on `windows-latest` (see `C:\src\GameBot\.github\workflows\dotnet.yml`). Tests that need Windows lock behavior skip on other systems and run on CI.

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- [P] = can run in parallel (different files, no open dependency). A task that shares a file with another task is never [P].
- [US1], [US2], [US3] = user story label from spec.md.
- Error text (FR-008): `Cannot replace file '<full path>' for <kind> '<id>': <reason>`. `<kind>` is sequence, command, image, template, registry or previous.
- Unknown-state entry (FR-009): `<kind>:<id> (<full file path>)`, for example `image:abc (C:\data\images\abc.png)`.
- Test-first: a test task marked [P] can be written in parallel with the code, but it is red until its code task is done. This is expected.

## Phase 1: Setup

- [X] T001 Re-check the write coverage (FR-008a). Search `C:\src\GameBot\src\GameBot.Domain` for `WriteAtomic` and `File.Replace`, and `C:\src\GameBot\src\GameBot.Service\Endpoints\GamepacksEndpoints.cs` for `WriteFailed`. Confirm this list: image save, alternates save, `GamepackRegistryStore` `Save`, `Update` (kind `registry`) and `WritePrevious` (kind `previous`). List every `WriteFailed` call site (about lines 180, 194, 235, 245 and any other). Add each new write or call site that you find to the tasks below. Do not change `Triggers\Evaluators\ReferenceImageStore.cs` or `Logging\AtomicFileWriter.cs`.
- [X] T002 Confirm the CI system. Read `C:\src\GameBot\.github\workflows\dotnet.yml` and check that the test jobs use `windows-latest`. Make sure the Windows-only tests below use a skip guard for other systems (FR-012).

## Phase 2: Foundational (blocks all stories)

- [X] T003 Create the helper `AtomicFile.ReplaceFile(tempPath, targetPath, kind, id)` in `C:\src\GameBot\src\GameBot.Domain\Utils\AtomicFile.cs` (new file; reason for a new helper is in plan.md and research.md D-001, FR-013). Use `File.Move(temp, target, overwrite: true)`. Retry only for sharing violation (Win32 32) and lock violation (Win32 33): 5 tries in total, waits 50, 100, 200, 400, 800 ms. Other errors (access denied, read-only, path not found) do not retry. On final failure delete the temp file and throw an `IOException` with the FR-008 text. Keep the inner exception. Add XML docs in STE.
- [X] T004 [P] Write unit tests for the helper in `C:\src\GameBot\tests\unit\Utils\AtomicFileTests.cs` (new file; depends on T003): target is new; target exists; target held with `FileShare.Read` and released after 300 ms (retry works, Windows only); target held for the whole test (message equals the FR-008 text with the full path, temp file is gone, inner exception kept); read-only target fails at once without a retry (elapsed time below 50 ms) and names the file (FR-010, FR-012).
- [X] T005 [P] Add a read-only path method that gives the full image file path for an image id (no side effects, FR-009) in `C:\src\GameBot\src\GameBot.Domain\Images\ImageRepository.cs`. Add it to the interface only if the applier uses the interface.
- [X] T006 [P] Add a read-only path method that gives the full alternates file path for an image id in `C:\src\GameBot\src\GameBot.Domain\Images\ImageAlternatesRepository.cs` (FR-009).
- [X] T007 [P] Add a read-only path method that gives the full sequence file path for a sequence id in `C:\src\GameBot\src\GameBot.Domain\Commands\FileSequenceRepository.cs` and, if the applier uses it, in `ISequenceRepository.cs` (FR-009).
- [X] T008 [P] Add a read-only path method that gives the full command file path for a command id in `C:\src\GameBot\src\GameBot.Domain\Commands\FileCommandRepository.cs` and, if the applier uses it, in `ICommandRepository.cs` (FR-009).
- [X] T009 [P] Add a read-only path method that gives the full template file path for a template id in `C:\src\GameBot\src\GameBot.Domain\QueueTemplates\FileQueueTemplateRepository.cs` and, if the applier uses it, in `IQueueTemplateRepository.cs` (FR-009).
- [X] T010 Add read-only path members in `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackRegistryStore.cs` and `IGamepackRegistryStore.cs`: the registry path (`RegistryPath`) and a method that gives the previous-file path for a pack name (FR-009). Kinds are `registry` and `previous`; the id is the pack name.

**Checkpoint**: the helper exists, its tests pass, and every store can give the full path of its install files.

## Phase 3: User Story 1 - Install a valid gamepack with take-over (P1)

**Goal**: A real install with take-over succeeds on Windows and replaces every taken-over object (259 of 259).
**Independent Test**: Install a 259-object pack with take-over over the same 259 existing objects. The install succeeds and all four kinds hold the pack content.

- [X] T011 [P] [US1] Write the root cause test in `C:\src\GameBot\tests\unit\Images\ImageRepositoryReplaceLockTests.cs` (new file): hold an existing image file open with `FileShare.Read` on a worker thread, release it after 300 ms, then call `SaveAsync(overwrite: true)`. Expect the new content on disk. Skip on non-Windows. Run it and confirm that it fails before T014 (FR-007, SC-004).
- [X] T012 [P] [US1] Write the same lock-and-release test for `SetAlternates` in `C:\src\GameBot\tests\unit\Images\ImageAlternatesReplaceLockTests.cs` (new file). Confirm that it fails before T015.
- [X] T013 [P] [US1] Write the same lock-and-release test for the registry writes in `C:\src\GameBot\tests\unit\Gamepacks\GamepackRegistryStoreReplaceLockTests.cs` (new file). Cover `Save`, `Update` and `WritePrevious`. Confirm that it fails before T016.
- [X] T014 [US1] Replace `File.Replace` with `AtomicFile.ReplaceFile(temp, target, "image", id)` in `C:\src\GameBot\src\GameBot.Domain\Images\ImageRepository.cs` (`SaveAsync`, about line 130). Keep the new-file `File.Move` path. Same file as T005, so after T005. Depends on T003, T011.
- [X] T015 [US1] Replace `File.Replace` with `AtomicFile.ReplaceFile(..., "image", id)` in `C:\src\GameBot\src\GameBot.Domain\Images\ImageAlternatesRepository.cs` (`SetAlternates`, about line 77). Same file as T006, so after T006. Depends on T003, T012.
- [X] T016 [US1] Change `WriteAtomic` in `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackRegistryStore.cs` (about line 122) to take a kind and an id and call `AtomicFile.ReplaceFile`. `Save` and `Update` pass kind `registry`. `WritePrevious` passes kind `previous`. The id is the pack name. Same file as T010, so after T010. Depends on T003, T013.
- [X] T017 [US1] Add the full-scale test in `C:\src\GameBot\tests\integration\Gamepacks\GamepackTakeOverScaleTests.cs` (new file). Depends on T014, T015, T016 (the writer tasks). Install a synthetic pack of 259 objects (images, sequences, commands, templates) with take-over on top of the same 259 existing objects. Assert success and the pack content for all four kinds. Assert that the counts match and no stale or duplicate object remains. Assert that the dry run and the real install agree (FR-011, FR-002, SC-001).
- [X] T018 [US1] Add the mixed test to the same file `C:\src\GameBot\tests\integration\Gamepacks\GamepackTakeOverScaleTests.cs` (after T017, not parallel): one install with new and existing objects together succeeds (FR-012).

**Checkpoint**: T004, T011, T012, T013, T017 and T018 pass. US1 works alone.

## Phase 4: User Story 2 - Clear error when a file cannot be replaced (P1)

**Goal**: The error response names the file, the object kind and the id (FR-003, FR-008).
**Independent Test**: Block one target file for the whole install. The error names that file. Other objects stay as before.

- [X] T019 [US2] Add "file held for the whole test" tests to `C:\src\GameBot\tests\unit\Images\ImageRepositoryReplaceLockTests.cs` (same file as T011, so after it). Assert that the `IOException` message equals the FR-008 text with the full path and kind `image`, and that the temp file is gone. Depends on T014.
- [X] T020 [US2] Add the same test for `SetAlternates` to `C:\src\GameBot\tests\unit\Images\ImageAlternatesReplaceLockTests.cs` (same file as T012, so after it). Depends on T015.
- [X] T021 [US2] Add "file held for the whole test" tests to `C:\src\GameBot\tests\unit\Gamepacks\GamepackRegistryStoreReplaceLockTests.cs` (same file as T013, so after it). Assert the FR-008 text with the full path for `Save` and `Update` (kind `registry`) and for `WritePrevious` (kind `previous`), with the pack name as id (FR-008a). Depends on T016.
- [X] T022 [US2] In `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackApplier.cs` `WriteObjectAsync`, catch `IOException` and `UnauthorizedAccessException` for sequences, commands and templates (they use `FileMode.Create`) and throw an `IOException` with the FR-008 text (full path from the T007 to T009 methods). Image errors from the helper pass through unchanged: no second prefix (FR-008). Depends on T007, T008, T009, T014, T015, T016. Use STE for the text.
- [X] T023 [P] [US2] Add tests in `C:\src\GameBot\tests\unit\Gamepacks\GamepackApplierWriteErrorTests.cs` (new file): a repository stub throws for one object of each kind (sequence, command, image, template). The result is `failed`, the message equals the FR-008 text, and all other objects hold their old content. Passes after T022.
- [X] T024 [P] [US2] Add the single-prefix test in `C:\src\GameBot\tests\unit\Gamepacks\GamepackApplierImagePrefixTests.cs` (new file): an image write failure through the applier gives an error message with exactly one `Cannot replace file` prefix (no double wrapping, FR-008). Passes after T022.
- [X] T025 [P] [US2] Add a read-only target install test in `C:\src\GameBot\tests\unit\Gamepacks\GamepackApplierReadOnlyTargetTests.cs` (new file): one read-only target file. The install fails at once, the message names the file, and the other objects keep the old content (FR-012). Passes after T022.
- [X] T026 [US2] Add a contract test in `C:\src\GameBot\tests\contract\Gamepacks\GamepackInstallWriteErrorContractTests.cs` (new file): a forced write failure returns 500, `result` is `failed`, and `detail` has the FR-008 text. Depends on T022.
- [X] T027 [US2] Add to the same file `C:\src\GameBot\tests\contract\Gamepacks\GamepackInstallWriteErrorContractTests.cs` (after T026, not parallel): a write failure with a working restore gives `failed` and all objects hold their old content (FR-012, US3 scenario 1).

**Checkpoint**: US2 tests pass.

## Phase 5: User Story 3 - Report files in an unknown state (P2)

**Goal**: A failed restore lists every file in an unknown state (FR-005, FR-009).
**Independent Test**: Force a write failure and a restore failure for one key. The response lists that key.

- [X] T028 [P] [US3] Add tests in `C:\src\GameBot\tests\unit\Gamepacks\GamepackApplierRestoreFailureTests.cs` (new file): write and restore fail for one key (result `partial_restore`; `FailedRestoreKeys` has that key in the FR-009 format); restore fails for two keys (both listed); one test for each kind, including `registry` and `previous`. Use the fault-injection pattern of `GamepackApplierFaultInjectionTests.cs`. Passes after T029 and T030.
- [X] T029 [US3] In `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackApplier.cs`, build each failed restore entry in the FR-009 format `<kind>:<id> (<full file path>)`. Get the path from the store that wrote the file (T005 to T010). Same file as T022, so after T022. Keep the result names.
- [X] T030 [US3] In `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackApplier.TakeOver.cs`, build the failed restore entries of the take-over restore in the same FR-009 format. Search for every place that adds to the failed restore keys. Depends on T029.
- [X] T031 [US3] In `C:\src\GameBot\src\GameBot.Service\Endpoints\GamepacksEndpoints.cs`, change `WriteFailed` (about line 349) to accept `failedRestoreKeys` and add it as the extension member `failedRestoreKeys` for `partial_restore`. Re-check every `WriteFailed` call site found in T001 (install, rollback, take-over restore, catalog install; about lines 180, 194, 235, 245): each one MUST carry the key list and the message. Check whether the route at about line 245 can give `partial_restore`. Keep the result name and HTTP status 500. Depends on T029, T030.
- [X] T032 [US3] Add a contract test in `C:\src\GameBot\tests\contract\Gamepacks\GamepackPartialRestoreContractTests.cs` (new file): a `partial_restore` outcome returns 500, `result` is `partial_restore`, and a `failedRestoreKeys` list in the FR-009 format. Cover every route that calls `WriteFailed`. Depends on T031.
- [X] T033 [US3] Describe `failedRestoreKeys` in the OpenAPI schema filter for the gamepack responses (find the filter that `GamepackTakeOverOpenApiTests.cs` uses, in `C:\src\GameBot\src\GameBot.Service`). Add the assertion in a new test `C:\src\GameBot\tests\contract\Gamepacks\GamepackPartialRestoreOpenApiTests.cs`. Depends on T031.

**Checkpoint**: US3 tests pass.

## Phase 6: Polish and cross-cutting

- [X] T034 [P] Update `C:\src\GameBot\docs\architecture.md`: persistence note (image, alternates and registry writes use move-with-replace and a retry) and the "Last reviewed" date.
- [X] T035 Re-check the coverage again (FR-008a): search for `File.Replace`, `WriteAtomic` and `WriteFailed`. Confirm that no gamepack install write or response is missed. Fix any gap.
- [X] T036 Run the build and the Gamepack, Image and Utils test sets. Fix failures. Run the steps in `quickstart.md`. Depends on all earlier tasks.
- [X] T037 Update the status (FR-014), after T036 passes: set the `Status` line in `C:\src\GameBot\specs\157-fix-gamepack-install-replace-file\spec.md` (from `Draft` to the done state) and update the feature entry in `C:\src\GameBot\specs\STATUS.md`.
- [X] T038 Bump the version in `C:\src\GameBot\installer\versioning\version.override.json` before the push (run `git fetch origin master`, compare with `origin/master`, apply the patch rule from CLAUDE.md, set `updatedBy` and `updatedAtUtc`).

## Dependencies

- T001 and T002 first. T003 blocks all code tasks. T004 follows T003.
- Foundational path methods: T005 to T009 run in parallel (different files). T010 is one file and is not parallel.
- US1: T011 to T013 first (must fail). Then T014 (after T005, T011), T015 (after T006, T012), T016 (after T010, T013). T017 needs T014, T015, T016. T018 follows T017 (same file).
- US2: T019 follows T011 and T014. T020 follows T012 and T015. T021 follows T013 and T016. T022 needs T007 to T009 and T014 to T016. T023, T024, T025 are written first and pass after T022. T026 follows T022. T027 follows T026.
- US3: T029 follows T022 (same file). T030 follows T029. T031 follows T029 and T030. T032 and T033 follow T031. T028 passes after T029 and T030.
- Polish: T035 after all stories. T036 after T035. T037 after T036. T038 last.

## Parallel example

- After T003: T004 and T005 to T009 run together (different files).
- T011, T012, T013 run together (different new files).
- T023, T024, T025, T028 run together (different new files).
- T032 and T033 run together after T031.

## Implementation strategy

- MVP = Phase 1 to Phase 3 (US1): the helper plus the three writers fix the install on Windows (issue #333).
- Then US2 (the error names the file), then US3 (the unknown-state list).
- Total: 38 tasks.

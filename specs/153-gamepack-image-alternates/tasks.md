# Tasks: Gamepack image carries its alternates list

**Input**: Design documents in `C:\src\GameBot\specs\153-gamepack-image-alternates\` (plan.md, spec.md, research.md, data-model.md, contracts\gamepack-image-alternates.md)
**Tests**: Required (spec FR-008, plan Principle II). Write each test before its code. Run it and see it fail.

**Format**: `- [ ] Txxx [P?] [Story?] Description with file path`. `[P]` means a different file and no dependency on an open task. Tasks that share a file have no `[P]`.

Paths: Domain = `C:\src\GameBot\src\GameBot.Domain\Gamepacks\`, Service = `C:\src\GameBot\src\GameBot.Service\`, Tests = `C:\src\GameBot\tests\unit\Gamepacks\`.

## Phase 1: Setup

- [ ] T001 Run a baseline build and the gamepack unit tests in `C:\src\GameBot\tests\unit` to see that they pass before any change.
- [ ] T002 Read `Domain\GamepackApplier.cs`, `Domain\GamepackApplier.TakeOver.cs`, `Domain\TakeOver\TakeOverBackup.cs`, `Domain\GamepackFileSerializer.cs`, `Domain\GamepackValidator.cs`, and `Tests\GamepackTestKit.cs`. Find the image `Snapshot`, `WriteObjectAsync`, `RestoreObjectAsync`, `CaptureLiveAsync`, `ToBackupEntry`, `ToOldSnapshot`, and the image read in `Parse`.

## Phase 2: Foundational (blocks all stories)

- [ ] T003 Add the optional member `Alternates` (list of text, null when absent, JSON name `alternates`) with an XML doc to `GamepackImage` in `Domain\GamepackFile.cs`.
- [ ] T004 Add the optional member `Alternates` (list of text, omitted when null or empty) to `TakeOverBackupImage` in `Domain\TakeOver\TakeOverBackup.cs`. An old backup file has no member and reads as no list. Do not change `PostHash`.
- [ ] T005 Add an optional `IImageAlternatesRepository` parameter to the `GamepackApplier` constructor in `Domain\GamepackApplier.cs`. Keep existing callers valid (the default is null, and null means no alternates handling).
- [ ] T006 In `Service\Endpoints\GamepackServiceCollectionExtensions.cs`, pass the `IImageAlternatesRepository` service to `GamepackApplier` in the DI registration.
- [ ] T007 Add a fake `IImageAlternatesRepository` and helpers to `Tests\GamepackTestKit.cs`: a way to build an applier with the fake, a way to build an image entry with an `alternates` list, and a way to read the stored list.

**Checkpoint**: The solution builds. All old tests pass.

## Phase 3: User Story 1 - Install an image with its alternates (P1)

**Goal**: An install stores the declared list. Undo, removal, take-over restore, and capture handle the list.
**Independent test**: Install a gamepack with one image that has `"alternates":["<other-id>"]`. Read the stored list. It must match.

All tests of this phase go in one file, so they have no `[P]`.

- [ ] T008 [US1] Write test: dry run of an image with a list of known ids is valid. Put it in `Tests\GamepackApplierImageAlternatesTests.cs` (new file).
- [ ] T009 [US1] Write test: install stores the declared list in the declared order. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T010 [US1] Write test: an alternates id names another image of the same gamepack and the install keeps it. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T011 [US1] Write test: a new image with no `alternates` member gets an empty stored list. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T012 [US1] Write test (FR-007): a re-install of a file with no `alternates` member leaves the stored list and the image bytes unchanged. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T013 [US1] Write test: a re-install with a present empty list clears the stored list, and a re-install with a changed list updates it (also when the bytes are unchanged). Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T014 [US1] Write test: when the apply fails after an image is written, the undo puts back the old stored list (or removes the list for a new image). Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T015 [US1] Write test: an image that the install removes also loses its stored list. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T016 [US1] Write test: a take-over of an image writes the declared list, and a restore of the take-over backup puts back the old list of the user (or removes the list when it was empty). Check the backup file holds `alternates` only when the list is not empty. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T017 [US1] Write test: `CaptureLiveAsync` writes the stored list into the captured image only when the list is not empty, and a capture with no lists gives the same bytes as before. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T018 [US1] Add `ImageAlternates` (list of text) to the image `Snapshot`. In `TakeSnapshotAsync`, read it with `GetAlternates` in `Domain\GamepackApplier.cs`.
- [ ] T019 [US1] In `WriteObjectAsync` for an image in `Domain\GamepackApplier.cs`, call `SetAlternates` right after `SaveAsync` when the declared list is not null. Use an empty list to clear. Do nothing when the member is absent.
- [ ] T020 [US1] In `RestoreObjectAsync` and in the image removal path in `Domain\GamepackApplier.cs`, restore the snapshot list with `SetAlternates` (empty list removes the sidecar file). Call `DeleteAlternates` on removal and on undo of a new image.
- [ ] T021 [US1] In `CaptureLiveAsync` in `Domain\GamepackApplier.cs`, write the stored list into the captured image only when it is not empty.
- [ ] T022 [US1] In `Domain\GamepackApplier.TakeOver.cs`, update `ToBackupEntry` (write the list when not empty), `ToOldSnapshot` (read the list), and the image snapshot for a take-over so that the old list is kept and restored.
- [ ] T023 [US1] Run the tests of T008 to T017 and fix until they pass.

**Checkpoint**: US1 works alone for valid input.

## Phase 4: User Story 2 - Refuse an unknown image member (P1)

**Goal**: An unknown image member is an error at dry run, and the install is refused.
**Independent test**: Dry run a gamepack with an image that has `"bogusMember":1`. The result is not valid and the message names the image id and the member.

- [ ] T024 [P] [US2] Write parse tests in `Tests\GamepackFileSerializerImageMembersTests.cs` (new file): an unknown member gives a Structure problem of type Image that names the image id and the member; known members (any case) are accepted; an entry with no id or that is not an object uses the position fallback name; `alternates` that is not a list, or has a non-text item, names the image.
- [ ] T025 [US2] Write test: install of a file with an unknown image member is refused and creates zero objects. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T026 [US2] In `Parse` in `Domain\GamepackFileSerializer.cs`, compare the member names of each image entry (without case) with `id`, `contentType`, `filename`, `dataBase64`, `alternates`. Report each unknown name as a Structure problem of type Image with the image id, the member, and the known members. Add the wrong-shape check for `alternates`.
- [ ] T027 [US2] Run the tests of T024 and T025 and fix until they pass.

**Checkpoint**: US1 and US2 both work.

## Phase 5: User Story 3 - Report a bad alternates list at dry run (P2)

**Goal**: The dry run applies the existing alternates rules. The install is refused on a violation.
**Independent test**: Dry run gamepacks with a self reference, an unknown id, and a duplicate id. Each gives an error with the image id.

- [ ] T028 [P] [US3] Write tests in `Tests\GamepackValidatorImageAlternatesTests.cs` (new file): self reference; unknown id; duplicate id; more than 8 ids (no `Ids:` part); bad id form; id of a stored image that the install removes is unknown; id of a stored image that is kept is valid; empty list is valid. Check the message form `Image '<id>': <message> Ids: <ids>. <hint>`.
- [ ] T029 [US3] Write test: install of a file with an invalid alternates list is refused and creates zero objects. Put it in `Tests\GamepackApplierImageAlternatesTests.cs`.
- [ ] T030 [US3] Add a `CheckAlternates` step to `Domain\GamepackValidator.cs`. For each image with a list, call `ImageAlternatesValidator.Validate(id, list, exists)`. The `exists` function covers the image ids of the file and the stored ids that the install keeps. Report a Structure problem of type Image whose name is the image id and whose message is built as in research.md R8.
- [ ] T031 [US3] Run the tests of T028 and T029 and fix until they pass.

**Checkpoint**: All three stories work.

## Phase 6: Polish and cross-cutting

- [ ] T032 [P] Update the image member text in `Service\Swagger\GamepackSchemaFilter.cs` (OpenAPI text) so that it lists `alternates` and says an unknown member is an error. Use STE.
- [ ] T033 [P] Update `C:\src\GameBot\docs\gamepacks.md`: the `alternates` image member, the unknown member error, the absent versus empty rule, and the take-over backup member. Name `C:\src\GameBot\specs\143-gamepack-install\spec.md` as the install spec that this feature extends. Use STE.
- [ ] T034 [P] Add a line under `[Unreleased]` in `C:\src\GameBot\CHANGELOG.md` for 153-gamepack-image-alternates (a gamepack image carries its alternates list, and an unknown image member is now an error). Use STE.
- [ ] T035 Bump the version in `C:\src\GameBot\installer\versioning\version.override.json`: run `git fetch origin master`, compare with `origin/master`; set `patch` to the `origin/master` patch plus 1 (or `"0"` when `minor` or `major` changed); set `updatedBy` and `updatedAtUtc`. Do nothing if the value is already right. Do this before the push.
- [ ] T036 Run the full build and all unit, contract, and integration tests for gamepacks. All must pass.
- [ ] T037 Run the steps in `C:\src\GameBot\specs\153-gamepack-image-alternates\quickstart.md` to check the result.

## Dependencies and order

- Phase 1, then Phase 2, then the stories. Phase 2 blocks all stories.
- US1 (T008 to T023) has no dependency on US2 or US3. T018 to T022 all edit `GamepackApplier*.cs`, so do them in order.
- US2 and US3 depend on Phase 2 only. T025 and T029 need the applier test file from T008.
- T030 (validator) is a precondition for a valid-list check in T008; if T008 fails for that reason, do T030 first. Run T023 after T030.
- Polish (Phase 6) follows all stories. T035 comes last before the push.

## Parallel examples

- After Phase 2: T024 (serializer tests) and T028 (validator tests) run in parallel, because they use different files.
- In Phase 6: T032, T033, and T034 run in parallel.

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1 (store the list with undo, take-over restore, and capture). Then add US2 (refuse unknown member, required by the issue), then US3 (rule reports).
- Deliver in the order US1, US2, US3, Polish. Run the tests after each phase.

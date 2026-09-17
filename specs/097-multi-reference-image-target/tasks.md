# Tasks: Alternate Reference Images for One Detection Target

**Input**: Design documents from `specs/097-multi-reference-image-target/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/image-alternates.md, quickstart.md

**Tests**: Required by the constitution (Principle II) — tests are written first and must fail before
the implementation task that satisfies them.

**Quality gate between phases**: `dotnet build C:\src\GameBot\GameBot.sln -c Debug` and the touched test
projects must be green before a phase is marked complete.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Confirm a green baseline: build the solution and run `tests/unit` filtered to `Vision|Image|Backup` and `tests/integration` filtered to `ImageDetections|MaskedDetection|Backup`; record any pre-existing failures in the PR notes (no code change)

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The persisted alternates list, the set loader and the matcher decorator — every story depends on these.

- [X] T002 [P] Write failing unit tests for `FileImageAlternatesRepository` (round-trip order preserved, empty list deletes the sidecar, `Delete` removes it, absent file reads as empty, invalid primary id rejected, sidecar folder never listed by `FileImageRepository.ListIdsAsync`, replace is atomic via `.tmp`) in tests/unit/Images/FileImageAlternatesRepositoryTests.cs
- [X] T003 [P] Write failing unit tests for `ImageAlternatesValidator` (more than 8 → error naming limit 8; self-reference → ids=[primary]; duplicates case-insensitive → ids; invalid-format ids → ids; not-stored ids → ids; valid list passes; empty list passes) in tests/unit/Images/ImageAlternatesValidatorTests.cs
- [X] T004 [P] Write failing unit tests for `ReferenceSetTemplateMatcher` using a fake inner `ITemplateMatcher` keyed by template Mat: union ordered by confidence desc; equal scores prefer primary then alternates in order; overlapping boxes across references de-duplicated by `config.Overlap`; capped at `config.MaxResults` with `LimitsHit` true when truncated; `LimitsHit` propagated from any inner result; every match carries `ReferenceId`; `Masked`/`RetainedPixelCount` taken from the primary result; `NoInformationPositionCount` summed; cancellation propagates; an information log names the matched reference when an alternate wins — in tests/unit/Vision/ReferenceSetTemplateMatcherTests.cs
- [X] T005 [P] Write failing unit tests for `ReferenceImageSetLoader` using `MemoryReferenceImageStore` and an in-memory alternates repository: primary missing → null; no alternates → empty `Alternates`; missing alternate ids reported in `MissingAlternateIds` and skipped; order preserved; no transitive expansion (alternate's own alternates ignored); `ReferenceImageSet.LogMissingAlternates(ILogger)` emits one warning naming the primary and each missing id, and nothing when none are missing; `CreateMatcher` returns the very same inner instance when there are no alternates — in tests/unit/Images/ReferenceImageSetLoaderTests.cs
- [X] T006 Add init-only `string? ReferenceId` to `TemplateMatch` with an XML doc comment in src/GameBot.Domain/Vision/ITemplateMatcher.cs
- [X] T007 [P] Implement `IImageAlternatesRepository` (`Get(id)`, `Set(id, IReadOnlyList<string>)`, `Delete(id)`) and `FileImageAlternatesRepository(root)` storing `<root>\.alternates\{id}.json` as `{ "alternates": [...] }`, writing through `<root>\.tmp` then `File.Replace`/`File.Move` (empty list deletes the file) in src/GameBot.Domain/Images/ImageAlternatesRepository.cs — makes T002 pass
- [X] T008 [P] Implement `ImageAlternatesValidator.Validate(primaryId, alternates, Func<string,bool> exists)` returning a result with `IsValid`, `Message`, `Hint`, `Ids` (max 8 constant `MaxAlternates`) in src/GameBot.Domain/Images/ImageAlternatesValidator.cs — makes T003 pass
- [X] T009 Implement `ReferenceSetTemplateMatcher : ITemplateMatcher` (ctor: inner matcher, primary id, `IReadOnlyList<(string Id, Mat Template)>` alternates, optional `ILogger`) per research R-003, reusing `Nms.Apply`, in src/GameBot.Domain/Vision/ReferenceSetTemplateMatcher.cs — makes T004 pass
- [X] T010 Implement `ReferenceImageSet` record and `ReferenceImageSetLoader.TryLoad(IReferenceImageStore, IImageAlternatesRepository?, string id, out ReferenceImageSet?)` plus helpers `ReferenceImageSet.CreateMatcher(ITemplateMatcher inner, Func<Bitmap, Mat> toMat, ILogger?)` (returns `inner` unchanged when there are no alternates) and `ReferenceImageSet.LogMissingAlternates(ILogger)` (every consumer calls it once per detection) in src/GameBot.Domain/Images/ReferenceImageSetLoader.cs — depends on T006, T007, T009; makes T005 pass
- [X] T011 Register `IImageAlternatesRepository` as a singleton `FileImageAlternatesRepository` on `ImageStorageOptions.Root` in `RegisterTriggerAndImageServices` in src/GameBot.Service/GameBotServiceSetup.cs

**Checkpoint**: Foundation green (build + T002–T005 pass).

---

## Phase 3: User Story 2 — Manage an image's alternates (Priority: P1)

**Goal**: Authors can set, read, clear alternates and see them in metadata.
**Independent Test**: Set `[B, C]` on `A`, read back, replace with `[C]`, clear; invalid requests are refused with unchanged state.

> Implemented before US1 because US1's end-to-end tests need the API to register alternates.

- [X] T012 [P] [US2] Write a failing contract test that the published OpenAPI document contains GET and PUT `/api/images/{id}/alternates` with 200/400/404 responses, the `ImageAlternatesResponse` schema, and a non-empty operation description mentioning the limit of 8, in tests/contract/Images/ImageAlternatesOpenApiTests.cs
- [X] T013 [P] [US2] Write failing integration tests: PUT then GET returns ordered ids with `exists: true`; PUT `[]` clears; metadata includes `alternates`; 404 for unknown primary on GET and PUT; 400 `invalid_request` for missing body/`alternates`; 400 `invalid_alternates` for >8, self, duplicate, unknown and invalid-format ids with `ids` populated, and GET afterwards shows the previous list unchanged — in tests/integration/ImageAlternatesIntegrationTests.cs
- [X] T014 [US2] Implement `ImageAlternatesEndpoints` with named handlers `GetAlternatesAsync` and `SetAlternatesAsync`, request `SetImageAlternatesRequest { Alternates }`, response `ImageAlternatesResponse { Id, Alternates: [{ Id, Exists }] }`, error envelope `{ error: { code, message, hint, ids } }`, `.Produces<ImageAlternatesResponse>()`, 400/404 declarations and `.WithDescription(...)` in src/GameBot.Service/Endpoints/ImageAlternatesEndpoints.cs
- [X] T015 [US2] Map the endpoints next to `app.MapImageReferenceEndpoints()` in src/GameBot.Service/Program.cs
- [X] T016 [US2] Add `alternates` (ids, registered order, `[]` when none) to `GetImageMetadataAsync` by injecting `IImageAlternatesRepository` in src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs
- [X] T017 [US2] Run T013 and T012 against the implementation (T014–T016) and fix until green; tests in tests/integration/ImageAlternatesIntegrationTests.cs and tests/contract/Images/ImageAlternatesOpenApiTests.cs

**Checkpoint**: US2 green — alternates are manageable.

---

## Phase 4: User Story 1 — One image covers day and night renderings (Priority: P1) 🎯 MVP

**Goal**: Every detection naming a primary also matches its alternates, with no sequence edits.
**Independent Test**: A night-only alternate turns a no-match into a match for the primary id.

- [X] T018 [P] [US1] Write failing integration tests with a synthetic frame (pattern "night" variant present, "day" variant absent): (a) `POST /api/images/detect` for primary `A` returns no match before alternates and one match with `templateId`=`A`, `matchedReferenceId`=`A-night` after PUT; (b) both references visible at the same place → one match from the higher score; (c) no reference visible → empty; (d) an image with no alternates returns identical matches/scores/boxes to a baseline captured before any alternates call and `matchedReferenceId` equal to its id; (e) deleting `A-night` then detecting `A` still succeeds (no 500); (f) `POST /api/images/detect-all` still reports `A-night` under its own `imageId` and never as `A` — in tests/integration/ImageAlternatesIntegrationTests.cs
- [X] T019 [P] [US1] Write failing integration tests executing a command with a `waitForImage` step and a `primitiveTap` detection target naming `A`, and a sequence `If`/`Break` image condition naming `A` (`present` and `absent` expectations), each satisfied only through the night alternate — in tests/integration/ImageAlternatesExecutionTests.cs
- [X] T020 [P] [US1] Write failing unit tests that `ImageMatchEvaluator` returns the maximum similarity across primary and alternates, returns the primary's similarity unchanged when there are no alternates, ignores missing alternates, and logs (Information) the alternate id when an alternate supplies the maximum, in tests/unit/Triggers/ImageMatchEvaluatorAlternatesTests.cs
- [X] T021 [P] [US1] Write a failing unit test that `GameReadinessProbe.WaitUntilReadyAsync` reports `Ready=true` when only a night alternate of the readiness image is on a stub `IScreenSource`, and still `"missing"` when the primary is absent, in tests/unit/Services/GameReadinessProbeAlternatesTests.cs
- [X] T022 [US1] Add `MatchedReferenceId` to `MatchResult` in src/GameBot.Service/Endpoints/Dto/ImageDetectionsDtos.cs
- [X] T023 [US1] In `DetectAsync`, via a new named helper method `LoadDetectionMatcher` (keeps `DetectAsync` from growing; constitution I), load the set with `ReferenceImageSetLoader` (inject `IImageAlternatesRepository?` via `IServiceProvider`), decode alternates with `TemplateImageDecoder.Decode` exactly as the primary, wrap the matcher via `CreateMatcher`, dispose alternate Mats, log missing alternates, and set `MatchedReferenceId = m.ReferenceId ?? id` in src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs (leave `DetectAllAsync` unchanged)
- [X] T024 [US1] Change `ImageDetectionHelper.TryDetect` to take a `ReferenceImageSet` (primary + alternates) and wrap the matcher for `TryApplyDetectionCoordinates` in src/GameBot.Service/Services/ImageDetectionHelper.cs
- [X] T025 [US1] Add optional `IImageAlternatesRepository? alternates = null` as the last parameter of the detection-capable `CommandExecutor` constructor; load sets in the primitive-tap and wait-for-image paths (keep the existing `template_not_found` / `image_unavailable` outcomes when the primary is missing); pass the set to `TryDetectAndTap` (wrap the matcher there) and `TryDetectImage` in src/GameBot.Service/Services/CommandExecutor.cs
- [X] T026 [US1] Load the set in `GameReadinessProbe` (new optional `IImageAlternatesRepository?` ctor parameter, passed from DI) and call the new `TryDetect` signature in src/GameBot.Service/Services/EnsureGameRunning/GameReadinessProbe.cs and src/GameBot.Service/GameBotServiceSetup.cs
- [X] T027 [US1] Refactor `ImageMatchEvaluator.ComputeSimilarity` into a per-template function and return the max over primary + existing alternates; add an optional `IImageAlternatesRepository?` constructor parameter to the DI constructor (keep the two-argument test constructor); log the alternate id when it supplies the maximum and call `LogMissingAlternates` in src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs — makes T020 pass
- [X] T028 [US1] Update any other callers/test doubles of the changed signatures (`ImageDetectionHelper.TryDetect`, `GameReadinessProbe` ctor) found with the Grep tool for `TryDetect\(|new GameReadinessProbe\(` under src/ and tests/; confirm T021 passes

**Checkpoint**: US1 green — T018–T020 pass, all existing ImageDetections/MaskedDetection/Detection* tests still pass.

---

## Phase 5: User Story 3 — Alternates survive lifecycle operations (Priority: P2)

**Goal**: Durable across restart, backup/restore, overwrite and delete.
**Independent Test**: Set alternates, restart/restore/delete, confirm behaviour.

- [X] T029 [P] [US3] Write failing integration tests: alternates still in effect from a second `WebApplicationFactory` over the same data dir (restart); overwriting `A`'s content keeps its alternates; deleting `A` removes its list (re-upload of `A` reads `[]`); deleting an alternate `B` leaves `A`'s list with `B` reported `exists: false` — in tests/integration/ImageAlternatesIntegrationTests.cs
- [X] T030 [P] [US3] Write failing unit tests for `BackupService`: a backup of a sequence naming `A` includes `images/B.png`, `images/C.png` and `image-alternates/A.json`; restoring it re-creates `A`'s alternates; restoring an archive without `image-alternates/` leaves existing alternates untouched — in tests/unit/BackupServiceTests.cs
- [X] T031 [US3] On successful delete in `DeleteImageAsync`, call `IImageAlternatesRepository.Delete(id)` in src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs
- [X] T032 [US3] Extend `BackupService` (new optional `IImageAlternatesRepository?` ctor parameter): add existing alternates of selected images to the image set, write `image-alternates/{id}.json`, and in `ApplyRestoreAsync` apply those sidecars after images are saved, filtering to ids that exist; ignore the `image-alternates/` prefix in image-id readers in src/GameBot.Service/Services/BackupService.cs

**Checkpoint**: US3 green.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T033 [P] Add `ImageAlternatesSchemaFilter` describing `MatchResult.matchedReferenceId` and the alternates request/response fields, register it beside `QueueHealthSchemaFilter` in src/GameBot.Service/Swagger/ImageAlternatesSchemaFilter.cs and src/GameBot.Service/GameBotServiceSetup.cs; extend T012's contract test to assert the `matchedReferenceId` description
- [X] T034 [P] Add `[Trait("Category","Performance")]` test `ReferenceSetMatcherPerformanceTests` timing 1 vs 1+3 references with the real `TemplateMatcher` on a 1280×720 frame (asserts only that 1+3 stays under 6× the single-reference time, to allow CI noise) and record measured numbers in the PR perf note, in tests/unit/Performance/ReferenceSetMatcherPerformanceTests.cs
- [X] T035 [P] Update `docs/architecture.md`: images capability (alternates, merge rule, `matchedReferenceId`, detect-all unchanged), API surface (GET/PUT alternates, metadata field), persistence layout (`.alternates\{id}.json`), backup archive entries; refresh "Last reviewed"
- [X] T036 [P] Add a `specs/STATUS.md` row for 097 and set spec 097 `**Status**: Implemented`; add a CHANGELOG.md entry under Unreleased
- [X] T037 Full verification: build solution; run `tests/unit`, `tests/integration`, `tests/contract` (rerun `MaskedTemplateMatchTests` once if it flakes); confirm zero new analyzer warnings

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 (T006 before T009; T007/T008 independent; T010 after T006, T007, T009) → T011.
- Phase 3 (US2) depends on Phase 2; T013 and T012 (tests) precede T014–T016; T017 verifies.
- Phase 4 (US1) depends on Phase 2 and on US2's PUT endpoint for its integration tests (T018, T019).
  Unit-level US1 work (T020–T027) can start right after Phase 2.
- Phase 5 (US3) depends on US2 (T014) and Phase 2.
- Phase 6 after all stories.

## Parallel Examples

- Phase 2: T002, T003, T004, T005 together; then T007 and T008 together.
- US2: T013 and T012 together.
- US1: T018, T019, T020, T021 together; T022–T024 are separate files and may proceed in parallel after tests exist, T025 after T024.
- US3: T029 and T030 together.
- Polish: T033, T034, T035, T036 together.

## Implementation Strategy

MVP = Phase 2 + US2 + US1: the live night-lighting failure is fixed once alternates can be registered and
every detection consumer uses them. US3 then makes the fix durable across backup/restore and deletes.

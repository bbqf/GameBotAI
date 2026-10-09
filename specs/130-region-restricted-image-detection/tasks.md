# Tasks: Region-Restricted Image Detection

**Input**: Design documents in `specs\130-region-restricted-image-detection\` (spec.md, plan.md, research.md, data-model.md, contracts\region-contract.md, quickstart.md). Closes issue #272.
**Tests**: Included. Plan Principle II requires tests before code. Write each test first and see it fail.
**Paths**: All paths are relative to `C:\src\GameBot`.
**Format**: `- [ ] T### [P?] [Story?] Description with path`. [P] means no other task in the same phase edits the same file.

## Phase 1: Setup (early checks)

**Purpose**: Find out which optional paths exist. Record each result in the section "Setup check results" of `specs\130-region-restricted-image-detection\research.md`. Each check ends in a named change or in "record no change" with the reason.

- [ ] T001 Check if the legacy class `src\GameBot.Domain\Commands\ImageVisibleCondition.cs` has an API read or write path (search `src\GameBot.Service` for its use). Record the result in `specs\130-region-restricted-image-detection\research.md`.
- [ ] T002 Check how the `primitiveTap` sequence payload `detectionTarget` is read: typed map or raw dictionary (`src\GameBot.Service\Endpoints\SequencesEndpoints.cs`, `src\GameBot.Service\Endpoints\StepsEndpoints.cs`). Record the result in `specs\130-region-restricted-image-detection\research.md`.
- [ ] T003 Check if `src\GameBot.Service\Services\ImageDetectionHelper.cs` and `src\GameBot.Service\Services\Conditions\ImageDetectionConditionAdapter.cs` read a condition or target field by field. Record the result (carry the region, or no change) in `specs\130-region-restricted-image-detection\research.md`.
- [ ] T004 Check if the web UI types in `src\web-ui\src\types` drop `region` on save for `imageVisible` or `detectionTarget`. Record the result in `specs\130-region-restricted-image-detection\research.md`.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The one validation rule, the one clip rule, and the field on every type.

- [ ] T005 Write `tests\unit\Commands\PixelRegionTests.cs` (new): `Validate` for each invalid form, every invalid field named at once, valid forms; `ClipTo` for inside, past the edge, fully outside, and edge pixels (`X + Width - 1`).
- [ ] T006 Create `src\GameBot.Domain\Commands\PixelRegion.cs`: immutable `PixelRegion` (`X`, `Y`, `Width`, `Height`), `Validate(prefix)` that names each invalid field (D1), and `ClipTo(width, height)`. Make T005 pass.
- [ ] T007 [P] Add `PixelRegion? Region` (persisted name `region`) to `DetectionTarget` in `src\GameBot.Domain\Commands\DetectionTarget.cs`.
- [ ] T008 [P] Add `PixelRegion? Region` to `ImageVisibleStepCondition`, omitted from JSON when null, in `src\GameBot.Domain\Commands\SequenceStepCondition.cs`.
- [ ] T009 [P] Add `PixelRegion? PixelRegion` to `Blocks.Condition` in `src\GameBot.Domain\Commands\Blocks\Condition.cs`. Keep the fraction `Region`.
- [ ] T010 [P] Add `PixelRegion? PixelRegion` to `ImageMatchParams` in `src\GameBot.Domain\Triggers\Trigger.cs`.
- [ ] T011 [P] Add the new `PixelRegionDto` (four `int?` fields) and `PixelRegionDto? Region` on `DetectionTargetDto` in `src\GameBot.Service\Models\Commands.cs`.

**Checkpoint**: Solution builds. User stories can start.

---

## Phase 3: User Story 1 - Limit an image condition to a screen region (P1)

**Goal**: An `imageVisible` condition with a region is true only when the image is inside the region.
**Independent Test**: Save and read back a condition with a region. Run it on a capture with the image inside (true), only outside (false), and at two places (only the inside match counts).

### Tests first

- [ ] T012 [US1] Write `tests\unit\Triggers\ImageMatchEvaluatorRegionTests.cs` (new): match inside is true; match only outside is false; two matches, only the inside one counts; region smaller than the image is false; region past the capture is clipped; both fraction `Region` and `PixelRegion` set uses `PixelRegion`; no region gives the old result.
- [ ] T013 [P] [US1] Write `tests\unit\Parameters\RegionCopyTests.cs` (new): `TryResolveImage` keeps the region.
- [ ] T014 [P] [US1] Write `tests\unit\Sequences\ImageConditionRegionEvaluationTests.cs` (new): `SequenceStepConditionEvaluator` carries the region to the adapters; `Describe` and `DescribeBreakCondition` text with a region (`region=x,y,width,height`) and without (equals the old text) (FR-012).
- [ ] T015 [P] [US1] Write `tests\unit\StepThrough\RegionDescriptionTests.cs` (new): the step-through output shows `region=x,y,width,height`.
- [ ] T016 [P] [US1] Write `tests\contract\Sequences\RegionImageConditionContractTests.cs` (new): save and read back an `imageVisible` condition with a region at top level, in `all`, `any`, `none`, in an `if`, in a loop condition, and as a break condition; a condition with no region has no `region` field.

### Implementation

- [ ] T017 [US1] Crop with `SubMat` in `ComputeSimilarity` in `src\GameBot.Domain\Triggers\Evaluators\ImageMatchEvaluator.cs`. `PixelRegion` wins over the fraction `Region`. Empty or too small area gives similarity 0.
- [ ] T018 [P] [US1] Copy `PixelRegion` into `ImageMatchParams` in `src\GameBot.Service\Services\Conditions\ImageVisibleConditionAdapter.cs`.
- [ ] T019 [P] [US1] Copy `PixelRegion` into `ImageMatchParams` in `src\GameBot.Service\Services\Conditions\ImageDetectionConditionAdapter.cs` (or record no change per T003).
- [ ] T020 [P] [US1] Copy the region when building `Blocks.Condition` (line 168) and add `, region=x,y,width,height` in `Describe` (line 265), only when set, in `src\GameBot.Domain\Services\SequenceStepConditionEvaluator.cs`.
- [ ] T021 [P] [US1] Add the same text rule to `DescribeBreakCondition` in `src\GameBot.Domain\Services\SequenceRunner.cs`.
- [ ] T022 [P] [US1] Copy `Region` in `TryResolveImage` (line 109 to 136) in `src\GameBot.Domain\Parameters\SequenceStepConditionResolver.cs`.
- [ ] T023 [P] [US1] Add `PixelRegionDto? Region` to `ImageVisibleConditionContract` in `src\GameBot.Service\Models\SequenceStepContracts.cs`.
- [ ] T024 [US1] Map the region in `MapPerStepCondition` (line 1454) and return it in `MapPerStepConditionToDto` (line 632) in `src\GameBot.Service\Endpoints\SequencesEndpoints.cs`.
- [ ] T025 [US1] If T001 found a read or write path for the legacy class `ImageVisibleCondition`, map and validate `Region` there, in the file named in `research.md`. Else record no change.

**Checkpoint**: T012 to T016 pass. Conditions work end to end.

---

## Phase 4: User Story 2 - Limit a tap target to a screen region (P1)

**Goal**: A `detectionTarget` with a region taps the match inside the region, in full-capture pixels.
**Independent Test**: Save and read back a target with a region. On a capture with two matches (the better one outside), the tap lands on the inside match.

### Tests first

- [ ] T026 [US2] Write `tests\unit\Commands\DetectionCoordinateResolverRegionTests.cs` (new): two matches with the better one outside gives the inside tap point in full-capture pixels; image not inside gives "not found" and no tap; empty clip gives "not found" and no error; both selection strategies.
- [ ] T027 [US2] Extend `tests\unit\Parameters\RegionCopyTests.cs`: `TryDetection` keeps the region for `primitiveTap`, `waitForImage`, and `ensureGameRunning.readinessImage`.
- [ ] T028 [P] [US2] Write `tests\contract\Commands\RegionDetectionTargetContractTests.cs` (new): `detectionTarget`, command-level `detection`, and `ensureGameRunning.readinessImage` keep the region on read-back; steps endpoints too.
- [ ] T029 [P] [US2] Write `tests\contract\Sequences\RegionPayloadContractTests.cs` (new): `waitForImage` and `primitiveTap` payload `detectionTarget.region` read back.

### Implementation

- [ ] T030 [US2] Crop to the clipped region in both `ResolveCenter` overloads, then add the origin to each box, in `src\GameBot.Domain\Commands\Execution\DetectionCoordinateResolver.cs`. Empty or too small area gives "not found" with no error.
- [ ] T031 [P] [US2] Copy `Region` into the resolved `DetectionTarget` in `TryDetection` (line 179) in `src\GameBot.Domain\Parameters\CommandStepResolver.cs`.
- [ ] T032 [P] [US2] If T003 found that `src\GameBot.Service\Services\ImageDetectionHelper.cs` reads target fields one by one, carry the region to the resolver call there. Else record no change.
- [ ] T033 [US2] Command-level `detection`: map, store, and return the region in `ToDomainDetection` (line 285) and `ToResponseDetection` (line 295) and the `detectionTarget` sites (lines 51, 69, 236, 351, 440) in `src\GameBot.Service\Endpoints\CommandsEndpoints.cs`.
- [ ] T034 [US2] `ensureGameRunning` readiness image: map, store, and return the region in the step detection targets (primitiveTap, waitForImage, readiness image) in `src\GameBot.Service\Endpoints\StepsEndpoints.cs`.
- [ ] T035 [US2] Read and return `region` in `MapWaitForImageDetectionTarget` (line 1393) and in the `primitiveTap` payload `detectionTarget` (per T002) in `src\GameBot.Service\Endpoints\SequencesEndpoints.cs`.

**Checkpoint**: T026 to T029 pass. Tap targets work end to end.

---

## Phase 5: User Story 3 - Existing data keeps its behaviour (P1)

**Goal**: Data and runs with no region behave as before (FR-008, SC-003).
**Independent Test**: Old stored data loads with no region; the existing suites pass with no changed expectation.

- [ ] T036 [P] [US3] Write `tests\unit\Commands\RegionBackwardCompatTests.cs` (new): stored JSON of a sequence and of a command with no `region` loads without error, has a null region, and serialises with no `region` field.
- [ ] T037 [US3] Run the existing condition and target suites in `tests\unit`, `tests\contract`, and `tests\integration`. Fix any change in an expected result by fixing the code, not the test (SC-003).

---

## Phase 6: User Story 4 - Reject a bad region at save time (P2)

**Goal**: A bad region gives a 400 that names every invalid field. Nothing is stored. Runs after US1 and US2.
**Independent Test**: Send each invalid form (size 0 or less, negative origin, missing field) to each save path. Each gives 400 that names the field.

### Tests first

- [ ] T038 [US4] Extend `tests\contract\Sequences\RegionImageConditionContractTests.cs`: bad regions on conditions give 400 with every invalid field named, and nothing is stored (SC-004).
- [ ] T039 [P] [US4] Extend `tests\contract\Commands\RegionDetectionTargetContractTests.cs`: bad regions on `detectionTarget`, command-level `detection`, readiness image, and steps endpoints give 400.
- [ ] T040 [P] [US4] Extend `tests\contract\Sequences\RegionPayloadContractTests.cs`: a bad `region` in `waitForImage` and `primitiveTap` payloads gives 400.

### Implementation

- [ ] T041 [P] [US4] Call `PixelRegion.Validate` for an image condition (lines 233, 389) in `src\GameBot.Domain\Services\SequenceStepValidationService.cs`.
- [ ] T042 [P] [US4] Call `PixelRegion.Validate` for an image condition (line 106) in `src\GameBot.Domain\Services\CompositeConditionValidator.cs`.
- [ ] T043 [P] [US4] Call `PixelRegion.Validate` at the `ImageVisibleStepCondition` case (line 316) in `src\GameBot.Domain\Commands\FileSequenceRepository.cs`.
- [ ] T044 [P] [US4] Validate in `ToDomainDetection` through `PixelRegion.Validate` and return 400 in `src\GameBot.Service\Endpoints\CommandsEndpoints.cs`.
- [ ] T045 [P] [US4] Validate the region in the step detection targets in `src\GameBot.Service\Endpoints\StepsEndpoints.cs`.
- [ ] T046 [US4] Validate in `MapPerStepCondition`, `MapWaitForImageDetectionTarget`, and the `primitiveTap` payload read in `src\GameBot.Service\Endpoints\SequencesEndpoints.cs`. Return one 400 with all invalid fields.

**Checkpoint**: T038 to T040 pass. The same bad region gives the same messages in every place.

---

## Phase 7: Polish and cross-cutting

- [ ] T047 Write `tests\contract\Sequences\RegionOpenApiTests.cs` (new): `swagger.json` lists `region` on `ImageVisibleCondition` and `DetectionTarget`, and the `PixelRegion` schema (FR-010).
- [ ] T048 [P] Describe `region` and add the `PixelRegion` schema text in `src\GameBot.Service\Swagger\ParametrizedReferenceImageSchemaFilter.cs`.
- [ ] T049 [P] Update the `waitForImage` and `primitiveTap` payload text in `src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs`.
- [ ] T050 [P] Describe the `region` field and update "Last reviewed" in `docs\architecture.md`.
- [ ] T051 [P] Add an entry in `CHANGELOG.md`.
- [ ] T052 [P] Update the feature row in `specs\STATUS.md`.
- [ ] T053 [P] Set the `Status` line in `specs\130-region-restricted-image-detection\spec.md`.
- [ ] T054 If the repository keeps a version override file for a feature release, update it as the last feature did.
- [ ] T055 Build the solution and run all tests (`tests\unit`, `tests\contract`, `tests\integration`). Fix any failure.
- [ ] T056 Run the checks in `specs\130-region-restricted-image-detection\quickstart.md` (SC-005: one row selected with `region` only).

---

## Dependencies and order

- Phase 1 (checks) first. T002 and T003 feed T032, T035, and T019.
- Phase 2 blocks all stories. T006 follows T005. T007 to T011 follow T006.
- US1 (Phase 3) and US2 (Phase 4) follow Phase 2. They share `RegionCopyTests.cs` and `SequencesEndpoints.cs`, so run US1 first, then US2.
- US3 (Phase 5) follows US1 and US2.
- US4 (Phase 6) follows US1 and US2 (it extends their files).
- Polish (Phase 7) follows all stories.
- Inside a phase, a task without [P] runs in task order. Tests come before code.

## Parallel examples

- Phase 2: T007, T008, T009, T010, T011 together.
- US1 tests: T013, T014, T015, T016 together (T012 is the evaluator test, run first).
- US1 code: T018 to T023 together after T017.
- US4 code: T041 to T045 together, then T046.
- Polish: T048 to T053 together.

## Implementation strategy

- MVP: Phases 1, 2, and 3 (condition region). Stop and check.
- Then US2 (tap target), US3 (regression), US4 (validation), and Polish.
- Do not commit with a failing build or test.

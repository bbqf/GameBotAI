# Implementation Plan: Region-Restricted Image Detection

**Branch**: `130-region-restricted-image-detection` | **Date**: 2026-10-09 | **Spec**: `specs/130-region-restricted-image-detection/spec.md`
**Input**: Feature specification from `/specs/130-region-restricted-image-detection/spec.md`. Closes issue #272.

## Summary

An `imageVisible` condition and a `detectionTarget` get one optional field, `region`. It is a pixel rectangle
(`x`, `y`, `width`, `height`) of the capture. When it is set, the image detection searches only inside the region.
When it is absent, nothing changes.

The approach has six parts:

1. A new domain type, `PixelRegion` (integer pixels). It holds the one validation rule, `PixelRegion.Validate`, and the
   clip rule, `ClipTo` (decision D1). No other code has its own copy of the rule.
2. The field is added to the domain types (`DetectionTarget`, `ImageVisibleStepCondition`), the evaluator inputs
   (`Blocks.Condition`, `ImageMatchParams`), and the API types (`DetectionTargetDto`, `ImageVisibleConditionContract`).
   Each copy site carries the field (research R1). One read-back test covers each site.
3. A tap target crops the capture in `DetectionCoordinateResolver` (research R3). A condition crops in
   `ImageMatchEvaluator` (research R5). Both add the region origin back, so results are in full-capture pixels.
4. Every save path calls `PixelRegion.Validate`. An invalid region gives a 400 that names every invalid field.
5. The condition text in execution logs, trees, and step-through shows `region=x,y,width,height`, only when a region
   is set (FR-012).
6. OpenAPI descriptions and `docs\architecture.md` describe the field.

There is no new step type, no new endpoint, and no change to the match threshold logic (FR-009).

## Technical Context

**Language/Version**: C# on .NET (the version in `src\GameBot.Service\GameBot.Service.csproj`). TypeScript for the web editor (read-only check only, no change planned).
**Primary Dependencies**: ASP.NET Core minimal API, `System.Text.Json`, Swashbuckle (OpenAPI), OpenCvSharp (`Mat.SubMat`). No new package.
**Storage**: JSON files through `FileSequenceRepository` and `FileCommandRepository`. No schema migration. A stored object without `region` reads as "no region".
**Testing**: xUnit and FluentAssertions in `tests\unit`, `tests\contract`, `tests\integration`.
**Target Platform**: Windows service.
**Project Type**: Web service with a web UI.
**Performance Goals**: A region search is not slower than a full-capture search. The crop makes the matched area smaller. A search with no region uses the same code path as today.
**Constraints**: No threshold change, no row-aware detection, no field name other than `region` (FR-009). No scaling between resolutions.
**Scale/Scope**: One optional field on two objects, in every place that holds them (FR-011). The change sites are in the table below. This plan gives no file count, so the count cannot drift from the table.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code Quality | Pass | One validation rule and one clip rule in `PixelRegion`. Endpoints and validators only call it (D1). The fraction `Region` types stay as they are. |
| II. Testing | Pass | The test table below covers each requirement and each copy site. Tests come before the code in `tasks.md`. |
| III. User Experience | Pass | An invalid region gives a 400 that names every invalid field. OpenAPI describes the field. The condition text in logs shows the region. |
| IV. Performance | Pass | A crop reduces the match area. No region means the same path as today. |
| V. Living Documentation | Pass | `docs\architecture.md`, `CHANGELOG.md`, `specs\STATUS.md`, and the `Status` line of `spec.md` are in the task list (see "Docs"). |
| VI. STE | Pass | All new text in this feature is written in STE. `/speckit-analyze` checks it. |

No violation. The Complexity Tracking table is not needed.

Post-design re-check: the design adds no project, no endpoint, and no step type. The result is the same: Pass.

## Project Structure

### Documentation (this feature)

```text
specs\130-region-restricted-image-detection\
  spec.md
  plan.md                 (this file)
  research.md             Phase 0 (has the section "Setup check results")
  data-model.md           Phase 1
  quickstart.md           Phase 1
  contracts\region-contract.md   Phase 1
  checklists\requirements.md
  tasks.md                Phase 2 (made by the tasks command, not by this command)
```

### Source code: change sites

All paths are relative to `C:\src\GameBot`. Row 1 to row 22 are the planned changes. Rows marked "check" end in a
named change or in "record no change" with the reason, in the section "Setup check results" of `research.md`.

| # | File | Change |
|---|------|--------|
| 1 | `src\GameBot.Domain\Commands\PixelRegion.cs` (new) | Immutable `PixelRegion` (`X`, `Y`, `Width`, `Height`). `Validate(prefix)` returns messages that name each invalid field (D1). `ClipTo(width, height)` returns the part inside the capture or "none". |
| 2 | `src\GameBot.Domain\Commands\DetectionTarget.cs` | Add `PixelRegion? Region` (optional last constructor or init value). Persisted name `region`. |
| 3 | `src\GameBot.Domain\Commands\SequenceStepCondition.cs` | Add `PixelRegion? Region` to `ImageVisibleStepCondition`. Omit from JSON when null. |
| 4 | `src\GameBot.Domain\Commands\Blocks\Condition.cs` | Add `PixelRegion? PixelRegion` to `Blocks.Condition`. The fraction `Region` stays. |
| 5 | `src\GameBot.Domain\Triggers\Trigger.cs` | Add `PixelRegion? PixelRegion` to `ImageMatchParams`. |
| 6 | `src\GameBot.Domain\Triggers\Evaluators\ImageMatchEvaluator.cs` | In `ComputeSimilarity`, when `PixelRegion` is set, compute the clipped rectangle and crop with `SubMat`. `PixelRegion` wins over the fraction `Region`. Empty or too small area gives similarity 0. |
| 7 | `src\GameBot.Domain\Commands\Execution\DetectionCoordinateResolver.cs` | In both `ResolveCenter` overloads, crop to the clipped region, match, then add the origin to each box. Empty or too small area gives "not found" with no error. |
| 8 | `src\GameBot.Service\Services\Conditions\ImageVisibleConditionAdapter.cs`, `src\GameBot.Service\Services\Conditions\ImageDetectionConditionAdapter.cs` | Copy `PixelRegion` into `ImageMatchParams` (line 39 and line 35). |
| 9 | `src\GameBot.Domain\Services\SequenceStepConditionEvaluator.cs` | Where it builds `Blocks.Condition` from `ImageVisibleStepCondition` (line 168), copy the region. In `Describe` (line 265), add `, region=x,y,width,height` only when set (R11). |
| 10 | `src\GameBot.Domain\Services\SequenceRunner.cs` | In `DescribeBreakCondition` (near line 1728), same text rule as row 9. |
| 11 | `src\GameBot.Domain\Parameters\SequenceStepConditionResolver.cs` | `TryResolveImage` (line 109) copies `Region` into the new `ImageVisibleStepCondition` (line 136). |
| 12 | `src\GameBot.Domain\Parameters\CommandStepResolver.cs` | `TryDetection` (line 179) copies `Region` into the resolved `DetectionTarget`. It covers `primitiveTap`, `waitForImage`, and `ensureGameRunning.readinessImage`. |
| 13 | `src\GameBot.Service\Models\Commands.cs` | Add `PixelRegionDto? Region` to `DetectionTargetDto` (line 214). Add the new `PixelRegionDto` with four `int?` fields. |
| 14 | `src\GameBot.Service\Models\SequenceStepContracts.cs` | Add `PixelRegionDto? Region` to `ImageVisibleConditionContract` (line 136). |
| 15 | `src\GameBot.Service\Endpoints\CommandsEndpoints.cs` | `ToDomainDetection` (line 285) validates through `PixelRegion.Validate` and maps. `ToResponseDetection` (line 295) returns it. This covers the command-level `detection`, `detectionTarget`, and the readiness image (lines 51, 69, 236, 351, 440). |
| 16 | `src\GameBot.Service\Endpoints\StepsEndpoints.cs` | Map and validate the region in the step detection targets (primitiveTap, waitForImage, readiness image). |
| 17 | `src\GameBot.Service\Endpoints\SequencesEndpoints.cs` | `MapPerStepCondition` (line 1454): map and validate the region. `MapPerStepConditionToDto` (line 632): return it. `MapWaitForImageDetectionTarget` (line 1393): read, validate, and return `region` from the raw payload. Add the same read for the `primitiveTap` payload `detectionTarget`. |
| 18 | `src\GameBot.Domain\Services\SequenceStepValidationService.cs`, `src\GameBot.Domain\Services\CompositeConditionValidator.cs` | Call `PixelRegion.Validate` for an image condition (lines 233, 389; line 106). They do not hold their own copy of the rule. |
| 19 | `src\GameBot.Domain\Commands\FileSequenceRepository.cs` | At the `ImageVisibleStepCondition` case (line 316), call `PixelRegion.Validate` so a stored write also refuses a bad region. |
| 20 | `src\GameBot.Service\Swagger\ParametrizedReferenceImageSchemaFilter.cs`, `src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs` | Describe `region` on `ImageVisibleCondition` and `DetectionTarget`. Add the `PixelRegion` schema text. Update the `waitForImage` and `primitiveTap` payload text. |
| 21 | `src\GameBot.Service\Services\ImageDetectionHelper.cs` | Check (C3). It takes a `DetectionTarget` (line 33). If it reads the target fields one by one, carry the region to the resolver call. Otherwise record no change. |
| 22 | `docs\architecture.md` | Describe the `region` field. Update "Last reviewed". |

Check sites (the results go to the section "Setup check results" in `research.md`):

| Check | What to find | Result rule |
|-------|--------------|-------------|
| Legacy class `src\GameBot.Domain\Commands\ImageVisibleCondition.cs` | Any API read or write path | If a path exists, map and validate the region there (conditional task in the US1 phase). Else record no change. |
| `primitiveTap` sequence payload | Typed map or raw dictionary | Row 17 covers the raw dictionary. A typed map, if it exists, gets the same read and validation. |
| `ImageDetectionHelper.cs` and `ImageDetectionConditionAdapter.cs` | Reads a condition or a target | See rows 8 and 21. |
| Web UI types (`src\web-ui\src\types`) | A typed `imageVisible` or `detectionTarget` that would drop `region` on save | Record the result. A change is outside this feature unless the UI drops the field. |

### Tests to add or change

| File | Covers |
|------|--------|
| `tests\unit\Commands\PixelRegionTests.cs` (new) | `Validate`: each invalid form, every invalid field named at once, valid forms. `ClipTo`: inside, past the edge, fully outside, edge pixels (`X + Width - 1`). |
| `tests\unit\Triggers\ImageMatchEvaluatorRegionTests.cs` (new) | A match inside the region is true. A match only outside is false. Two matches: only the inside one counts (US1, SC-001). Region smaller than the image gives false. A region past the capture is clipped. When both fraction `Region` and `PixelRegion` are set, the detection uses `PixelRegion`. No region gives the old result. |
| `tests\unit\Commands\DetectionCoordinateResolverRegionTests.cs` (new) | Two matches, the better one outside: the tap point comes from the inside match, in full-capture pixels (US2, FR-006). Image not inside: "not found" and no tap. Empty clip: "not found" and no error (FR-007). Both selection strategies. |
| `tests\unit\Parameters\RegionCopyTests.cs` (new) | `TryResolveImage` and `TryDetection` keep the region (one case per target kind, including the readiness image). |
| `tests\unit\Sequences\ImageConditionRegionEvaluationTests.cs` (new) | `SequenceStepConditionEvaluator` carries the region to the adapters. The `Describe` and `DescribeBreakCondition` text with and without a region (FR-012). The text with no region equals the old text. |
| `tests\unit\StepThrough\RegionDescriptionTests.cs` (new) | The step-through output shows `region=x,y,width,height`. |
| `tests\contract\Sequences\RegionImageConditionContractTests.cs` (new) | Save and read back an `imageVisible` condition with a region: top-level, in `all`, `any`, `none`, in an `if`, in a loop condition, and as a break condition. A condition with no region has no `region` field. Bad regions give 400 with every invalid field named, and nothing is stored (US4, SC-004). |
| `tests\contract\Sequences\RegionPayloadContractTests.cs` (new) | `waitForImage` and `primitiveTap` payload `detectionTarget.region`: read back, and 400 for a bad region. |
| `tests\contract\Commands\RegionDetectionTargetContractTests.cs` (new) | Commands: `detectionTarget`, command-level `detection`, and `ensureGameRunning.readinessImage` keep the region. Bad regions give 400. Steps endpoints too. |
| `tests\contract\Sequences\RegionOpenApiTests.cs` (new) | `swagger.json` lists `region` on both objects and the `PixelRegion` schema (FR-010). |
| Existing tests for conditions and targets | No change to expected results (SC-003). The tasks run them as a regression check. |

Test order: the first tests (`PixelRegionTests`, then the evaluator and resolver tests) are written before the code.

### Docs

`docs\architecture.md` (the only file in `docs` that mentions `imageVisible` or `detectionTarget`), `CHANGELOG.md`,
`specs\STATUS.md`, and the `Status` line in `specs\130-region-restricted-image-detection\spec.md`. If the repository
keeps a version override file for a feature release, follow the pattern of the last feature. All new text is STE.

## Design Notes

### One rule, one place (D1)

`PixelRegion.Validate(prefix)` is the only code that knows the rule of FR-004. The endpoint maps, the domain
validators, and the repository guard call it. Each returns the messages as 400 errors. A test fails if a second copy
of the rule exists: the contract tests send the same bad region to every place and compare the messages.

### Two region types

`Blocks.Condition.Region` and `ImageMatchParams.Region` are fractions (0 to 1). `PixelRegion` is in pixels. The
evaluator uses `PixelRegion` when it is set. The fraction `Region` is used as before when `PixelRegion` is null. A
unit test checks the order (see the test table).

### Where the crop happens

A tap target crops in `DetectionCoordinateResolver`, because `primitiveTap`, `waitForImage`, and the readiness image
all use it (R3). A condition crops in `ImageMatchEvaluator`, because the fraction crop is already there (R5). The
two places use the same `ClipTo` method.

## Task authoring rules

The tasks command MUST follow these rules when it makes `tasks.md` from this plan and from the "Planning notes" in the
spec.

1. A task is marked [P] only if no other task in the same phase edits the same file. Tasks that edit the same file
   (for example `SequencesEndpoints.cs`, `MapPerStepCondition`, or one test file) are in sequence. Order is given by
   task order and phase. A task text does not say "sequential after ...". User Story 4 tasks run after User Story 1
   and User Story 2.
2. Every task names an exact path from the table above. No task says "find the file".
3. Tasks include early check tasks for the legacy class, the `primitiveTap` payload, `ImageDetectionHelper.cs`, and
   `ImageDetectionConditionAdapter.cs`. Each check records its result in the section "Setup check results" of
   `research.md`. Each check ends in a named change or in "record no change" with the reason.
4. Tasks include a conditional task in the User Story 1 phase: "If the check finds a read or write path for the legacy
   class `ImageVisibleCondition`, map and validate `Region` there".
5. Tasks include one task for the command-level `detection` and one task for the `ensureGameRunning` readiness image.
   Each maps, validates, stores, and returns the region.
6. Tasks include the `primitiveTap` payload validation through `PixelRegion.Validate`, with a contract test for a 400.
7. Tasks include the unit test for the two region types (`PixelRegion` wins over the fraction `Region`).
8. The docs tasks name `docs\architecture.md`.

## Complexity Tracking

No constitution violation. This table is not used.

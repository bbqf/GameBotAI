# Tasks: Resolve parameter placeholders in inline WaitForImage

**Input**: Design documents from `/specs/186-fix-waitforimage-placeholder/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/
**Tests**: Required. Constitution Principle II: a bug fix needs a failing test before the fix.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no open dependency).
- **[Story]**: US1 = single-value placeholder, US2 = multi-value placeholder, US3 = `matchedImageId` in the log.

## Phase 1: Setup

- [ ] T001 Run `dotnet build` for `C:\src\GameBot\GameBot.sln` and run `dotnet test C:\src\GameBot\GameBot.sln --filter FullyQualifiedName~WaitForImage`. Record that the baseline is green.

## Phase 2: Foundational (blocks all stories)

- [ ] T002 Add the public method `TryResolveWaitForImage(WaitForImageConfig, ParameterScope, out WaitForImageConfig?, out ParameterResolutionError?)` to `C:\src\GameBot\src\GameBot.Domain\Parameters\CommandStepResolver.cs`. It builds a temporary WaitForImage `CommandStep` and calls `TryResolve`. Add XML doc comments in STE.
- [ ] T003 [P] Add resolver unit tests for `TryResolveWaitForImage` (single placeholder, multi-value placeholder, literal id, unknown name) in `C:\src\GameBot\tests\unit\Parameters\CommandStepResolverWaitForImageTests.cs`.

**Checkpoint**: the resolver method works for all four inputs.

## Phase 3: User Story 1 - Single-value image parameter (Priority: P1)

**Goal**: An inline WaitForImage step with `{{s}}` resolves and detects the image.
**Independent Test**: Run the sequence with `s` = `pns-city-anchor`. The step ends with `image_detected`.

- [ ] T004 [US1] Write a failing unit test in `C:\src\GameBot\tests\unit\Sequences\SequenceRunnerWaitForImageTests.cs`: an inline step with `{{s}}` gives `image_detected`, and `ReferenceImageId` in the details is the resolved id. Run it and see it fail.
- [ ] T005 [US1] In `C:\src\GameBot\src\GameBot.Domain\Services\SequenceRunner.cs`, resolve `step.WaitForImage` in `ExecuteSingleStepAsync` (after `ApplyScope`, for a WaitForImage step) with `TryResolveWaitForImage` and the scope of the step. Pass the resolved config to `RecordWaitForImageStepAsync`. Keep steps without a placeholder on the fast path. Also check the form where the WaitForImage config comes from an `Action` payload of type WaitForImage. If that form does not reach the resolver, resolve it too, and add a test.
- [ ] T006 [US1] On a resolution error in `C:\src\GameBot\src\GameBot.Domain\Services\SequenceRunner.cs`, fail the step with `ParameterResolutionError.ToMessage(stepKey)`. Use the same `result.Fail` path and step status as a failed guard, so the sequence stops. Do not wait. Do not give `image_unavailable`. Add a unit test for an unknown parameter in `C:\src\GameBot\tests\unit\Sequences\SequenceRunnerWaitForImageTests.cs`. The test checks the step status, the message, and the sequence status.
- [ ] T007 [US1] Do the resolution before the dry-run check (research R5). Add tests in `C:\src\GameBot\tests\unit\Sequences\SequenceRunnerWaitForImageTests.cs`: a dry run with a valid placeholder passes; a dry run with an unknown name gives the same error as a real run; a step in a loop body and in an if branch resolves against its own scope.

**Checkpoint**: US1 passes alone.

## Phase 4: User Story 2 - Multi-value image parameter (Priority: P1)

**Goal**: `{{q}}` has the same effect as `referenceImageIds`.
**Independent Test**: `q` = both images, one on the screen. The step ends with `image_detected`.

- [ ] T008 [US2] Write a failing unit test in `C:\src\GameBot\tests\unit\Sequences\SequenceRunnerWaitForImageTests.cs`: `{{q}}` with two values, only the second visible, gives `image_detected` and `MatchedImageId` = the second id.
- [ ] T009 [US2] Confirm that T005 makes T008 pass. If it does not, fix the list handling in `C:\src\GameBot\src\GameBot.Domain\Services\SequenceRunner.cs` (`WaitForImageAsync`) so a resolved list uses the list path.
- [ ] T010 [US2] Add an edge-case test: a multi-value placeholder with one value behaves as one image; a `fieldTemplates` placeholder resolves like the inline field; a resolved value that is not on the screen gives `timeout_elapsed`; a resolved id of a missing image gives `image_unavailable`. File: `C:\src\GameBot\tests\unit\Sequences\SequenceRunnerWaitForImageTests.cs`.

**Checkpoint**: US1 and US2 pass.

## Phase 5: User Story 3 - `matchedImageId` in the step log (Priority: P2)

**Goal**: `detailAttributes` shows `matchedImageId`.
**Independent Test**: A list with only the second image visible gives `matchedImageId` = the second id in the log.

- [ ] T011 [US3] Write a failing test that reads the execution log of a sequence run and expects `detailAttributes.matchedImageId`, in `C:\src\GameBot\tests\integration\Sequences\WaitForImageSequenceExecutionIntegrationTests.cs`. Cover the `referenceImageIds` list case and the `{{q}}` case.
- [ ] T012 [P] [US3] Add `MatchedImageId` (optional, last parameter) to `WaitForImageDetailAttributes` in `C:\src\GameBot\src\GameBot.Domain\Logging\ExecutionLogModels.cs`.
- [ ] T013 [P] [US3] Add `MatchedImageId` to `ExecutionLogWaitForImageDetailAttributesDto` in `C:\src\GameBot\src\GameBot.Service\Models\ExecutionLogs.cs`. Omit the field from JSON when null.
- [ ] T014 [US3] Fill `MatchedImageId` in both `BuildWaitForImageDetailAttributes` builders in `C:\src\GameBot\src\GameBot.Service\Services\ExecutionLog\ExecutionLogService.cs` (read the attribute `matchedImageId`, and `outcome.MatchedImageId`).
- [ ] T015 [US3] Copy `MatchedImageId` in both `DetailAttributes` maps (the tree node map and the detail response map) in `C:\src\GameBot\src\GameBot.Service\Endpoints\ExecutionLogsEndpoints.cs`.
- [ ] T016 [P] [US3] Add optional `matchedImageId` to `ExecutionLogWaitForImageDetailAttributesDto` in `C:\src\GameBot\src\web-ui\src\services\executionLogsApi.ts` and a test in `C:\src\GameBot\src\web-ui\src\services\__tests__\executionLogsApi.spec.ts`.

**Checkpoint**: all stories pass.

## Phase 6: Polish

- [ ] T017 [P] Update `C:\src\GameBot\docs\architecture.md` where it describes WaitForImage resolution or log fields. Refresh "Last reviewed".
- [ ] T018 [P] Add the `Status` line to `C:\src\GameBot\specs\186-fix-waitforimage-placeholder\spec.md` and update `C:\src\GameBot\specs\STATUS.md`. Mark features 114, 180, and 181 as iterated by 186 where the convention needs it.
- [ ] T019 [P] Check the Swagger descriptions in `C:\src\GameBot\src\GameBot.Service\Swagger\ParametrizedReferenceImageSchemaFilter.cs` still match the behavior. Add a description for the new log field if the log DTO is in the schema.
- [ ] T020 Add a `CHANGELOG.md` entry in STE (fix for issue 386). Bump `patch` in `C:\src\GameBot\installer\versioning\version.override.json` as the CLAUDE.md rule says.
- [ ] T021 Run the full affected test projects (unit, integration, contract) and `npm test` in `C:\src\GameBot\src\web-ui`. All must pass. Run the quickstart in `C:\src\GameBot\specs\186-fix-waitforimage-placeholder\quickstart.md`.

## Dependencies

- T001 first. T002 before T005. T003 can run with T004.
- US1 (T004-T007) before US2 (T008-T010), because US2 uses the same runner change.
- US3 (T011-T016) is independent of US1 and US2 in code. T011 needs T012-T015 to pass.
- Polish after all stories.

## Parallel examples

- T003 with T004; T012, T013, and T016 together; T017, T018, and T019 together.

## Implementation strategy

MVP: Phases 1-3 (US1). Then US2 (same change, more tests). Then US3. Commit after each phase when tests are green.

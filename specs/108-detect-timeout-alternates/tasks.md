# Tasks: Detect does not report a time-limited measurement as an absence

**Input**: Design documents from `specs/108-detect-timeout-alternates/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/detect-timeout.md, quickstart.md

**Tests**: Required. The fix is for a defect, so each story has a regression test. Write the tests before the fix, and make sure that they fail before the fix.

**Format**: `[ID] [P?] [Story] Description`. `[P]` means that the task can run in parallel with other `[P]` tasks (different files, no dependencies).

## Phase 1: Setup

- [ ] T001 Read `DetectAsync` in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs` and `MatchAllAsync` in `src/GameBot.Domain/Vision/ReferenceSetTemplateMatcher.cs`. Make sure that the root cause in research R-001 and R-002 is correct.

## Phase 2: Foundational (blocks all stories)

- [ ] T002 In `src/GameBot.Service/Endpoints/ImageDetectionsValidation.cs`, add `public static TimeSpan DetectionTimeLimit(int timeoutMs, int referenceCount)`. It returns `max(1, timeoutMs) × max(1, referenceCount)` ms, calculated in `long` and capped at `int.MaxValue` ms (data-model.md). Add an STE XML comment that names issue #223.
- [ ] T003 In `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.Logging.cs`, add `LogDetectTimeLimitExpired(this ILogger logger, string Id, int ReferenceCount, long LimitMs, long DurationMs)` with `EventId = 11007`, `LogLevel.Warning`, and the message in data-model.md.

**Checkpoint**: The solution builds. No behavior change.

## Phase 3: User Story 1 - A 200 with no matches is a real absence (Priority: P1)

**Goal**: When the time limit expires, `detect` returns `504 detection_timeout` and never a `200`.

**Independent Test**: A matcher that does not complete before the time limit gives `504 detection_timeout`.

### Tests for User Story 1

- [ ] T004 [P] [US1] Create `tests/integration/DetectTimeLimitIntegrationTests.cs` (collection `ConfigIsolation`). Use `WebApplicationFactory<Program>.WithWebHostBuilder(b => b.ConfigureTestServices(...))`. Replace `ITemplateMatcher` with a test matcher that waits (`Task.Delay`, with the cancellation token) and then calls the real `TemplateMatcher`, and set `DetectionOptions.TimeoutMs` with `PostConfigure`. Use the screen and templates of `AlternatesFixtures` (`tests/integration/ImageAlternatesIntegrationTests.cs`). Test: with a matcher that waits until cancellation and `TimeoutMs` 100, `POST /api/images/detect` for an image without alternates returns `504`, the body `code` is `detection_timeout`, the `message` is not empty, and the body has no `matches` property.
- [ ] T005 [US1] In `tests/integration/ImageDetectionsStressTests.cs`, remove `TimeoutReturnsOkWithLimitsHit`. It records the defect as the expected behaviour, and T004 replaces it (research R-006).

### Implementation for User Story 1

- [ ] T006 [US1] In `DetectAsync` in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`, change `catch (OperationCanceledException)` to `catch (OperationCanceledException) when (!ct.IsCancellationRequested)`. In it, call `LogDetectTimeLimitExpired`, keep `ImageDetectionsMetrics.Record(elapsedMs, 0)`, and return `Results.Json(new { code = "detection_timeout", message = "..." }, statusCode: StatusCodes.Status504GatewayTimeout)` with the message in contracts/detect-timeout.md. Remove the empty `DetectResponse`. Add an STE code comment that names issue #223. The log entry (FR-006) has no automated test, because log text is not an API contract; the review of this task verifies it.

**Checkpoint**: T004 passes.

## Phase 4: User Story 2 - An image with alternates gets the same time for each reference (Priority: P1)

**Goal**: The time limit of a call is `TimeoutMs` for each reference.

**Independent Test**: The time limit calculation, and a detect call on an image with two alternates where each reference takes half of `TimeoutMs`.

### Tests for User Story 2

- [ ] T007 [P] [US2] Create `tests/unit/Images/DetectionTimeLimitTests.cs`. Tests: `(500, 1)` gives 500 ms; `(500, 3)` gives 1500 ms; `(0, 3)` gives 3 ms; `(500, 0)` gives 500 ms; `(int.MaxValue, 8)` gives `int.MaxValue` ms.
- [ ] T008 [US2] In `tests/integration/DetectTimeLimitIntegrationTests.cs`, add a test: `TimeoutMs` 500, a matcher that waits 250 ms for each reference, image `anchor` (day template) with alternates `anchor-n1` (other template) and `anchor-night` (night template). `POST /api/images/detect` returns `200` with one match whose `matchedReferenceId` is `anchor-night`. Before the fix, the call returned a `200` with an empty `matches` array and `limitsHit: true`.
- [ ] T009 [US2] In `tests/integration/DetectTimeLimitIntegrationTests.cs`, add a test: `TimeoutMs` 250, a matcher that waits 300 ms for each reference, image `anchor` with alternates `anchor-night` and `gone`. Upload `gone`, set the alternates, then delete `gone` (the method of `DeletedAlternateIsSkipped` in `ImageAlternatesIntegrationTests`). Two references load, so the limit is 500 ms and the work is at least 600 ms. The call returns `504 detection_timeout`. If the missing alternate counted, the limit is 750 ms and the call returns `200`. This test verifies that the limit counts only the references that loaded (spec US2 scenario 3).

### Implementation for User Story 2

- [ ] T010 [US2] In `DetectAsync` in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`, calculate `var referenceCount = 1 + referenceSet.Alternates.Count;` and `var timeLimit = ImageDetectionsValidation.DetectionTimeLimit(detOpts.Value.TimeoutMs, referenceCount);`. Use `timeoutCts.CancelAfter(timeLimit)`. Pass `referenceCount` and `timeLimit` to `LogDetectTimeLimitExpired`.

**Checkpoint**: T007, T008 and T009 pass. T004 still passes. The existing tests in `ImageAlternatesIntegrationTests` and `ImageDetectionsStressTests` still pass.

## Phase 5: User Story 3 - The API description tells the truth (Priority: P2)

**Goal**: The OpenAPI document describes the `504 detection_timeout` failure and the meaning of `limitsHit`.

**Independent Test**: Read `/swagger/v1/swagger.json`.

### Tests for User Story 3

- [ ] T011 [P] [US3] Create `tests/contract/Images/DetectTimeLimitOpenApiTests.cs`, in the style of `tests/contract/Images/ImageAlternatesOpenApiTests.cs`. Test: the `post` operation of `/api/images/detect` has a `504` response whose `application/json` example has `code` `detection_timeout`. Test: the operation description contains `detection_timeout` and `limitsHit`.

### Implementation for User Story 3

- [ ] T012 [US3] In `src/GameBot.Service/Swagger/SwaggerConfig.cs`, add `SetResponseExample(operation, "504", ImageDetectError("detection_timeout", "<message of T006>"), context);` to `SetImageDetectErrorExamples`. Add STE sentences to `ImageDetectDescription`: the time limit is `TimeoutMs` for each reference (the image and each alternate); when it expires, the call fails with `504 detection_timeout`; `limitsHit` means only that `maxResults` cut the list of matches.
- [ ] T013 [US3] In `src/GameBot.Service/Endpoints/Dto/ImageDetectionsDtos.cs`, add an STE XML `<summary>` to `DetectResponse.LimitsHit`: true when `maxResults` cut the list of matches; a time limit failure is a `504`, not this flag.

**Checkpoint**: T011 passes.

## Phase 6: Polish and cross-cutting concerns

- [ ] T014 [P] In `README.md`, in the detect section, add the `504 detection_timeout` failure, and change the `TimeoutMs` text: the limit applies to each reference (the image and each alternate).
- [ ] T015 [P] In `docs/architecture.md`, add the `504 detection_timeout` failure and the time limit rule to the `POST /api/images/detect` notes, and refresh the "Last reviewed" date.
- [ ] T016 [P] Add a "Fixed" entry for #223 under `## [Unreleased]` in `CHANGELOG.md`, in STE.
- [ ] T017 [P] Add row `| 108 | Detect does not report a time-limited measurement as an absence | Implemented |` to the table in `specs/STATUS.md`, and set `**Status**: Implemented` in `specs/108-detect-timeout-alternates/spec.md`.
- [ ] T018 Build with `dotnet build GameBot.sln -c Release -warnaserror`, and run the unit, integration and contract tests of the affected areas (`DetectionTimeLimit`, `DetectTimeLimit`, `ImageDetections`, `ImageAlternates`, `DetectImage`). Also run the sequence and command tests (`Sequences`, `Commands`, `ImageMatchEvaluator`), which verify that the consumers in FR-008 do not change. When the local SDK is not available, rely on CI, which runs all tests.

## Dependencies

- T002 and T003 block T006 and T010.
- T004 before T006 (test first). T007, T008, T009 before T010. T011 before T012 and T013.
- T006 and T010 change the same method: do them in sequence.
- Phase 6 after phases 3 to 5.

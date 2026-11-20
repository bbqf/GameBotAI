---

description: "Task list for feature 197: do not read a stale capture frame in a step (B-043)"
---

# Tasks: Do not read a stale capture frame in a step (B-043)

**Input**: Design documents from `specs/197-fix-stale-capture-frame/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/frame-freshness.md, quickstart.md

**Tests**: Required. Constitution Principle II: a bug fix MUST include a test that fails before the fix. Each user story has its tests first.

**Organization**: Tasks are in groups by user story. Each story can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an open task)
- **[Story]**: The user story of the task (US1, US2, US3, US4)
- All paths are relative to the repository root `C:\src\GameBot`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make sure that the start point is green.

- [X] T001 Build `GameBot.sln` and run `tests/unit/GameBot.UnitTests.csproj` on the branch before a change, and record the result (constitution: red builds block the work)

---

## Phase 2: Foundational (Prerequisites for All Stories)

**Purpose**: The frame data, the freshness rule, the ambient scope, and the stale-repeat rule of the capture loop. All user stories need them.

**⚠️ CRITICAL**: No user story work can start until this phase is complete.

- [X] T002 [P] Write unit tests for `FrameFreshness` (age at 0, age below and above `limitMs`, stale repeat with age 0, negative time gives age 0, `EffectiveLimitMs(5000, 500)` = 5000, `EffectiveLimitMs(1000, 2000)` = 4000) in `tests/unit/Sessions/FrameFreshnessTests.cs`
- [X] T003 [P] Write unit tests for `FrameReadScope` (no scope: `Admit` is true and records nothing; scope: fresh frame admitted and `LastRead` set; stale frame refused and `LastRead.Stale` true; nested push and dispose restore; `EnsureFreshAsync` with no delegate is true) in `tests/unit/Sessions/FrameReadScopeTests.cs`
- [X] T004 [P] Add `FrameMaxAgeMs` (default 5000) and `MinFrameMaxAgeMs` (1000) to `DeviceLivenessOptions`, and apply the minimum in `Normalized()`, in `src/GameBot.Domain/Sessions/DeviceLivenessOptions.cs`; add a test for the default and the minimum to the current file `tests/unit/Sessions/DeviceLivenessOptionsTests.cs`
- [X] T005 Add `FrameId` (init property, default `Guid.NewGuid().ToString("N")`), `StaleRepeat` (default false), and `StaleRepeatSince` (default null) to `CachedFrame` in `src/GameBot.Domain/Sessions/CachedFrame.cs`; keep the positional constructor so that the current callers compile
- [X] T006 [P] Create the record `FrameReadInfo(string FrameId, long AgeMs, bool Stale)` in `src/GameBot.Domain/Sessions/FrameReadInfo.cs`
- [X] T007 Create the static class `FrameFreshness` with `EffectiveLimitMs`, `AgeMs`, `IsStale`, and `Describe` (data-model section 2) in `src/GameBot.Domain/Sessions/FrameFreshness.cs` (depends on T005, T006; makes T002 pass)
- [X] T008 Create `FrameReadScope` (data-model section 4: `MaxAgeMs`, a `TimeProvider` clock, `LastRead`, an optional wait delegate `Func<CancellationToken, Task<bool>>`, `Current`, `Push`, `Admit`, `EnsureFreshAsync`) in `src/GameBot.Domain/Sessions/FrameReadScope.cs` (depends on T007; makes T003 pass)
- [X] T009 Write unit tests for the capture loop in `tests/unit/BackgroundScreenCaptureServiceTests.cs`: a new frame has a new `FrameId`; same bytes after a time-out keep the `FrameId` and the `Timestamp` and set `StaleRepeat`; different bytes clear the flag and give a new id; same bytes with no failure before them give a new id and no flag (still screen); repeats for longer than `FrameMaxAgeMs` give a new id and clear the flag; a stale repeat does not call `IDeviceLivenessTracker.RecordCapture`; the shared `Bitmap` of a stale repeat is not disposed; after `StartCapture` restarts the loop of the session, the first same-byte capture is a normal new frame (the stale-repeat state starts from nothing)
- [X] T010 Implement the stale-repeat rule (data-model section 1, research R-003) in `SessionCaptureLoop.RunLoopAsync` in `src/GameBot.Emulator/Session/BackgroundScreenCaptureService.cs`: add `_lastCaptureFailed`, read `FrameMaxAgeMs` from the normalized options in the `BackgroundScreenCaptureService` constructor and pass it to the loop, use `FrameFreshness.EffectiveLimitMs(FrameMaxAgeMs, _intervalMs)` as the repeat window, skip `RecordCapture` on a stale repeat, and dispose the old `Bitmap` only when the new frame has a different instance (depends on T005, T009; makes T009 pass)
- [X] T011 Call `FrameReadScope.Admit(frame)` in `SessionScopedScreenSource.DecodeCachedFrame` in `src/GameBot.Emulator/Session/SessionScopedScreenSource.cs` and in `BackgroundCaptureSessionFrameSource.Capture` in `src/GameBot.Service/Services/SequenceExecution/BackgroundCaptureSessionFrameSource.cs`; return null when `Admit` is false (depends on T008)
- [X] T012 Push a `FrameReadScope` in `SequenceExecutionService.ExecuteCoreAsync` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` after the device scope, when the run is not a dry-run, a session id is known, and `BackgroundScreenCaptureService` is available. Add `IOptions<DeviceLivenessOptions>?` and `AppConfig?` as optional constructor parameters, and register them in the DI setup if the service does not get them yet (research R-010a). Set `MaxAgeMs` to `FrameFreshness.EffectiveLimitMs(options.FrameMaxAgeMs, appConfig.CaptureIntervalMs)` (500 ms when `AppConfig` is null). Make the wait delegate: read `GetCachedFrame(sessionId)` each `CaptureIntervalMs` until `FrameFreshness.IsStale` is false, no frame exists, or `MaxAgeMs` passed (depends on T008)

**Checkpoint**: The foundation is ready. The user stories can start.

---

## Phase 3: User Story 1 - A step does not count a stale frame as a fresh read (Priority: P1) 🎯 MVP

**Goal**: A `WaitForImage` pass, an `imageVisible` read, and an `ocrText` read do not decide on a stale frame (FR-004, FR-005, FR-006, FR-013).

**Independent Test**: A test frame source stops to give new frames, then gives a fresh frame that shows the image. The `WaitForImage` step does not end on a stale pass and ends with `image_detected` on the first fresh frame.

### Tests for User Story 1 ⚠️

> Write these tests first. Make sure that they FAIL before the implementation.

- [X] T013 [P] [US1] Write unit tests in `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs` with a `FrameReadScope` that uses a `FakeTimeProvider`. The test image evaluator calls `FrameReadScope.Admit` with a test `CachedFrame` (its `Timestamp` controls the age), and returns false when `Admit` is false. Cases: (a) the stale frame shows the image, so the step does not end with `image_detected` on it (this test fails before the fix, spec US1 scenario 6); (b) stale passes, then a fresh pass with the image, give `image_detected`; (c) only stale passes give `timeout_elapsed` (status `success`); (d) in `until: absent` mode a stale pass is not an absent capture
- [X] T014 [P] [US1] Write unit tests in `tests/unit/Sequences/ImageVisibleStaleFrameTests.cs`. Use the same test setup as T013 (`FakeTimeProvider`, test `CachedFrame`, an image evaluator that calls `FrameReadScope.Admit`, and a test wait delegate). Cases: (a) a negated `imageVisible` on a stale frame with no image fails the step with `stale_frame` and is never true (this test fails before the fix, spec US1 scenario 7); (b) a stale frame that becomes fresh in the wait gives the normal result; (c) a frame that stays stale throws `ConditionEvaluationException` with kind `StaleFrame` and a message with `stale_frame`; (d) a frame that becomes stale between the wait and the read also throws `StaleFrame`; (e) no frame at all keeps the current result (false)
- [X] T015 [P] [US1] Write unit tests in `tests/unit/Sequences/OcrTextConditionEvaluatorTests.cs`: a frame that stays stale adds one record with the reason `stale-frame` and then throws `ConditionEvaluationException` with kind `StaleFrame`; a fresh frame gives the current result
- [X] T016 [P] [US1] Write an integration test in `tests/integration/Sequences/WaitForImageStaleFrameRunTests.cs` with a fake capture provider. Case 1: the provider gives a frame that shows the image, then each capture times out. Start the `WaitForImage` step only after the frame is older than the effective limit. Expect `timeout_elapsed` and not `image_detected` (this case fails before the fix). Case 2: the provider stalls, then gives a fresh frame with the image. Expect `image_detected`.

### Implementation for User Story 1

- [X] T017 [US1] Add `FrameReadInfo? Frame = null` to `ImageDetectionPass` in `src/GameBot.Domain/Vision/ImageDetectionPassScope.cs`, and set it from `FrameReadScope.Current?.LastRead` in both `LastPass` writes of `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`
- [X] T018 [US1] Count the stale passes in `PollImageConditionAsync` and `PollAbsentConditionAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs` (read `ImageDetectionPassScope.Current?.LastPass?.Frame?.Stale` after each evaluator call; clear `LastPass` before each pass in present mode too), and return the stale count with the pass count (depends on T017)
- [X] T019 [US1] Add the kind `StaleFrame` to `ConditionEvaluationFailureKind`, and call `FrameReadScope.Current?.EnsureFreshAsync(ct)` before the screen read of the `ImageVisibleStepCondition` and `OcrTextStepCondition` leaves in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`; when it is false, throw `ConditionEvaluationException(StaleFrame, ...)` with the message of contract section 5. After each screen read of the leaf, also throw `StaleFrame` when `FrameReadScope.Current?.LastRead?.Stale` is true (research R-006). For `ocrText`, first add the record with the reason `stale-frame` (depends on T020)
- [X] T020 [P] [US1] Add `OcrTextFailureReasons.StaleFrame = "stale-frame"` in `src/GameBot.Domain/Services/OcrTextEvaluationScope.cs`
- [X] T020a [P] [US1] Write a unit test in `tests/unit/Commands/CommandExecutorWaitForImageTests.cs`: in a `FrameReadScope`, a command `WaitForImage` on a stale frame that shows the image does not give a detection, and the pass counts as a pass with no frame (spec edge case "Other readers in a step"; contract section 5a). Change `src/GameBot.Service/Services/CommandExecutor.cs` only if the test fails
- [X] T021 [US1] Make sure that `SequenceRunner` maps the kind `StaleFrame` to the failure message `Step '<stepKey>' condition evaluation failed: stale_frame: ...` in the `catch (ConditionEvaluationException ex)` block of `src/GameBot.Domain/Services/SequenceRunner.cs` (the default branch already gives this text; add a test assertion, change code only if it is necessary)

**Checkpoint**: User Story 1 works and is tested alone.

---

## Phase 4: User Story 2 - The step log shows which frame the step read (Priority: P2)

**Goal**: The step details and the condition log lines give the capture id, the frame age, the score or text, and the stale data (FR-007, FR-008).

**Independent Test**: Run a `WaitForImage` step, an `imageVisible` condition, and an `ocrText` condition with a test frame source, and read the execution log.

### Tests for User Story 2 ⚠️

- [X] T022 [P] [US2] Write unit tests in `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs` for the six new fields of `WaitForImageStepResultDetails` (`CaptureId`, `FrameAgeMs`, `BestScore`, `PassCount`, `StalePassCount`, `StaleReason`) on `image_detected`, `timeout_elapsed` with a fresh last pass (`StaleReason` null), and `timeout_elapsed` with a stale last pass (`StaleReason` = `stale_frame`)
- [X] T023 [P] [US2] Write unit tests in `tests/unit/Sequences/ImageVisibleStaleFrameTests.cs` for the `imageVisible` log line `imageVisible(<id>) capture=<frameId> ageMs=<n> score=<s>` and the form with no frame data
- [X] T024 [P] [US2] Write unit tests in `tests/unit/Sequences/OcrTextConditionEvaluatorTests.cs` and `tests/unit/Services/Ocr/OcrTextConditionReaderTests.cs` for ` capture=<id> ageMs=<n>` at the end of the `ocrText` line, and for no change of the line when no frame data exists
- [X] T025 [P] [US2] Write an integration test in `tests/integration/Sequences/WaitForImageStaleFrameRunTests.cs` that reads the execution log of the run and checks the keys `captureId`, `frameAgeMs`, `bestScore`, `passCount`, `stalePassCount`, `staleReason` of the `waitForImage` step entry and its `detailAttributes`

### Implementation for User Story 2

- [X] T026 [US2] Add the six fields to `WaitForImageStepResultDetails` (each with `JsonIgnore` when null) and fill them in `WaitForImageAsync` on each exit path that polls, in `src/GameBot.Domain/Services/SequenceRunner.cs`; add the stale count to `LogWaitForImageTimeout` (depends on T018)
- [X] T027 [US2] Write the keys `captureId`, `frameAgeMs`, `bestScore`, `passCount`, `stalePassCount`, `staleReason` into the step detail item in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` (depends on T026)
- [X] T028 [US2] Add the six fields to `WaitForImageDetailAttributes` in `src/GameBot.Domain/Logging/ExecutionLogModels.cs` (optional parameters at the end), map them in both `BuildWaitForImageDetailAttributes` methods in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` (add the fields to `PrimitiveTapStepOutcome` only if the compiler needs it), add them to `ExecutionLogWaitForImageDetailAttributesDto` in `src/GameBot.Service/Models/ExecutionLogs.cs` (each with `JsonIgnore` when null), and map them in both places in `src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs` (depends on T027)
- [X] T029 [US2] Push an `ImageDetectionPassScope` around each image check of the `imageVisible` leaf and add the log line to `CommandStepStateScope.Current` in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`. `NoteImageMatch` must read the score from the pass of the inner scope (depends on T017)
- [X] T030 [US2] Add `CaptureId` and `FrameAgeMs` to `OcrTextReadResult` and `OcrTextEvaluationRecord`, and add ` capture=<id> ageMs=<n>` to `ToLogLine` when `CaptureId` is set, in `src/GameBot.Domain/Services/OcrTextEvaluationScope.cs`; fill them in `src/GameBot.Service/Services/Ocr/OcrTextConditionReader.cs` from `FrameReadScope.Current?.LastRead`, and pass them to the records in `EvaluateOcrTextAsync` in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs` (depends on T020)

**Checkpoint**: User Stories 1 and 2 work.

---

## Phase 5: User Story 3 - The screenshot endpoint does not hide a stale frame (Priority: P2)

**Goal**: `X-Capture-Id` is the frame id; a stale frame has `X-Capture-Stale: true` (FR-009, FR-010).

**Independent Test**: Two calls with no new capture give the same id; an old frame gives `X-Capture-Stale: true`.

### Tests for User Story 3 ⚠️

- [X] T031 [P] [US3] Write integration tests in `tests/integration/EmulatorScreenshotCaptureTests.cs`: two calls with the same cached frame give the same `X-Capture-Id` (fails before the fix); a new frame gives a new id; a frame older than `FrameMaxAgeMs` gives `X-Capture-Stale: true`; a stale-repeat frame gives `X-Capture-Stale: true` and the old id; `POST /api/images/crop` with the frame id works
- [X] T032 [P] [US3] Write unit tests for `CaptureSessionStore.GetOrAdd` (same id returns the stored capture; new id stores; trim rule unchanged) in the new file `tests/unit/Services/CaptureSessionStoreTests.cs`

### Implementation for User Story 3

- [X] T033 [US3] Add `GetOrAdd(string id, byte[] png)` to `src/GameBot.Service/Services/CaptureSessionStore.cs`
- [X] T034 [US3] In the cached-frame path of `GET /api/emulator/screenshot` in `src/GameBot.Service/Endpoints/EmulatorImageEndpoints.cs`, call `captures.GetOrAdd(frame.FrameId, frame.PngBytes)`, write the frame id as `X-Capture-Id`, and in `ApplyCachedFrameHeaders` set the stale flag to `report stale || FrameFreshness.IsStale(frame, now, FrameFreshness.EffectiveLimitMs(liveness.Options.FrameMaxAgeMs, appConfig.CaptureIntervalMs))`; get `AppConfig` from the request services (depends on T033)
- [X] T035 [US3] Update the doc comment of `CaptureHeaders.CaptureId` and `CaptureHeaders.Stale` in `src/GameBot.Service/Services/Liveness/CaptureHeaders.cs`, and the header descriptions in `src/GameBot.Service/Swagger/SwaggerConfig.cs` where they describe `X-Capture-Id` or `X-Capture-Stale`

**Checkpoint**: User Stories 1, 2, and 3 work.

---

## Phase 6: User Story 4 - The liveness check sees a stall while a step runs (Priority: P3)

**Goal**: A stale repeat is not a completed capture for `capture_stalled`; the documentation states that the watch runs during a step (FR-011, FR-012).

**Independent Test**: With `FrameMaxAgeMs` equal to or more than the stall limit, a fake capture provider times out once, then returns the same bytes for longer than the stall limit; the liveness report gives `not_live` with `capture_stalled`.

### Tests for User Story 4 ⚠️

- [X] T036 [P] [US4] Write a unit test in `tests/unit/BackgroundScreenCaptureServiceTests.cs` with a real `DeviceLivenessTracker` and a `FakeTimeProvider`: after one time-out, same-byte captures for longer than `CaptureStallLimitMs` (with `FrameMaxAgeMs` set above the stall limit for the test) give `capture_stalled` from `DeviceLivenessEvaluator.Evaluate` (spec FR-011 and SC-005 name this setup); a capture with different bytes gives `live` again
- [X] T037 [P] [US4] Write a unit test in the current file `tests/unit/Queues/QueueLivenessWatchTests.cs` that shows that `CheckOnceAsync` observes `capture_stalled` while a sequence step of the run runs (the watch does not wait for the next sequence start)

### Implementation for User Story 4

- [X] T038 [US4] Confirm that T010 skips `RecordCapture` on a stale repeat, so that T036 passes; change `src/GameBot.Emulator/Session/BackgroundScreenCaptureService.cs` only if T036 fails
- [X] T039 [US4] Update the schema text in `src/GameBot.Service/Swagger/DeviceLivenessSchemaFilter.cs`: the periodic liveness watch of a queue run runs every `QueueCheckIntervalMs` also while a step runs; a stale-repeat capture is not a completed capture for `capture_stalled`; a step does not use a frame older than `FrameMaxAgeMs`

**Checkpoint**: All user stories work.

---

## Phase 7: Polish and Work for All Stories

**Purpose**: Documentation, full test run, and version data.

- [X] T040 [P] Update `docs/architecture.md`: capture loop (frame id, stale repeat), frame freshness rule and `FrameMaxAgeMs`, screenshot headers (`X-Capture-Id`, `X-Capture-Stale`), `WaitForImage` step details keys, condition log lines, the `stale_frame` failure, the liveness note; refresh "Last reviewed"
- [X] T041 [P] Add a `CHANGELOG.md` entry for feature 197 (issue #407) in STE
- [X] T042 [P] Set the `Status` line of `specs/106-wedged-device-liveness/spec.md` to "Implemented (iterated by 121, 197)" with a short note; add row 197 and change row 106 in `specs/STATUS.md`; set the `Status` of `specs/197-fix-stale-capture-frame/spec.md` to "Implemented"
- [X] T043 [P] Add `FrameMaxAgeMs` to the configuration documentation where the other `Service:DeviceLiveness` keys are listed (search `CaptureStallLimitMs` in `docs/` and `src/GameBot.Service/appsettings*.json`)
- [X] T044 Run the full test projects `tests/unit/GameBot.UnitTests.csproj`, `tests/integration/GameBot.IntegrationTests.csproj`, and `tests/contract/GameBot.ContractTests.csproj`; fix each failure; confirm that no current expected result changed except where this spec changes the rule (SC-006)
- [X] T045 Run `dotnet build` with analyzers for `GameBot.sln` and fix each new warning (CA rules, method names in CamelCase)
- [X] T046 Run the steps of `specs/197-fix-stale-capture-frame/quickstart.md` section 1

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. Blocks all user stories.
- **User Stories (Phase 3 to 6)**: Depend on Phase 2. US2 uses the pass data of US1 (T017, T018). US3 and US4 depend only on Phase 2.
- **Polish (Phase 7)**: Depends on all user stories.

### User Story Dependencies

- **US1 (P1)**: After Phase 2. No dependency on another story.
- **US2 (P2)**: After Phase 2 and T017, T018 of US1 (the pass data).
- **US3 (P2)**: After Phase 2 (T005, T007). No dependency on another story.
- **US4 (P3)**: After Phase 2 (T010). No dependency on another story.

### Within Each User Story

- Tests first. They MUST fail before the implementation.
- Domain types before the runner, the runner before the service and the log map code.

### Parallel Opportunities

- T002, T003, T004, T006 in Phase 2.
- T013 to T016 (tests of US1); T020 next to T017.
- T022 to T025 (tests of US2).
- US3 (T031 to T035) and US4 (T036 to T039) in parallel with US1 and US2.
- T040 to T043 in Phase 7.

---

## Parallel Example: User Story 1

```text
Task: "T013 Unit tests for stale passes in tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs"
Task: "T014 Unit tests for imageVisible on a stale frame in tests/unit/Sequences/ImageVisibleStaleFrameTests.cs"
Task: "T015 Unit tests for ocrText on a stale frame in tests/unit/Sequences/OcrTextConditionEvaluatorTests.cs"
Task: "T016 Integration test in tests/integration/Sequences/WaitForImageStaleFrameRunTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Stop and validate: the `WaitForImage` and condition tests pass.

### Incremental Delivery

1. Phase 1 and 2: the foundation.
2. US1: no decision on a stale frame (MVP).
3. US2: the log shows the frame data.
4. US3: the screenshot endpoint shows the frame id and the stale flag.
5. US4: the stall rule and the documentation.
6. Phase 7: documentation, full tests, analyzers.

---

## Notes

- [P] tasks change different files and have no dependency on an open task.
- Do not change the `WaitForImage` outcome rule (FR-013).
- The version bump of `installer/versioning/version.override.json` is pipeline step 10, not a task here.

---

description: "Task list for feature 166: fix WaitForImage timeout on an image that is on screen (B-038)"
---

# Tasks: Fix WaitForImage timeout on an image that is on screen (B-038)

**Input**: Design documents from `/specs/166-fix-waitforimage-timeout/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: The spec requires tests (FR-009, FR-010, FR-013, SC-005). This is a bug fix. Thus the tests come first in each story, and they MUST fail before the fix (constitution Principle II). Record each failure before the fix in the notes of the task. A test that is a regression guard and cannot fail before the fix is marked as such, and the task notes MUST give the reason.

**Organization**: Tasks are in groups by user story. Each story can be done and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1, US2)
- Each task gives the exact file path

## Path Conventions

- Service source: `src/GameBot.Service/`, domain source: `src/GameBot.Domain/`, emulator source: `src/GameBot.Emulator/`
- Integration tests: `tests/integration/` (`GameBot.IntegrationTests.csproj`)
- Unit tests: `tests/unit/` (`GameBot.UnitTests.csproj`)
- Line numbers refer to `master` at commit `8a78a0f` (research.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make sure that the start state is green before work starts.

- [ ] T001 Run `dotnet build -c Debug` and `dotnet test` on `C:\src\GameBot\GameBot.sln`. Record that the build and all tests pass before the change (constitution build and test gate). If a test fails, stop. Fix the failure or get a documented maintainer waiver.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The test host that the integration tests of US1 and US2 use. No story work can start before this phase is complete.

- [ ] T002 Create the file `tests/integration/WaitForImageCaptureLoopTests.cs` with the test class `WaitForImageCaptureLoopTests` (xUnit, collection `ConfigIsolation`, FluentAssertions) and an internal test host class `WaitForImageCaptureTestHost` in the same file (plan Test plan, research R-006). Make the host class internal, so that `tests/integration/Sessions/SessionCaptureLifecycleTests.cs` can use it. The host builds a `WebApplicationFactory<Program>` with `GAMEBOT_USE_ADB=false`, `GAMEBOT_AUTH_TOKEN`, and a clean data directory. In `ConfigureTestServices`, it registers: (a) a fake `ISessionManager` whose `CreateSession` gives a session with `DeviceSerial` = `"emulator-test"` and status `Running`, and whose `StopSession` removes the session (use the pattern of `ScreenshotFakeSessionManager` in `tests/integration/EmulatorScreenshotCaptureTests.cs:189`); (b) a real `BackgroundScreenCaptureService` with a fake `IAdbScreenCaptureProvider` and a short capture interval (for example 50 ms). Use the pattern of `FixedFrameProvider` (same file, line 232) and the registration at lines 44-50. The fake provider holds the current frame in a field that the test can change, and it can give no frame (return null or throw); (c) `IScreenSourceFactory` = `BackgroundCaptureScreenSourceFactory` and `IScreenSource` = `BackgroundCaptureScreenSource`, so that the test uses the ADB-mode frame path. Do not call `SessionCaptureCleanup.Attach` in this host (research R-002a). Expose the `BackgroundScreenCaptureService` instance to the tests. Add helper methods: make a 540x960 seeded-noise frame ("image frame" with one seed, "no-image frame" with a different seed); crop a 58x22 reference image at a fixed point of the image frame; upload an image with `POST /api/images`; make a session with `POST /api/sessions`; wait for the first cached frame of a session with a limit (for example 2 s); measure the elapsed time of an HTTP call with `Stopwatch`.

**Checkpoint**: The test host builds. User story work can start.

---

## Phase 3: User Story 1 - A WaitForImage step finds an image that is on screen (Priority: P1) MVP

**Goal**: A `WaitForImage` step on the step-execute, command force-execute, and sequence-execute paths finds a reference image that is on the session screen. It completes with `executed` / `image_detected` before `timeoutMs` elapses. An inline sequence step has `waitForImageDetails` not null with `exitCondition` `image_detected` (FR-001 to FR-005, FR-011). `POST /api/sessions` starts the capture loop. `DELETE /api/sessions/{id}` and each other removal of a session (for example the idle sweep) stop the loop. Stub mode starts no loop (FR-013).

**Independent Test**: Set the image frame in the test host. Make a session with `POST /api/sessions`. Run a `WaitForImage` step on each of the three paths. Each step completes with the found status before `timeoutMs` elapses. Before T017, these tests fail with `completed_timeout`. The lifecycle tests show that `DELETE` and the idle sweep stop the loop, and that stub mode starts no loop.

### Tests for User Story 1 (write first, they MUST fail before T017 to T022)

- [ ] T003 [US1] Add test `StepExecuteFindsImageOnScreen` in `tests/integration/WaitForImageCaptureLoopTests.cs`: make a session with `POST /api/sessions`, set the image frame, and send `POST /api/steps/execute` with a `WaitForImage` step (`timeoutMs` 3000, `confidence` 0.85). Assert `stepOutcomes[0].status` = `executed`, `reason` = `image_detected`, `detectionConfidence` and `resolvedPoint` set, and elapsed time less than `timeoutMs` (FR-001, FR-002, FR-011, SC-001, SC-002).
- [ ] T004 [US1] Add test `ForceExecuteFindsImageOnScreen` in `tests/integration/WaitForImageCaptureLoopTests.cs`: create a command with one `WaitForImage` step and send `POST /api/commands/{id}/force-execute` with the session id. Assert that the `WaitForImage` step outcome is `executed` / `image_detected` and that the elapsed time is less than `timeoutMs` (FR-004, clarification C2).
- [ ] T005 [US1] Add test `SequenceInlineWaitFindsImageOnScreen` in `tests/integration/WaitForImageCaptureLoopTests.cs`: create a sequence with an inline `WaitForImage` step and send `POST /api/sequences/{id}/execute`. Assert step `actionOutcome` = `image_detected`, `waitForImageDetails` not null, `waitForImageDetails.exitCondition` = `image_detected`, and step time (`startedAt` to `endedAt`) less than `timeoutMs` (FR-004, FR-005, research R-004).
- [ ] T006 [US1] Add test `SequenceCommandWaitFindsImageOnScreen` in `tests/integration/WaitForImageCaptureLoopTests.cs`: create a sequence with a command step that runs a command with a `WaitForImage` step. Assert that the command step outcome is `executed`, that the nested `WaitForImage` outcome is `executed` / `image_detected`, that `waitForImageDetails` stays null for the command step, and that the step time is less than `timeoutMs` (FR-005, research R-004, contract `waitforimage-step-outcome.md`).
- [ ] T007 [US1] Add test `StepResultAgreesWithDetectEndpoint` in `tests/integration/WaitForImageCaptureLoopTests.cs`: for the image frame and then for the no-image frame, send `POST /api/images/detect` with `sessionId` and `threshold` = the step `confidence`, then `POST /api/steps/execute` (use `timeoutMs` 1500 for the no-image frame). Assert that both calls find the image, or that both calls do not find it (FR-003, SC-004).
- [ ] T008 [US1] Add test `StepFindsImageThatComesOnScreenLater` in `tests/integration/WaitForImageCaptureLoopTests.cs` (edge case 1): set the no-image frame, start `POST /api/steps/execute` (`timeoutMs` 3000), and change to the image frame after about 300 ms. Assert `executed` / `image_detected` and elapsed time less than `timeoutMs` (clarification C3).
- [ ] T009 [US1] Add test `StepFindsImageAtLowConfidence` in `tests/integration/WaitForImageCaptureLoopTests.cs` (edge case 2, found part): set the image frame and send `POST /api/steps/execute` with `confidence` 0.5. Assert `executed` / `image_detected` and elapsed time less than `timeoutMs` (clarification C3).
- [ ] T010 [US1] Add test `StepFindsStoredAlternate` in `tests/integration/WaitForImageCaptureLoopTests.cs` (edge case 3): upload a reference image that is not on the frame, upload a second image that is a crop of the image frame, and set it as an alternate with `PUT /api/images/{id}/alternates`. Send `POST /api/steps/execute` for the first image. Assert `executed` / `image_detected` (clarification C3).
- [ ] T011 [P] [US1] Create `tests/integration/Sessions/SessionCaptureLifecycleTests.cs` with test `DeleteSessionStopsCaptureLoop` (FR-013, research R-006 lifecycle item 1, contract `session-capture-lifecycle.md`). Use `WaitForImageCaptureTestHost` from T002. Make a session with `POST /api/sessions`, wait for the first cached frame, and send `DELETE /api/sessions/{id}`. Assert that after the call, `GetCachedFrame(id)` and `GetCaptureMetrics(id)` of the capture service give `null`. Depends on T002.
- [ ] T012 [US1] In `tests/integration/Sessions/SessionCaptureLifecycleTests.cs`, add test `StubModeStartsNoCaptureLoop` (FR-013, research R-006 lifecycle item 2). Use a plain `WebApplicationFactory<Program>` host with `GAMEBOT_USE_ADB=false` and no replacement of the capture service. Assert that `POST /api/sessions` gives `201`, that `Services.GetService<BackgroundScreenCaptureService>()` gives `null`, and that `GET /api/sessions/running` gives a null `captureRateFps` for the session (or does not list a capture rate for it). This test is a regression guard. It passes before the fix. Record this in the task notes.
- [ ] T013 [P] [US1] Create `tests/unit/Sessions/SessionCaptureCleanupTests.cs` with test `IdleSweepStopsCaptureLoop` (FR-013, research R-002a and R-006 lifecycle item 3): a real `SessionManager` with `IdleTimeoutSeconds` = 1, a real `BackgroundScreenCaptureService` with a fake `IAdbScreenCaptureProvider`, and `SessionCaptureCleanup.Attach(manager, captureService)`. Make a session, call `StartCapture` for it, wait for the first frame, wait more than 1 s with no session manager call, and call `ListSessions()`. Assert that `GetCaptureMetrics(id)` and `GetCachedFrame(id)` give `null`.
- [ ] T014 [US1] In `tests/unit/Sessions/SessionCaptureCleanupTests.cs`, add tests (FR-013, research R-006 lifecycle item 4): (a) `StopSessionRaisesSessionRemovedOnce`: subscribe to `ISessionRemovalNotifier.SessionRemoved` of a real `SessionManager`, call `StopSession(id)` for a known session, and assert exactly one event with that `SessionId`; (b) `StopSessionOfUnknownIdRaisesNoEvent`; (c) `HandlerExceptionDoesNotFailRemoval`: a handler that throws does not make `StopSession` throw, and the session is removed; (d) `AttachWithManagerThatIsNotNotifierDoesNothing`: `Attach` with a fake `ISessionManager` that does not implement `ISessionRemovalNotifier` does not throw.
- [ ] T015 [US1] Run the US1 tests: `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~WaitForImageCaptureLoopTests|FullyQualifiedName~SessionCaptureLifecycleTests"` and `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~SessionCaptureCleanup"`. Record in the task notes that T003 to T011 fail before the fix (the found-case tests with `completed_timeout`, T011 because no first frame comes), that T013 and T014 fail before the fix (they do not compile, because `ISessionRemovalNotifier` and `SessionCaptureCleanup` do not exist), and that T012 passes (regression guard) (SC-005).

### Implementation for User Story 1

- [ ] T016 [P] [US1] Create `src/GameBot.Emulator/Session/ISessionRemovalNotifier.cs` (plan D-2, data-model.md): the public interface `ISessionRemovalNotifier` with `event EventHandler<SessionRemovedEventArgs>? SessionRemoved`, and the public sealed class `SessionRemovedEventArgs : EventArgs` with a `SessionId` (string) property set in the constructor. Add XML doc comments in STE.
- [ ] T017 [US1] In `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`, change the `POST /api/sessions` handler (lines 19-51) to start the capture loop (plan D-1, research R-002, contract `session-capture-lifecycle.md`, FR-013). Add an `HttpContext ctx` parameter. After `CreateSession` and `cache.SetSessionId` (line 34), get `BackgroundScreenCaptureService` with `ctx.RequestServices.GetService<BackgroundScreenCaptureService>()`. When the service is not null and `sess.DeviceSerial` is not blank, call `StartCapture(sess.Id, sess.DeviceSerial)`. Put the call in a try/catch. On an exception, write a warning log in STE with the session id, and still return `201`. Keep the handler under about 50 lines. Move the start into a small private method if necessary.
- [ ] T018 [US1] In `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`, change the `DELETE /api/sessions/{id}` handler (lines 134-138). Get `BackgroundScreenCaptureService` in the same way. When it is not null, call `StopCapture(id)` before `mgr.StopSession(id)` (plan D-1, FR-013). Stub mode does not change. Depends on T017 (same file).
- [ ] T019 [US1] In `src/GameBot.Emulator/Session/SessionManager.cs`, implement `ISessionRemovalNotifier` (plan D-2, research R-002a). Raise `SessionRemoved` one time after `TryRemove` succeeds in `StopSession` (about line 176) and in `CleanupIdleSessions` (about line 513). Raise the event outside of any lock. Catch each handler exception, write a warning log, and continue, so that a removal never fails. Do not raise the event when `TryRemove` fails. Do not change `ISessionManager`. Depends on T016.
- [ ] T020 [US1] Create `src/GameBot.Emulator/Session/SessionCaptureCleanup.cs` (plan D-2, data-model.md): a public static class with `Attach(ISessionManager sessionManager, BackgroundScreenCaptureService captureService)`. When `sessionManager` is an `ISessionRemovalNotifier`, subscribe a handler that calls `captureService.StopCapture(e.SessionId)`. The handler catches an exception and writes a warning log (or lets the `SessionManager` catch it, as T019 does). When `sessionManager` is not an `ISessionRemovalNotifier`, do nothing. Add XML doc comments in STE. Depends on T016.
- [ ] T021 [US1] In `src/GameBot.Service/GameBotServiceSetup.cs` (the DI factory of `BackgroundScreenCaptureService`, lines 481-492), call `SessionCaptureCleanup.Attach(sp.GetRequiredService<ISessionManager>(), service)` before the factory returns (plan D-2). Depends on T020.
- [ ] T022 [US1] Run the US1 tests from T015 again and make sure that all pass after T016 to T021 (SC-001, SC-002, SC-004, FR-013). Also run `tests/integration/EmulatorScreenshotCaptureTests.cs`, the current session endpoint tests, and the current `SessionManager` unit tests, and make sure that they still pass.

**Checkpoint**: User Story 1 works and is tested independently. This is the MVP. It fixes the reported defect (issue #350), and no capture loop stays on for a session that does not exist.

---

## Phase 4: User Story 2 - A WaitForImage step still times out correctly when the image is not on screen (Priority: P2)

**Goal**: When the image is not on screen, the step completes with `completed_timeout` / `timeout_elapsed` (inline sequence step: `actionOutcome` and `exitCondition` `timeout_elapsed`) in not more than `timeoutMs` plus one detection pass. No pass starts at or after the deadline. When the remaining time is not more than the poll interval, the last wait ends 50 ms before the deadline, and the step does one last pass (FR-006, FR-010). On all three paths, the service writes one debug log line with the frame state and the best score of the last pass (FR-012). The missing-image and no-target behaviors do not change (FR-007, FR-008).

**Independent Test**: Set the no-image frame in the test host. Run a `WaitForImage` step on each path. Each step completes with the timeout status in not more than `timeoutMs` + 500 ms and in less than 2 × `timeoutMs`. Unit tests with a slow pass show 2 passes and no pass start at or after the deadline (30 ms tolerance), and each executor writes one debug log line on timeout.

### Tests for User Story 2 (write first; the deadline tests MUST fail before T037 and T038)

- [ ] T023 [US2] Add theory `StepTimesOutWhenImageNotOnScreen` in `tests/integration/WaitForImageCaptureLoopTests.cs` with one case for each path (step execute, command force-execute, sequence inline step). Set the no-image frame and `timeoutMs` 1500. Assert `completed_timeout` / `timeout_elapsed` (inline sequence step: `actionOutcome` and `waitForImageDetails.exitCondition` = `timeout_elapsed`). Assert elapsed time not more than `timeoutMs` + 500 ms and less than 2 × `timeoutMs` (FR-006, SC-003, clarification A1).
- [ ] T024 [US2] Add test `StepTimesOutInTimeAtLowConfidence` in `tests/integration/WaitForImageCaptureLoopTests.cs` (edge case 2, timeout part): set the no-image frame and send `POST /api/steps/execute` with `confidence` 0.5 and `timeoutMs` 1500. Assert `completed_timeout` / `timeout_elapsed`, elapsed time not more than `timeoutMs` + 500 ms, and less than 2 × `timeoutMs` (clarification C3, A1).
- [ ] T025 [US2] Add test `StepTimesOutWhenNoFrameYet` in `tests/integration/WaitForImageCaptureLoopTests.cs` (edge case 4): set the fake provider to give no frame, and send `POST /api/steps/execute` with `timeoutMs` 1500. Assert `completed_timeout` / `timeout_elapsed`, and that the outcome is never `image_detected` (clarification C3).
- [ ] T026 [US2] Add test `MissingImageStillUnavailable` in `tests/integration/WaitForImageCaptureLoopTests.cs`: send `POST /api/steps/execute` with a reference image id that is not uploaded. Assert `completed_image_unavailable` / `image_unavailable` (FR-007). This test is a regression guard and passes before the fix.
- [ ] T027 [P] [US2] Create `tests/unit/Commands/WaitForImageScheduleTests.cs` (FR-010, data-model.md "WaitForImage poll schedule", research R-003). Test `WaitForImageSchedule.NextWait(remaining, pollInterval)` with `pollInterval` = 1000 ms and fixed values: remaining 1500 ms gives wait 1000 ms, not the last wait; remaining 1000 ms (equal to the interval) gives wait 950 ms, the last wait; remaining 120 ms gives wait 70 ms, the last wait; remaining 30 ms gives wait 0 ms, the last wait; remaining 0 ms and a negative value give no wait and stop. The tests fail before the fix (they do not compile). Record this in the task notes.
- [ ] T028 [P] [US2] In `tests/unit/Commands/CommandExecutorWaitForImageTests.cs`, add the deadline test (FR-010, research R-003, clarification U1): a screen source stub whose `GetLatestScreenshot` records the start offset of each call from the start of the step (use one `Stopwatch`), sleeps 150 ms, and returns null. Set `CaptureIntervalMs` = 1000 and `timeoutMs` = 300. Assert that the number of passes is 2, that each pass start offset is less than 280 ms (`timeoutMs` − 50 ms + 30 ms tolerance), and that the result is `completed_timeout` / `timeout_elapsed`. Do not assert on exact times. Also add a check that a step with no detection target stays `executed` / `delay_elapsed` (FR-008), if no current test covers it. Run the test and record in the task notes that it fails before T037 (expected: pass 2 starts at about 300 ms). If it cannot fail before the fix, record the reason.
- [ ] T029 [P] [US2] In `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs`, add the deadline test (FR-010, clarification U1): a condition evaluator stub that records the start offset of each call, sleeps 150 ms, and gives false. Set the poll interval to 1000 ms and `timeoutMs` = 300. Assert that the number of evaluations is 2, that each start offset is less than 280 ms, and that the result is `timeout_elapsed`. Run the test and record in the task notes that it fails before T038. If it cannot fail before the fix, record the reason.
- [ ] T030 [US2] In `tests/unit/Commands/CommandExecutorWaitForImageTests.cs`, add the log line test (FR-012, plan D-4): use a capturing `ILogger<CommandExecutor>` and a screen source that gives null. Assert that exactly one `Debug` line is written on timeout, and that it contains the session id, the reference image id, the `confidence`, the pass count, frame available `false`, and best score `none`. Depends on T028 (same file).
- [ ] T031 [US2] In `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs`, add two log line tests (FR-012, research R-008): (a) the stub evaluator writes a pass to `ImageDetectionPassScope.Current` (frame available `true`, best score 0.423); assert exactly one `Debug` line on timeout with the sequence id, the step key, the reference image id, the `confidence`, the pass count, frame state `true`, and best score `0.423`; (b) the stub evaluator writes nothing; assert frame state `unknown` and best score `none`. Depends on T029 (same file).
- [ ] T032 [P] [US2] Create `tests/unit/Vision/ImageDetectionPassScopeTests.cs` (FR-012, research R-008, data-model.md): (a) with no scope, `ImageDetectionPassScope.Current` is null; (b) after `Push()`, `Current` is not null and `LastPass` is null; (c) a value written to `LastPass` is visible after an `await` on the same flow; (d) after `Dispose`, `Current` is the earlier value (null or the outer scope); (e) `ImageMatchEvaluator` writes `FrameAvailable` false when the screen source gives null, and `FrameAvailable` true with a best score when it gives a frame; (f) with no scope, `ImageMatchEvaluator` writes nothing and its result does not change.

### Implementation for User Story 2

- [ ] T033 [P] [US2] Create `src/GameBot.Domain/Commands/Execution/WaitForImageSchedule.cs` (plan D-3, data-model.md, research R-003): a public static class with a pure method `NextWait(TimeSpan remaining, TimeSpan pollInterval)` that returns a small result (for example a `readonly record struct WaitForImageWait(bool Stop, TimeSpan Wait, bool IsLastWait)`). Rules: `remaining` more than `pollInterval` gives `pollInterval`, not the last wait; `remaining` not more than `pollInterval` and more than 50 ms gives `remaining` − 50 ms, the last wait; `remaining` not more than 50 ms and more than 0 gives 0 ms, the last wait; `remaining` not more than 0 gives stop. Put the 50 ms value in a named constant. Add XML doc comments in STE. Make sure that T027 passes.
- [ ] T034 [P] [US2] In `src/GameBot.Service/Services/ImageDetectionHelper.cs`, add the internal record `DetectionPassResult(bool Found, bool FrameAvailable, double? BestScore, PrimitiveTapResolvedPoint? ResolvedPoint, double? DetectionConfidence)` and the method `DetectPass(...)` that returns it (plan D-4, data-model.md, research R-005). `FrameAvailable` is false when the screen source gives null. `BestScore` is the best match score at or above the matcher threshold, or null. Change `TryDetect` to call `DetectPass` and keep its current signature, so that the game-readiness probe does not change.
- [ ] T035 [P] [US2] Create `src/GameBot.Domain/Vision/ImageDetectionPassScope.cs` (plan D-4, research R-008, data-model.md): an ambient scope with an `AsyncLocal` value, the same pattern as `OcrTextEvaluationScope` (`src/GameBot.Domain/Services/OcrTextEvaluationScope.cs:69-81`). It has `Current`, a `Push()` method that returns an `IDisposable` and restores the earlier scope on dispose, and the property `LastPass` of type `ImageDetectionPass?`. `ImageDetectionPass` is a record with `FrameAvailable` (bool) and `BestScore` (double?). Write the code comments in STE.
- [ ] T036 [US2] In `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`, change `ComputeSimilarity` (lines 59-81) to write to `ImageDetectionPassScope.Current` when a scope is present: `FrameAvailable` false when `GetLatestScreenshot` gives null (line 62), else `FrameAvailable` true and the best similarity of the image and its alternates. When no scope is current, write nothing, so that triggers and `imageVisible` conditions do not change (research R-008). Depends on T035. Make sure that T032 passes.
- [ ] T037 [US2] In `src/GameBot.Service/Services/CommandExecutor.cs`, change `ExecuteWaitForImageStepAsync` (method at line 606, first pass at line 682, poll loop at lines 697-720, timeout return at lines 722-733) (plan D-3 and D-4, FR-010, FR-012). Keep the first pass. Replace the poll loop: read the clock; call `WaitForImageSchedule.NextWait(deadline - now, pollInterval)`; stop on `Stop`; else `await Task.Delay(wait, ct)`; read the clock again and start a pass only when the current time is before the deadline; stop after the pass of the last wait. Use `DetectPass` for each pass. Keep the last `DetectionPassResult` and a pass count. On timeout, write one `[LoggerMessage]` line at `Debug` level, for example `WaitForImage step {Order} on session {SessionId} timed out for image {ReferenceImageId} at confidence {Confidence}: {PassCount} passes, last pass frame available {FrameAvailable}, best score {BestScore}.` The best score has three decimals, or the text `none`. Put the timeout log in a separate method. Do not change the API outcome fields. Keep each method under about 50 lines. Move the poll loop into a private helper method if necessary (plan Constitution Check I). Depends on T033 and T034. Make sure that T028 and T030 pass.
- [ ] T038 [US2] In `src/GameBot.Domain/Services/SequenceRunner.cs`, change `WaitForImageAsync` (poll loop at lines 1065-1086) (plan D-3 and D-4, FR-010, FR-012, research R-008). Use `WaitForImageSchedule.NextWait` with the same rule as T037: no evaluation starts at or after the deadline, and the last wait ends 50 ms before the deadline. Push an `ImageDetectionPassScope` at the start of the wait and dispose it at the end. Count the passes. On timeout, write one `[LoggerMessage]` line at `Debug` level with the sequence id, the step key, the reference image id, the `confidence`, the pass count, the frame state (`true`, `false`, or `unknown` when `LastPass` is null), and the best score (three decimals, or `none`). Put the timeout log in a separate method. Do not change the API outcome fields. Depends on T033 and T035. Make sure that T029 and T031 pass.
- [ ] T039 [US2] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~WaitForImage|FullyQualifiedName~ImageDetectionPassScope"` and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~WaitForImageCaptureLoopTests"`. Make sure that all US1 and US2 tests pass. Also make sure that the current tests `tests/integration/Commands/WaitForImageExecutionIntegrationTests.cs`, `tests/integration/Sequences/WaitForImageSequenceExecutionIntegrationTests.cs`, and the `ImageMatchEvaluator`, trigger, and `imageVisible` tests still pass.

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Living documentation, STE review, and the full gate.

- [ ] T040 [P] Update `docs/architecture.md` near the capture loop text (about lines 968-1008) (plan D-5, research R-007): state that all three session-creation paths (`POST /api/sessions`, `POST /api/sessions/start`, queue runs) start the capture loop; that each removal of a session stops the loop (`DELETE /api/sessions/{id}`, the idle sweep through `SessionRemoved` and `SessionCaptureCleanup`, and other stops), and that the idle sweep is lazy; that a `WaitForImage` step does not start a detection pass at or after its deadline, and that the last wait ends 50 ms before the deadline; and that a timeout writes one debug log line with the frame state and the best score. Update the `_Last reviewed:` line (line 13) with the date and feature 166. Use STE.
- [ ] T041 [P] Update `CHANGELOG.md` under `## [Unreleased]` (line 5) in the `Fixed` section: add an entry for `166-fix-waitforimage-timeout` (#350) with the cause (a session from `POST /api/sessions` had no capture loop, so the step had no frame), the fix (the create starts the loop; the delete and each other removal of a session stop it; no pass starts at or after the deadline; one debug log line on timeout), and what did not change (`imageVisible`, `POST /api/images/detect`, the image lookup, the API shape). Use STE.
- [ ] T042 [P] Update `specs/STATUS.md`: add the row `| 166 | Fix WaitForImage Timeout on an Image That Is on Screen | Implemented |` after the row of feature 165 (line 195). Set the `**Status**:` line of `specs/166-fix-waitforimage-timeout/spec.md` to `Implemented`.
- [ ] T043 Review all new and changed code comments, log messages, and doc text in `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`, `src/GameBot.Service/GameBotServiceSetup.cs`, `src/GameBot.Service/Services/ImageDetectionHelper.cs`, `src/GameBot.Service/Services/CommandExecutor.cs`, `src/GameBot.Emulator/Session/ISessionRemovalNotifier.cs`, `src/GameBot.Emulator/Session/SessionCaptureCleanup.cs`, `src/GameBot.Emulator/Session/SessionManager.cs`, `src/GameBot.Domain/Commands/Execution/WaitForImageSchedule.cs`, `src/GameBot.Domain/Services/SequenceRunner.cs`, `src/GameBot.Domain/Vision/ImageDetectionPassScope.cs`, `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`, and the seven new or changed test files. Check STE (constitution Principle VI), method length under about 50 lines, CamelCase names, XML doc comments on public members, and no new analyzer warnings (Principle I, CA1003).
- [ ] T044 Run the full `dotnet build` and `dotnet test` on `C:\src\GameBot\GameBot.sln` and make sure that all tests pass with no new warnings (constitution gate). Check coverage of 80% line and 70% branch for the touched code (Principle II). Then do the automated steps of `specs/166-fix-waitforimage-timeout/quickstart.md`: for the SC-005 check, revert only the change in `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`, make sure that the found-case tests fail with `completed_timeout`, and then restore the change. For the FR-010 check, revert only the deadline rule in `CommandExecutor.cs` and `SequenceRunner.cs`, make sure that T028 and T029 fail, and then restore the change. Record the results in the task notes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependency. Start at once.
- **Foundational (Phase 2)**: Depends on Phase 1. Blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2. No dependency on US2.
- **User Story 2 (Phase 4)**: Depends on Phase 2. The integration tests T023 to T026 are in the same file as the US1 tests, thus do them after T010. The integration timeout tests need a frame, thus they pass only after T017 (US1). The unit tests and the source changes (T027 to T038) do not depend on US1.
- **Polish (Phase 5)**: Depends on US1 and US2.

### Within Each User Story

- Write the tests first. The US1 found-case tests and T011 MUST fail before T017. T013 and T014 MUST fail before T016, T019, and T020. The deadline tests T028 and T029 MUST fail before T037 and T038 (FR-010). Record each failure in the task notes.
- US1: T016 before T019 and T020. T020 before T021. T017 before T018 (same file).
- US2: T033 (schedule) and T034 (`DetectPass`) before T037 (`CommandExecutor`). T033 and T035 (scope) before T038 (`SequenceRunner`). T035 before T036 (`ImageMatchEvaluator`).
- Run the story tests at the end of each story (T022, T039).

### Parallel Opportunities

- US1 tests: T011 (`tests/integration/Sessions/SessionCaptureLifecycleTests.cs`) and T013 (`tests/unit/Sessions/SessionCaptureCleanupTests.cs`) can run in parallel with T003 to T010 (`tests/integration/WaitForImageCaptureLoopTests.cs`).
- US1 implementation: T016 (new file) can run in parallel with T017 and T018 (`SessionsEndpoints.cs`).
- US2 tests: T027 (`WaitForImageScheduleTests.cs`), T028 then T030 (`CommandExecutorWaitForImageTests.cs`), T029 then T031 (`SequenceRunnerWaitForImageTests.cs`), and T032 (`ImageDetectionPassScopeTests.cs`) are in four files and can run in parallel.
- US2 implementation: T033, T034, and T035 are in different files and can run in parallel. After them, the `CommandExecutor` path (T037) and the `SequenceRunner` path (T036, T038) can run in parallel.
- T040, T041, and T042 are in different files and can run in parallel.
- The tasks in `tests/integration/WaitForImageCaptureLoopTests.cs` (T002 to T010, T023 to T026) are in one file, thus they run in sequence.

---

## Parallel Example: User Story 1

```text
Task: "T011 [P] [US1] DeleteSessionStopsCaptureLoop in tests/integration/Sessions/SessionCaptureLifecycleTests.cs"
Task: "T013 [P] [US1] IdleSweepStopsCaptureLoop in tests/unit/Sessions/SessionCaptureCleanupTests.cs"
Task: "T016 [P] [US1] Create src/GameBot.Emulator/Session/ISessionRemovalNotifier.cs"
```

## Parallel Example: User Story 2

```text
Task: "T027 [P] [US2] WaitForImageSchedule tests in tests/unit/Commands/WaitForImageScheduleTests.cs"
Task: "T028 [P] [US2] Deadline unit test in tests/unit/Commands/CommandExecutorWaitForImageTests.cs"
Task: "T029 [P] [US2] Deadline unit test in tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs"
Task: "T032 [P] [US2] Scope tests in tests/unit/Vision/ImageDetectionPassScopeTests.cs"
Task: "T033 [P] [US2] Create src/GameBot.Domain/Commands/Execution/WaitForImageSchedule.cs"
Task: "T035 [P] [US2] Create src/GameBot.Domain/Vision/ImageDetectionPassScope.cs"
```

## Parallel Example: Polish

```text
Task: "T040 [P] Update docs/architecture.md"
Task: "T041 [P] Update CHANGELOG.md"
Task: "T042 [P] Update specs/STATUS.md and the spec Status line"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Do Phase 1 (green start state).
2. Do Phase 2 (test host).
3. Do Phase 3: write the failing found-case and lifecycle tests, record the failures, then fix `SessionsEndpoints.cs` and add the `SessionRemoved` cleanup.
4. Stop and validate: the found-case tests pass on all three paths, and the lifecycle tests pass. This fixes the reported defect with no capture loop leak.

### Incremental Delivery

1. Setup and Foundational: the test host is ready.
2. US1: the step finds the image on screen, and the capture loop follows the session lifecycle (MVP, fixes issue #350).
3. US2: the timeout tests, the deadline rule with the 50 ms last wait, and the debug log line on all three paths.
4. Polish: docs, STE review, full gate, and the revert checks for SC-005 and FR-010.

---

## Notes

- [P] tasks = different files, no dependency on an incomplete task.
- [Story] label maps the task to a user story for traceability.
- No API shape change. No new package. No web UI change. No change to `ISessionManager` (its test fakes stay as they are).
- Record in the task notes each test failure before the fix (SC-005, FR-010, FR-013). If a deadline test cannot fail before the fix, record the reason. T012 and T026 are regression guards and pass before the fix.
- Timeout test limits (clarification A1): elapsed time not more than `timeoutMs` + 500 ms, and less than 2 × `timeoutMs`. Use `timeoutMs` 1500 or more in the integration timeout tests, so that the two limits do not conflict.
- Deadline test rule (clarification U1): assert on the number of passes (2) and on pass start offsets less than `timeoutMs` − 50 ms + 30 ms. Do not assert on exact times.
- Version bump: before the push for the PR, bump `installer/versioning/version.override.json` (CLAUDE.md, constitution "Version bump"). This is not a task in this list.
- PR performance note: the PR description MUST contain the performance note on the capture loop that `POST /api/sessions` now starts (spec Assumptions, plan D-5, research R-007, constitution Principle IV). Text: "`POST /api/sessions` now starts one capture loop for each session with a device, at `CaptureIntervalMs`. This is the same cost as a session from `POST /api/sessions/start` or a queue session. The detection pass does not change. The deadline check adds one clock read for each poll." Write this note together with the version bump.

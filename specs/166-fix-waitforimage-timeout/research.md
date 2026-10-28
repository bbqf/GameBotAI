# Research: Fix WaitForImage timeout on an image that is on screen (B-038)

**Feature**: `166-fix-waitforimage-timeout` | **Date**: 2026-10-27 | **Issue**: #350

This file records the root-cause investigation and the design decisions. Line numbers refer to `master` at commit `8a78a0f`.

## R-001: Root cause — a session from `POST /api/sessions` has no capture loop

**Decision**: The defect is not in the matcher and not in the template decode. The defect is in the session lifecycle. `POST /api/sessions` makes a session, but it does not start the background capture loop of that session. Thus the session screen source never has a frame, and every detection pass of the step finds nothing.

**Evidence (code)**:

1. `src/GameBot.Service/Endpoints/SessionsEndpoints.cs:19-51`: the `POST /api/sessions` handler calls `mgr.CreateSession(game, req.AdbSerial)` (line 32). It writes the session to the context cache (line 34). It does not call `BackgroundScreenCaptureService.StartCapture`.
2. The two other paths that make a session start the loop: `src/GameBot.Service/Services/SessionService.cs:65-68` (`POST /api/sessions/start`) and `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs:1014-1020` (`BindQueueSession`).
3. `src/GameBot.Service/Endpoints/SessionsEndpoints.cs:134-138`: `DELETE /api/sessions/{id}` calls only `mgr.StopSession(id)`. It does not stop a capture loop.
4. `src/GameBot.Service/Services/CommandExecutor.cs:94-99` (`ResolveScreenSource`): when the executor knows the session, it uses `_screenFactory.ForSession(sessionId)`. In ADB mode, this is a `SessionScopedScreenSource`.
5. `src/GameBot.Emulator/Session/SessionScopedScreenSource.cs:46-48` and `src/GameBot.Emulator/Session/BackgroundScreenCaptureService.cs:96-99`: with no loop for the session, `GetCachedFrame` gives `null`, and `GetLatestScreenshot` gives `null`.
6. `src/GameBot.Service/Services/ImageDetectionHelper.cs:42-45`: `TryDetect` returns `false` for a `null` frame. It writes no log.
7. `src/GameBot.Service/Services/CommandExecutor.cs:682` and `:706`: each pass gets `false`. After the deadline, the step returns `completed_timeout` / `timeout_elapsed` with `ImageLoadStatus: "loaded"` (lines 722-733). This agrees with the issue: the image lookup works, and the step times out.

**Why `POST /api/images/detect` works on the same device**:

- `src/GameBot.Service/Endpoints/EmulatorImageEndpoints.cs:49-60`: `GET /api/emulator/screenshot` reads the cache first. When the cache has no frame, it falls back to a direct ADB capture (line 60) and stores the result as a capture with a `captureId`.
- `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs:104-107`: `POST /api/images/detect` with a `captureId` reads that stored PNG. It does not read the cache. Thus the detect call measures a real frame and gives 0.99999994.
- A detect call with the `sessionId` of the same session gives `503` "No screenshot has been captured for that session yet." (`ImageDetectionsEndpoints.cs:113-121`). A detect call with no target gives `409 ambiguous_session` when other queue sessions run. Thus the reporter could only have used a `captureId`.

**Why the `imageVisible` condition works in queue runs**: a queue run binds its session with `BindQueueSession`, which starts the loop (`QueueExecutionService.cs:1017-1019`). Thus the cache has frames for queue sessions.

**Evidence (live service, 2026-10-27, read-only checks)**:

- The execution log has the reporter calls on session `7c74ff96b0c54b1a9f7a92b6491060bf`. That session is not in `GET /api/sessions/running` now, and it is not a queue session. The pattern agrees with an ad hoc session from `POST /api/sessions`.
- Command `ZZZ.GatherProbe` (12:55:36 UTC) has a `PrimitiveTap` step with `skipped_detection_failed` / `detection_failed_after_3_retries`. A `PrimitiveTap` step reads the same session screen source (`CommandExecutor.cs:770-771`). Thus the tap detection failed for the same reason. This supports a frame-source cause, not a `WaitForImage`-only cause.
- The reference image `pns-city-anchor.png` is 58x22, `Format32bppArgb`, and all alpha values are 255. The capture size of the device is 540x960.

**Ruled out (with evidence)**:

- **Template copy in `ToTemplateMat`** (`ImageDetectionHelper.cs:19-24`, `new Bitmap(bitmap)`): the copy of `pns-city-anchor.png` is pixel-identical to the original (0 different pixels, minimum alpha 255). `TemplateMask.TryCreate` returns `false` for a fully opaque template (`src/GameBot.Domain/Vision/TemplateMask.cs:70-73`). Thus the matcher uses the same `CCoeffNormed` path as the detect endpoint.
- **Matcher, coordinate resolver, and alternates**: a scratch test ran `ImageDetectionHelper.TryDetect` with the real `TemplateMatcher`, the real stored image, and a 540x960 frame that contains the image. It found the image at confidence 0.85 and 0.5, with score 1.0, in 66 ms to 169 ms. `DetectionCoordinateResolver.ResolveCenter` (`src/GameBot.Domain/Commands/Execution/DetectionCoordinateResolver.cs:51-73`) selects the best match and does not reject several matches. The image has no alternates.
- **Request mapping**: `StepsEndpoints.ToDomainStep` (`src/GameBot.Service/Endpoints/StepsEndpoints.cs:139-152`) maps `confidence`, offsets, and region with no change.

**Alternatives considered**:

- Read the frame with a direct ADB capture in `ImageDetectionHelper` when the cache is empty. Rejected: each poll would start an ADB capture. This bypasses the capture gate of feature 121 and the liveness data of feature 106. It also hides the real defect.
- Start the loop on demand in `SessionScopedScreenSource`. Rejected: a screen source is a reader. A start of a device loop from a read is a side effect in the wrong layer, and the factory does not know the device serial.

## R-002: Fix — `POST /api/sessions` starts the capture loop, and `DELETE /api/sessions/{id}` stops it

**Decision**: After `CreateSession`, when `BackgroundScreenCaptureService` is registered and the session has a `DeviceSerial`, the `POST /api/sessions` handler calls `StartCapture(sess.Id, sess.DeviceSerial)`. The `DELETE /api/sessions/{id}` handler calls `StopCapture(id)` before `StopSession(id)`. The handlers get the service with `ctx.RequestServices.GetService<BackgroundScreenCaptureService>()`, as `EmulatorImageEndpoints.cs:23` does. In stub mode (`GAMEBOT_USE_ADB=false`) the service is not registered, so nothing changes there.

**Rationale**: All three session-creation paths then give the same capture state. This agrees with the behavior of `POST /api/sessions/start` and of queue runs. The fix is small, and the API shape does not change.

**Alternatives considered**: Move the start into `SessionManager.CreateSession`. Rejected: `GameBot.Emulator` `SessionManager` does not own the capture service, and a queue rebind already starts the loop itself. A double start restarts the loop (`BackgroundScreenCaptureService.cs:67-71`), which is safe but adds a log line and drops the cached frame.

## R-002a: Stop the loop when the session manager removes a session for any reason (loop 2, I2)

**Decision**: add a small interface `ISessionRemovalNotifier` in `src/GameBot.Emulator/Session/`. It has one event, `SessionRemoved` (`EventHandler<SessionRemovedEventArgs>`), with the session id. `SessionManager` implements it. It raises the event after each removal from its session table: in `StopSession` (`SessionManager.cs:173-182`) and in `CleanupIdleSessions` (`SessionManager.cs:504-519`). A new static helper, `SessionCaptureCleanup.Attach(ISessionManager, BackgroundScreenCaptureService)`, in the same folder, subscribes to the event when the session manager implements the interface. The handler calls `StopCapture(sessionId)`. The DI factory of `BackgroundScreenCaptureService` (`src/GameBot.Service/GameBotServiceSetup.cs:481-492`) calls `Attach` one time.

**Rationale**:

- Before the fix, a session from `POST /api/sessions` had no loop. After the fix it has a loop. If the idle sweep removes the session and nothing stops the loop, the loop is a new resource leak.
- `SessionService.SyncFromSessionManager` (`SessionService.cs:96-105`) stops a loop only for a session in its own running list, and only when a client calls `GET /api/sessions/running` or a `SessionService` method. That is not sufficient.
- `ISessionManager` has more than 30 test fakes. A new member on `ISessionManager` breaks all of them. A separate interface keeps the fakes as they are.
- The DI factory runs before the first `StartCapture`, because each caller must get the service first. Thus no loop can start before the subscription.
- `StopCapture` does nothing for an id with no loop (`BackgroundScreenCaptureService.cs:86-93`). Thus two stop calls for one session are safe. The `DELETE /api/sessions/{id}` handler, `SessionService.StopSession`, and the queue runner can all stop the same loop with no error.

**Why `DELETE /api/sessions/{id}` still calls `StopCapture` itself**: the integration test hosts replace `ISessionManager` with a fake that does not implement `ISessionRemovalNotifier`, and they register their own `BackgroundScreenCaptureService` with no `Attach`. The explicit call keeps the `DELETE` behavior independent of the event, and the integration test can check it.

**Known limit (no change)**: the idle sweep of `SessionManager` is lazy. It runs only at the start of a session manager call (`CreateSession`, `GetSession`, `ListSessions`, and others). The capture loop does not call the session manager. Thus the loop stops at the first session manager call after the idle timeout, not at the exact timeout. The session is in the session table until that call, so no loop runs for a session that does not exist.

**Alternatives considered**:

- Add the event to `ISessionManager`. Rejected: it breaks more than 30 test fakes.
- Inject `BackgroundScreenCaptureService` into `SessionManager`. Rejected: in stub mode the capture service is not registered, and the session manager then has a dependency on a capture concern.
- A timer that compares the loops with the session table. Rejected: a poll adds work and a delay, and the event is exact.

## R-003: The 12.1 s result for a 6 s timeout

**Decision**: The step loop does not wait two times. The plan adds a strict deadline check (FR-010) and a round-trip time test, but it does not look for a second wait in the executor.

**Evidence**:

- The execution log entry `eb22d47e3a4048d18ff815588b0edd47` (12:57:10 UTC) is the `POST /api/steps/execute` call with `timeoutMs` 6000. It has `startedAtUtc` 12:57:04.519 and `durationMs` 6000. The executor sets `startedAtUtc` at `CommandExecutor.cs:257`, just before the step runs. Thus the step itself took 6.0 s.
- The next reporter call started at 12:57:10.547, 28 ms after this call ended. Thus the client measured more than the server step. The extra 6.1 s is outside the executor time window. The most probable cause is client-side time, for example the detect calls that the reporter made before and after the step.
- With no frame, each pass returns at once (`ImageDetectionHelper.cs:42-45`). Thus no slow pass is possible in the reported runs.

**Design gaps that the fix closes (FR-010)**:

- `CommandExecutor.cs:697-720`: the loop computes `pollMs` from the remaining time, waits, and then runs a pass with no new deadline check. The last pass starts at the deadline. With a slow pass (low `confidence` on a large frame), the step can end one full pass after the deadline.
- `ImageDetectionHelper.TryDetect` is synchronous and does not get the cancellation token. Thus the 10 s limit of `POST /api/steps/execute` (`StepsEndpoints.cs:8`, `CommandExecutor.cs:252-256`) cannot stop a pass.
- `src/GameBot.Domain/Services/SequenceRunner.cs:1065-1086` has the same loop shape for an inline sequence `WaitForImage` step.

**Decision for the loop (FR-010, loop 2 I1)**: one shared rule gives the next wait. Put it in one pure static helper, `WaitForImageSchedule.NextWait(remaining, pollInterval)`, in `src/GameBot.Domain/Commands/Execution/`. Both `CommandExecutor` and `SequenceRunner.WaitForImageAsync` use it. The rule:

1. Do the first pass at the start of the step (no change).
2. Read the clock. If the current time is at or after the deadline, stop and return the timeout result.
3. If the remaining time is more than the poll interval, wait for the poll interval. This wait is not the last wait.
4. Else, this wait is the last wait. Wait for the remaining time minus 50 ms, or for 0 ms if less than 50 ms remain.
5. After the wait, read the clock again. Start a pass only when the current time is before the deadline. A pass that started before the deadline can finish.
6. After the last pass, stop and return the timeout result if the pass did not find the image.

Thus no pass starts at or after the deadline. A step with `timeoutMs` not more than the poll interval still does two passes when `timeoutMs` is more than 50 ms: one at the start, and one 50 ms before the deadline. This keeps the late-image detection of the code before the fix. The cancellation token is not passed into the matcher in this feature (no change to `ITemplateMatcher` callers; out of scope).

**Worst-case time**: the last pass starts at the latest 50 ms before the deadline. Thus the step completes in not more than `timeoutMs` plus one pass (SC-003).

**Decision for the deadline tests (FR-010, loop 2 U1, written before the fix)**: the tests use a slow pass, not a fake clock. Set `CaptureIntervalMs` = 1000 and `timeoutMs` = 300. The pass stub (a screen source for `CommandExecutor`, a condition evaluator for `SequenceRunner`) records its start time and sleeps 150 ms. It never finds the image. Each test asserts two things:

- The number of passes is 2.
- No pass starts at or after the deadline, with a tolerance of 30 ms. The assertion is: the start offset of each pass, from the start of the step, is less than `timeoutMs` − 50 ms + 30 ms (that is, less than 280 ms).

Expected behavior:

- Before the fix: pass 1 starts at 0 ms and ends at about 150 ms. The loop waits `ceil(remaining)` ≈ 150 ms, so pass 2 starts at about 300 ms or later. The start-offset assertion fails.
- After the fix: pass 1 starts at 0 ms and ends at about 150 ms. The remaining time (about 150 ms) is not more than the poll interval, so the last wait ends at about 250 ms. Pass 2 starts at about 250 ms. It ends at about 400 ms, after the deadline, so no pass 3 starts. Both assertions pass.

The 30 ms tolerance covers the timer resolution of Windows (about 15.6 ms). The tests do not assert on exact times. Add a pure unit test for `WaitForImageSchedule.NextWait` with fixed values: remaining more than the interval, remaining equal to the interval, remaining 120 ms, remaining 30 ms, and remaining 0 ms.

**Why no fake clock**: `CommandExecutor` and `SequenceRunner` read `DateTimeOffset.UtcNow` and use `Task.Delay` with no `TimeProvider`. A `TimeProvider` parameter in both constructors is a larger change, and the slow-pass test gives the same proof. `Task.Delay` waits at least the requested time, and the loop before the fix rounds the wait up (`Math.Ceiling`), so the second pass before the fix starts at or after the deadline. If the test does not fail before the fix on a machine, the task notes MUST record the reason (FR-010).

## R-004: `waitForImageDetails` is null in the reporter's sequence run

**Decision**: This is correct behavior for the step type that the reporter used. No change to the sequence result model.

**Evidence**:

- The execution log entry of `ZZZ.WaitProbeSeq` (12:58:34 UTC) shows step `w` with `stepType` `command`. It ran command `ZZZ.WaitProbe`. Thus the `WaitForImage` step was inside a command, and it ran through `CommandExecutor` (the same defect as R-001).
- `SequenceRunner` sets `WaitForImageDetails` only for an inline sequence `WaitForImage` step (`SequenceRunner.cs:605-606`, `:717-735`). A command step result has no `WaitForImage` details. The `WaitForImage` outcome of a command is in the command step outcomes and in the child execution log entry.
- An inline sequence `WaitForImage` step uses the condition evaluator (`SequenceRunner.cs:1044-1050`, `SequenceExecutionService.StepThrough.cs:72-73`, `ImageVisibleConditionAdapter.cs:21-52`). That path reads the singleton `BackgroundCaptureScreenSource`, which also needs a capture loop. R-002 fixes that path also.

**Consequence for FR-005 and acceptance scenario US1-3**: the tests check `waitForImageDetails` not null, with `exitCondition` `image_detected`, on an inline sequence `WaitForImage` step. A second sequence test wraps the step in a command, and it checks that the command step completes with outcome `executed` and that the nested `WaitForImage` outcome is `executed` / `image_detected`.

## R-005: Debug log of the last detection pass on timeout (FR-012)

**Decision**: `ImageDetectionHelper` gets a pass result type, `DetectionPassResult`, with `FrameAvailable` (bool) and `BestScore` (double?, the best score at or above the matcher threshold, or null). `TryDetect` keeps its current signature for the game-readiness probe and calls the new method. On timeout, `CommandExecutor` writes one debug log line with the session id, the reference image id, the `confidence`, the number of passes, `FrameAvailable`, and `BestScore` of the last pass. When the last pass had no frame, the line says so. The API response does not change.

**Scope (C1 clarification)**: the log line is necessary on all three paths. `CommandExecutor` covers the step-execute path and the force-execute path. `SequenceRunner` covers the inline sequence step. A sequence command step runs its `WaitForImage` step through `CommandExecutor`, so that path also writes the line. R-008 gives the design for `SequenceRunner`.

**Best score on the command path**: `ImageDetectionHelper` runs the matcher at the step `confidence` (`ImageDetectionHelper.cs:59-66`). Thus a match at or above the matcher threshold normally gives a found result. `BestScore` is not null on a not-found pass only when the match is outside the capture bounds (lines 78-80) or the resolver gives an error. In the reported defect, the frame state was the useful value.

**Rationale**: The matcher collects only scores at or above the threshold (`src/GameBot.Domain/Vision/TemplateMatcher.cs:57-68`). A true best score below the threshold needs one more match at threshold 0 after the deadline, which FR-010 does not permit. "No frame" was the real cause of this defect, so the frame state is the most useful value in the log.

**Alternatives considered**: An extra diagnostic match at threshold 0 on timeout. Rejected: it is a new pass after the deadline (FR-010), and it costs time on every timeout.

## R-006: Test design (FR-009, SC-005)

**Decision**: Service-level integration tests with `WebApplicationFactory<Program>`. The test host keeps `GAMEBOT_USE_ADB=false` and replaces these services with `ConfigureTestServices`:

- `ISessionManager`: a fake that gives each new session a `DeviceSerial`. Use the pattern of `ScreenshotFakeSessionManager` in `tests/integration/EmulatorScreenshotCaptureTests.cs:189`.
- `BackgroundScreenCaptureService`: a real instance with a fake `IAdbScreenCaptureProvider` that returns a fixed PNG. Use the pattern of `FixedFrameProvider` in `tests/integration/EmulatorScreenshotCaptureTests.cs:232` and the registration at lines 44-50.
- `IScreenSourceFactory` = `BackgroundCaptureScreenSourceFactory`, and `IScreenSource` = `BackgroundCaptureScreenSource`, so the test uses the same frame path as ADB mode.

The fixed frame is a 540x960 image of seeded noise. The reference image is a 58x22 crop of that frame. A second frame has a different seed, so the image is not on it.

**Why the found tests fail before the fix**: the session comes from `POST /api/sessions`, so no loop starts and the cache stays empty. After the fix the loop starts, and the fake provider fills the cache.

**Test list (FR-009, C2 and C3 clarifications)**:

- Found case on each of the three paths. Each found test checks the status and that the step completes before `timeoutMs`. The sequence path has two found tests: an inline `WaitForImage` step (`waitForImageDetails` not null, `exitCondition` `image_detected`) and a command step that contains the `WaitForImage` step (command step outcome `executed`, nested outcome `executed` / `image_detected`).
- Timeout case on each of the three paths, with a no-image frame. Each test checks `completed_timeout` / `timeout_elapsed` (inline sequence: `timeout_elapsed`). It also checks the elapsed time with fixed limits (loop 2 A1): not more than `timeoutMs` + 500 ms, and less than 2 × `timeoutMs`. Use `timeoutMs` = 1500 or more, so that the two limits do not conflict.
- One comparison test: on the same frame and at the same `confidence`, the step result agrees with `POST /api/images/detect` with the `sessionId`. Run it on an image frame and on a no-image frame (SC-004).
- Four edge-case tests on the step-execute path:
  1. The image comes on screen later. The fake provider gives the no-image frame first, and the image frame after about 300 ms. The step finds the image before `timeoutMs`.
  2. Low `confidence` (0.5). The step finds the image before `timeoutMs` on the image frame. On the no-image frame, it times out in not more than `timeoutMs` + 500 ms, and in less than 2 × `timeoutMs`.
  3. Alternate. The frame contains only the alternate image. Set the alternate with `PUT /api/images/{id}/alternates`. The step finds the image.
  4. No frame yet. The fake provider gives no frame (it returns null or throws). The step times out and never reports `image_detected`.

- Capture loop lifecycle (FR-013, loop 2 C1 and I2):
  1. Integration: `DELETE /api/sessions/{id}` stops the loop. Make a session with `POST /api/sessions`, wait for the first frame, and send `DELETE`. After the call, `GetCachedFrame(id)` and `GetCaptureMetrics(id)` of the capture service give `null`.
  2. Integration: stub mode starts no loop. Use a host with `GAMEBOT_USE_ADB=false` and no test replacement of the capture service. `POST /api/sessions` gives `201`, `GetService<BackgroundScreenCaptureService>()` gives `null`, and `GET /api/sessions/running` gives a null `captureRateFps` for the session.
  3. Unit: the idle sweep stops the loop. Use a real `SessionManager` with `IdleTimeoutSeconds` = 1, a real `BackgroundScreenCaptureService` with a fake provider, and `SessionCaptureCleanup.Attach`. Make a session, start its loop, wait more than 1 s, and call `ListSessions()`. Then `GetCaptureMetrics(id)` gives `null`.
  4. Unit: `SessionManager.StopSession` raises `SessionRemoved` one time, and `StopSession` of an unknown id raises no event.

**Fake provider**: the provider holds the current frame in a field that the test can change. This supports edge case 1 and the comparison test with one host.

**Alternatives considered**: Unit tests with `ScreenSourceStub` and `TemplateMatcherStub` (as `tests/unit/Commands/CommandExecutorWaitForImageTests.cs`). Rejected as the only tests: they bypass the session lifecycle, so they pass before the fix. The current integration tests (`tests/integration/Commands/WaitForImageExecutionIntegrationTests.cs`, `tests/integration/Sequences/WaitForImageSequenceExecutionIntegrationTests.cs`) use a 1x1 stub frame through `FixedScreenSourceFactory`, which also bypasses the loop.

## R-007: Technical context items

- **Language and dependencies**: C# on .NET, ASP.NET Core minimal API, OpenCvSharp, System.Drawing. No new package.
- **Performance**: a session from `POST /api/sessions` now has one capture loop at `CaptureIntervalMs`, the same cost as a session from `POST /api/sessions/start` or a queue. The detection pass does not change. The deadline check adds one clock read per poll. The `SessionRemoved` event adds one delegate call for each removal of a session.
- **PR performance note (Principle IV)**: the PR description MUST contain this note. Write it together with the version bump, when you write the PR. Text: "`POST /api/sessions` now starts one capture loop for each session with a device, at `CaptureIntervalMs`. This is the same cost as a session from `POST /api/sessions/start` or a queue session. The detection pass does not change. The deadline check adds one clock read for each poll."
- **Web UI**: no change.
- **Living documentation**: `docs/architecture.md` describes the capture loop (about lines 968-1008) but not which session paths start it. Add the rule for the three session-creation paths, the rule that each removal of a session stops its loop (`DELETE`, idle sweep, and other stops), and the `WaitForImage` deadline rule (no pass at or after the deadline, the last wait ends 50 ms before the deadline). Refresh the "Last reviewed" date.

## R-008: Timeout log line in `SequenceRunner` (FR-012)

**Decision**: add an ambient scope, `ImageDetectionPassScope`, in `src/GameBot.Domain/Vision/`. It holds the last pass of the current wait: `FrameAvailable` (bool) and `BestScore` (double?). `ImageMatchEvaluator.ComputeSimilarity` (`ImageMatchEvaluator.cs:59-81`) writes to `ImageDetectionPassScope.Current` when a scope is present: `FrameAvailable` false when `GetLatestScreenshot` gives null (line 62), else true and the best similarity of the image and its alternates. `SequenceRunner.WaitForImageAsync` pushes a scope at the start of the wait and disposes it at the end. On timeout, it writes one debug line with the sequence id, the step key, the reference image id, the `confidence`, the number of passes, the frame state, and the best score.

**Rationale**:

- The inline sequence step gets only a `bool` from the condition evaluator (`Func<Condition, CancellationToken, Task<bool>>`). The evaluator flows through about 40 private methods of `SequenceRunner`. A new parameter in all of them is a large change for one log line.
- The project already uses ambient scopes for the same reason: `OcrTextEvaluationScope` (`src/GameBot.Domain/Services/OcrTextEvaluationScope.cs:69-81`) and `SequenceRunContext`. The call from `SequenceRunner` through `SequenceExecutionService.EvaluateConditionAsync`, `ImageVisibleConditionAdapter`, and `TriggerEvaluationService` to `ImageMatchEvaluator` is on the same async flow, so the `AsyncLocal` value is visible there.
- The sequence matcher runs at threshold 0 (`ImageMatchEvaluator.cs:143`). All scores are at or above the matcher threshold, so the best score is the true best similarity. No extra pass is necessary (FR-010).
- `TriggerEvaluationResult` does not change. It is in the API model `Triggers.LastResult` (`src/GameBot.Service/Models/Triggers.cs:27`), so a new field there is an API change.

**Frame state values**: `true`, `false`, or `unknown`. `unknown` means that no evaluator wrote to the scope, for example a unit test stub or a run with no image evaluator. The best score is a number with three decimals, or `none`.

**Alternatives considered**:

- A new `Reason` value (for example `no_screenshot`) in `ImageMatchEvaluator.Evaluate`. Rejected: the reason is stored in `Trigger.LastResult` and returned by the API. This changes trigger output, and the spec puts `imageVisible` out of scope.
- A new delegate parameter on `SequenceRunner.ExecuteAsync` that returns the pass details. Rejected: see the first rationale item.
- Write the line in `SequenceExecutionService`. Rejected: the C1 clarification puts the line in `SequenceRunner`, and only the runner knows the pass count and the timeout.

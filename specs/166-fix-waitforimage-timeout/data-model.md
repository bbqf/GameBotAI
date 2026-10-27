# Data Model: Fix WaitForImage timeout on an image that is on screen (B-038)

This bug fix adds no stored entity and changes no API shape. This file lists the runtime state that the fix touches.

## Session capture state (runtime only)

| Field | Source | Note |
|-------|--------|------|
| `SessionId` | `EmulatorSession.Id` | Key of the capture loop in `BackgroundScreenCaptureService`. |
| `DeviceSerial` | `EmulatorSession.DeviceSerial` | A loop starts only when this value is not blank. |
| Capture loop | `SessionCaptureLoop` | Writes the latest `CachedFrame` (PNG bytes) at `CaptureIntervalMs`. |

**State transitions**:

| Event | Before the fix | After the fix |
|-------|----------------|---------------|
| `POST /api/sessions` (ADB mode, device bound) | Session `Running`, no loop, cache empty | Session `Running`, loop started, cache fills |
| `POST /api/sessions/start` | Loop started | No change |
| Queue run binds a session | Loop started | No change |
| `DELETE /api/sessions/{id}` | Session stopped, a loop (if any) continues | Loop stopped, then session stopped |
| Idle sweep of `SessionManager` removes the session | Session removed, a loop (if any) continues until a `SessionService` sync | Session removed, `SessionRemoved` raised, loop stopped |
| Other `SessionManager.StopSession` call (for example a queue run end) | Loop stopped by the caller, if the caller stops it | Loop stopped by the `SessionRemoved` handler also (a second stop does nothing) |
| Stub mode (`GAMEBOT_USE_ADB=false`) | No capture service | No change, no loop |

**Validation rules**: no new rule. `StartCapture` requires a non-blank session id and device serial.

## ISessionRemovalNotifier (new, internal to the service)

A small interface in `GameBot.Emulator.Session`. `SessionManager` implements it. Test fakes of `ISessionManager` do not have to implement it.

| Member | Type | Meaning |
|--------|------|---------|
| `SessionRemoved` | `event EventHandler<SessionRemovedEventArgs>?` | Raised one time after a session goes out of the session table. `SessionRemovedEventArgs.SessionId` is the session id. Raised by `StopSession` and by the idle sweep. (`EventArgs` type, so that the .NET analyzers do not report CA1003.) |

`SessionCaptureCleanup.Attach(ISessionManager, BackgroundScreenCaptureService)` subscribes to the event when the session manager implements the interface. The handler calls `StopCapture(sessionId)`. A handler exception does not go back to the session manager: the handler catches it and writes a warning log.

## WaitForImage poll schedule (new, internal)

A pure static helper, `WaitForImageSchedule`, in `GameBot.Domain.Commands.Execution`. `CommandExecutor` and `SequenceRunner` use it.

| Input | Output |
|-------|--------|
| `remaining` (time to the deadline) more than `pollInterval` | wait `pollInterval`, not the last wait |
| `remaining` not more than `pollInterval` and more than 50 ms | wait `remaining` − 50 ms, the last wait |
| `remaining` not more than 50 ms and more than 0 | wait 0 ms, the last wait |
| `remaining` not more than 0 | no wait, no pass, stop |

After each wait, the caller reads the clock again. It starts a pass only when the current time is before the deadline. After the last pass, the caller stops.

## DetectionPassResult (new, internal)

The result of one detection pass in `ImageDetectionHelper`. It is not stored and not in an API response.

| Field | Type | Meaning |
|-------|------|---------|
| `Found` | bool | A match at or above the step `confidence` was found inside the capture. |
| `FrameAvailable` | bool | The screen source gave a frame. False means the session has no cached frame. |
| `BestScore` | double? | The best match score at or above the matcher threshold. Null when no position reached the threshold, or when no frame was available. |
| `ResolvedPoint` | `PrimitiveTapResolvedPoint?` | The point with offsets. Not null only when `Found` is true. |
| `DetectionConfidence` | double? | The score of the selected match. Not null only when `Found` is true. |

`TryDetect` keeps its signature and maps `DetectionPassResult` to its `out` values.

## ImageDetectionPassScope (new, internal to the run)

An ambient scope in `GameBot.Domain/Vision` (an `AsyncLocal` value, the same pattern as `OcrTextEvaluationScope`). `SequenceRunner.WaitForImageAsync` pushes it for one wait. `ImageMatchEvaluator` writes the last pass into it. It is not stored and not in an API response.

| Field | Type | Meaning |
|-------|------|---------|
| `LastPass` | `ImageDetectionPass?` | The last pass that an evaluator wrote. Null when no evaluator wrote to the scope. |
| `ImageDetectionPass.FrameAvailable` | bool | The screen source gave a frame. |
| `ImageDetectionPass.BestScore` | double? | The best similarity of the image and its alternates. Null when no frame was available. |

**Lifecycle**: push at the start of the wait. `ImageMatchEvaluator` replaces `LastPass` on each pass. Dispose at the end of the wait. When no scope is current, `ImageMatchEvaluator` writes nothing, so triggers and `imageVisible` conditions do not change.

## Timeout log line (FR-012, debug level)

One line for each `WaitForImage` step that times out. It is not in an API response.

| Field | `CommandExecutor` (step execute, force execute, sequence command step) | `SequenceRunner` (inline sequence step) |
|-------|-------|-------|
| Context | session id | sequence id, step key |
| Reference image id | yes | yes |
| `confidence` | yes | yes |
| Pass count | yes | yes |
| Frame state | `true` or `false` | `true`, `false`, or `unknown` |
| Best score | number with three decimals, or `none` | number with three decimals, or `none` |

## WaitForImage step outcome (no change)

The outcome fields stay as they are (`PrimitiveTapStepOutcome` for the step and command paths, `WaitForImageStepResultDetails` for an inline sequence step). The fix changes only which values occur in the found case:

| Path | Found | Not found |
|------|-------|-----------|
| Step execute, command force-execute | `status` `executed`, `reason` `image_detected` | `status` `completed_timeout`, `reason` `timeout_elapsed` |
| Sequence, inline `WaitForImage` step | `actionOutcome` `image_detected`, `waitForImageDetails.exitCondition` `image_detected` | `actionOutcome` `timeout_elapsed` |
| Sequence, command step with a `WaitForImage` step | command step outcome `executed`; `waitForImageDetails` stays null for a command step | command step outcome by the current command rules |

# Research: Do not read a stale capture frame in a step (B-043)

## R-001: The cause of the stale window

**Decision**: The main cause is the capture loop. The fix makes the age of the frame in memory visible to each reader in a step, and adds a guard for a device that returns the same pixels after a time-out.

**Rationale**: In `SessionCaptureLoop.RunLoopAsync` (`src/GameBot.Emulator/Session/BackgroundScreenCaptureService.cs`), a capture that times out calls `DeviceCaptureGate.TimedOut`. The device becomes `Suspect`. While the device is `Suspect`, `TryBegin` refuses each capture, and the loop waits `CaptureStallLimitMs` (default 60 s) before it asks the device about the `screencap` process. During this time the cached frame does not change. `BackgroundCaptureScreenSource`, `SessionScopedScreenSource`, and `BackgroundCaptureSessionFrameSource` return this frame with no age check. The screenshot endpoint calls `CaptureSessionStore.Add`, which gives a new GUID for each call. This matches the probe of the issue: one hung capture (60.2 s), then 58 screenshots with the same MD5 and new ids. The liveness evaluator gives `capture_stalled` only after 60 s with no completed capture, which matches the 17:31:52 failure.

**Alternatives considered**: Shorter Suspect check interval. Rejected: feature 121 chose the interval on purpose, to start no new `screencap` process on a blind device. It also does not tell the steps that the frame is old.

## R-002: The freshness rule

**Decision**: A frame is stale when its age is more than the effective limit, or when it has the stale-repeat flag. The effective limit is `max(FrameMaxAgeMs, 2 × CaptureIntervalMs)`. `FrameMaxAgeMs` is a new option in `DeviceLivenessOptions` (default 5000, minimum 1000). The pure helper `FrameFreshness` (Domain) calculates the age and the stale flag from a `CachedFrame`, a time, and the effective limit. The static method `FrameFreshness.EffectiveLimitMs(int frameMaxAgeMs, int captureIntervalMs)` calculates the effective limit. Each user of the limit (the capture loop, `FrameReadScope`, the screenshot endpoint) uses this method.

**Rationale**: Clarification 1 of the spec. The lower bound of two capture intervals prevents a false stale flag when an operator sets a long capture interval (for example 2000 ms). The default capture interval is 500 ms (`GAMEBOT_CAPTURE_INTERVAL_MS`). A normal loop makes a frame well within 5 s. The capture time limit is 10 s, so a frame older than 5 s means that at least one capture did not complete in time.

**Alternatives considered**: "Captured after the start of the step". Rejected: it adds up to one capture interval to each condition read, and it is not a better test of the stall.

## R-003: The stale-repeat rule

**Decision**: `SessionCaptureLoop` keeps `_lastCaptureFailed` (true after a time-out or an error, false after a capture that is kept as a new frame). When a capture returns the same PNG bytes as the current frame and `_lastCaptureFailed` is true, or the current frame already has the stale-repeat flag, the capture is a stale repeat. The loop keeps the current frame (same frame id, same capture time) and sets the flag (`previous with { StaleRepeat = true, StaleRepeatSince = ... }`, the same `Bitmap` instance, which the loop must not dispose). A stale repeat does not call `IDeviceLivenessTracker.RecordCapture`. The flag clears when a capture returns different bytes, or when the repeats continue for `FrameMaxAgeMs`; then the capture is a normal new frame.

**Rationale**: Request 1 of the issue. A still screen is possible, so the repeat window has an upper limit (spec edge case). Because the stale repeat does not update `LastCaptureAt`, the stall rule does not count it as a completed capture (FR-011). With the default values the repeat window (5 s) ends before the stall limit (60 s). Then the rule acts only when `FrameMaxAgeMs` is equal to or more than `CaptureStallLimitMs`. The age rule of the steps (R-002) covers the short windows.

**Alternatives considered**: (a) A content hash on each frame and a compare in each reader. Rejected: the loop already compares the bytes (feature 106, R-003). (b) No time limit on the flag. Rejected: one time-out on a still screen then blocks all steps.

## R-004: How a step reader learns the frame data

**Decision**: A new ambient scope `FrameReadScope` (Domain, `AsyncLocal`, the same pattern as `ImageDetectionPassScope` and `OcrTextEvaluationScope`). `SequenceExecutionService.ExecuteCoreAsync` pushes it for a run that has a session and is not a dry-run. The scope holds `MaxAgeMs`, the clock, the last read (`FrameReadInfo`: frame id, age, stale flag), and a wait delegate. The three decode places call one gate method, `FrameReadScope.Admit(CachedFrame frame)`. With no scope, `Admit` returns true and records nothing, so triggers, `POST /api/images/detect`, and other readers do not change. With a scope, it records the read and returns false for a stale frame; the decode place then returns null (no frame). The scope covers the whole sequence run, so the other step readers of the frame in memory also get no frame on a stale frame: `CommandExecutor` (command `WaitForImage`, through `IScreenSourceFactory.ForSession`), `WaitForStillHandler` (through `ISessionFrameSource`), and `GameReadinessProbe`. Each of them already has a no-frame rule (a pass with no frame, a skipped frame, or "not ready"), so no new code is necessary there (spec edge case).

**Rationale**: `IScreenSource.GetLatestScreenshot()` and `ISessionFrameSource.Capture()` return only a `Bitmap`. A new return type changes many callers and tests. The ambient scope keeps all signatures. A null frame already means "no frame" in each reader: `ImageMatchEvaluator` writes `ImageDetectionPass(false, null)`, the absent mode counts it as `PassKind.Failed` (never absent), and the OCR reader gives `capture-failed`.

**Alternatives considered**: A new `GetLatestFrame()` member on `IScreenSource`. Rejected: it changes the public interface and its test doubles.

## R-005: `WaitForImage` with stale frames

**Decision**: `ImageDetectionPass` gets one more optional member, `FrameReadInfo? Frame`. `ImageMatchEvaluator` copies `FrameReadScope.Current?.LastRead` into the pass. `SequenceRunner.PollImageConditionAsync` and `PollAbsentConditionAsync` count the passes whose `LastPass.Frame.Stale` is true. `WaitForImageAsync` writes `CaptureId`, `FrameAgeMs`, `BestScore`, `PassCount`, `StalePassCount`, and `StaleReason` into `WaitForImageStepResultDetails`. `StaleReason` is `stale_frame` when the exit condition is `timeout_elapsed` and the last pass was stale.

**Rationale**: A stale pass returns false in present mode, so the wait continues (FR-004). The absent mode already does not count a pass with no frame. The outcome rule does not change (FR-005, FR-013).

**Alternatives considered**: A new exit condition `stale_frame`. Rejected: the spec keeps `timeout_elapsed` (non-goal of the issue).

## R-006: `imageVisible` and `ocrText` with a stale frame

**Decision**: Before an `imageVisible` or `ocrText` leaf reads the screen, `SequenceStepConditionEvaluator` calls `FrameReadScope.Current?.EnsureFreshAsync(ct)`. The delegate, made by the service, reads `BackgroundScreenCaptureService.GetCachedFrame(sessionId)` at each capture interval until the frame is fresh or `MaxAgeMs` has passed. No frame at all counts as "not stale" (the current no-frame rule applies). When the wait ends with a stale frame, the leaf throws `ConditionEvaluationException` with the new kind `StaleFrame` and a message that contains `stale_frame`. The frame can become stale between the end of the wait and the read. Thus, after each screen read of the leaf, the evaluator also checks `FrameReadScope.Current.LastRead.Stale`, and throws `StaleFrame` when it is true. A negated leaf then never becomes true on a refused stale frame. For `ocrText`, the evaluator first adds one `OcrTextEvaluationRecord` with the reason `stale-frame` (new code in `OcrTextFailureReasons`). `SequenceRunner` already turns a `ConditionEvaluationException` into a failed step.

**Rationale**: Clarification 2 and FR-006. A plain `false` becomes `true` with `negate`, which is the wrong decision that the issue reports. The exception path exists and is tested.

**Alternatives considered**: Return false and ignore `negate`. Rejected: it gives a third value that the composite rules do not know.

## R-007: The log of a condition read

**Decision**: For `imageVisible`, the evaluator pushes an `ImageDetectionPassScope` around each image check and adds one line to `CommandStepStateScope` (the same channel as the `parameter` and `matchedImage` lines): `imageVisible(<id>) capture=<id> ageMs=<n> score=<s>`. For `ocrText`, `OcrTextEvaluationRecord` gets two optional members, `CaptureId` and `FrameAgeMs`, and `ToLogLine` adds ` capture=<id> ageMs=<n>` when they are set. `OcrTextConditionReader` reads `FrameReadScope.Current?.LastRead` after `Capture`, and returns the data in `OcrTextReadResult` (two optional members).

**Rationale**: Clarification 5. The current log line shapes stay; only new text is added at the end.

## R-008: The capture id of the screenshot endpoint

**Decision**: `CachedFrame` gets `FrameId` (a GUID "N" string, made by the loop for each new frame). The screenshot endpoint calls a new `CaptureSessionStore.GetOrAdd(string id, byte[] png)` with the frame id, and writes this id to `X-Capture-Id`. The direct-capture path keeps `Add` (new random id). `X-Capture-Stale` is `true` when the report is stale (current rule) or when `FrameFreshness` gives stale for the frame.

**Rationale**: FR-009 and FR-010, and clarification 4: the same id in the step log and in the screenshot answer. `GetOrAdd` keeps the crop flow, because the id is in the store.

**Alternatives considered**: A hash of the bytes as id. Rejected: a still screen then gives the same id for two different captures, which is not "a new frame".

## R-009: The liveness check during a step

**Decision**: No new timer. `QueueLivenessWatch` already runs every `QueueCheckIntervalMs` for the whole run, also during a step (feature 106, FR-017). The change is FR-011 (R-003): a stale repeat does not update `LastCaptureAt`. The API schema text of the liveness check (`DeviceLivenessSchemaFilter`) states both facts.

**Rationale**: The answer to the question of the issue owner is "yes". The step itself now sees a stale frame at once (R-004), so it does not need the watch to act.

## R-010a: The capture interval in the run service

**Decision**: `SequenceExecutionService` reads `CaptureIntervalMs` from `AppConfig` (the same object that `SequenceRunner` gets as `_config` and that `IConfigApplier` updates). Add `AppConfig?` as an optional constructor parameter when the service does not have it yet. With no `AppConfig`, use 500 ms.

**Rationale**: The configuration applier changes `AppConfig.CaptureIntervalMs` at run time, so the service must read the value at each run, not one time at the start.

## R-010: Performance

**Decision**: No measurable change. `Admit` is one subtraction and one compare per read. The stale-repeat check uses the byte compare that the loop already does. The wait of a condition read adds time only when the frame is stale, and at most `FrameMaxAgeMs`.

## R-011: Living documentation

**Decision**: Update `docs/architecture.md` (capture loop and frame freshness, screenshot headers, `FrameMaxAgeMs`, `WaitForImage` step details, liveness note; "Last reviewed"), `CHANGELOG.md`, `specs/STATUS.md` (row 197; row 106 to "Implemented (iterated by 121, 197)"), and the `Status` line of spec 106. Spec 166 does not change its rules, so its status stays.

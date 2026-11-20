# Data Model: Do not read a stale capture frame in a step (B-043)

All data is in memory. No stored data changes.

## 1. `CachedFrame` (changed, `src/GameBot.Domain/Sessions/CachedFrame.cs`)

| Field | Type | New | Rule |
|---|---|---|---|
| `PngBytes` | `byte[]` | no | Unchanged. |
| `Bitmap` | `Bitmap` | no | Unchanged. A stale repeat shares the `Bitmap` of the frame before it; the loop disposes a bitmap only when the new frame has a different instance. |
| `Timestamp` | `DateTimeOffset` | no | The capture time of the frame. A stale repeat keeps the time of the frame before it. |
| `Width`, `Height` | `int` | no | Unchanged. |
| `FrameId` | `string` | yes | A GUID ("N" format). New for each new frame. A stale repeat keeps the id of the frame before it. Default for old callers: a new GUID. |
| `StaleRepeat` | `bool` | yes | True for a stale repeat (FR-002). Default false. |
| `StaleRepeatSince` | `DateTimeOffset?` | yes | The time of the first capture of the current repeat run. Null when `StaleRepeat` is false. |

State changes of the frame in memory (in `SessionCaptureLoop`):

| Current state | Capture result | New state |
|---|---|---|
| any | time-out or error | Frame unchanged. `_lastCaptureFailed = true`. |
| no frame | PNG | New frame (new id, now, `StaleRepeat = false`). |
| frame | PNG with different bytes | New frame. `_lastCaptureFailed = false`. `RecordCapture(changed: true)`. |
| frame, `_lastCaptureFailed = false`, not stale repeat | PNG with same bytes | New frame (new id, now). `RecordCapture(changed: false)`. Current rule. |
| frame, `_lastCaptureFailed = true` or `StaleRepeat = true`, repeat run shorter than `FrameMaxAgeMs` | PNG with same bytes | Same frame, `StaleRepeat = true`, `StaleRepeatSince` set at the first repeat. No `RecordCapture`. |
| frame with `StaleRepeat = true`, repeat run at or above `FrameMaxAgeMs` | PNG with same bytes | New frame (new id, now, flag clear). `_lastCaptureFailed = false`. `RecordCapture(changed: false)`. |

## 2. `FrameFreshness` (new, static, `src/GameBot.Domain/Sessions/FrameFreshness.cs`)

| Member | Rule |
|---|---|
| `int EffectiveLimitMs(int frameMaxAgeMs, int captureIntervalMs)` | `max(frameMaxAgeMs, 2 × captureIntervalMs)`. |
| `long AgeMs(CachedFrame frame, DateTimeOffset now)` | `max(0, now - frame.Timestamp)` in ms. |
| `bool IsStale(CachedFrame frame, DateTimeOffset now, int limitMs)` | `frame.StaleRepeat` or `AgeMs > limitMs`. `limitMs` is the effective limit. |
| `FrameReadInfo Describe(CachedFrame frame, DateTimeOffset now, int limitMs)` | The three values in one record. |

In this data model, "`FrameMaxAgeMs`" in a rule means the effective limit (spec FR-003).

## 3. `FrameReadInfo` (new record, `src/GameBot.Domain/Sessions/FrameReadInfo.cs`)

| Field | Type | Rule |
|---|---|---|
| `FrameId` | `string` | `CachedFrame.FrameId`. |
| `AgeMs` | `long` | `FrameFreshness.AgeMs`. |
| `Stale` | `bool` | `FrameFreshness.IsStale`. |

## 4. `FrameReadScope` (new, `src/GameBot.Domain/Sessions/FrameReadScope.cs`)

Ambient (`AsyncLocal`) for the time of one sequence run with a session.

| Member | Rule |
|---|---|
| `MaxAgeMs` | The effective limit: `FrameFreshness.EffectiveLimitMs(options.FrameMaxAgeMs, appConfig.CaptureIntervalMs)` at the start of the run. |
| `LastRead` | The `FrameReadInfo` of the last admitted or refused frame. Null before the first read. |
| `static Current` | The scope of the current flow, or null. |
| `static IDisposable Push(FrameReadScope scope)` | Makes the scope current until dispose. |
| `static bool Admit(CachedFrame frame)` | No scope: true, records nothing. Scope: records `LastRead`, returns `!Stale`. |
| `Task<bool> EnsureFreshAsync(CancellationToken ct)` | Calls the wait delegate. No delegate: true. True when the frame is fresh or no frame exists; false when the frame is still stale after `MaxAgeMs`. |

## 5. `DeviceLivenessOptions` (changed)

| Field | Default | Minimum | Rule |
|---|---|---|---|
| `FrameMaxAgeMs` | 5000 | 1000 (`MinFrameMaxAgeMs`) | `Normalized()` applies the minimum. Configuration key `Service:DeviceLiveness:FrameMaxAgeMs`. The users apply `FrameFreshness.EffectiveLimitMs` with the capture interval. |

## 6. `ImageDetectionPass` (changed, `src/GameBot.Domain/Vision/ImageDetectionPassScope.cs`)

`ImageDetectionPass(bool FrameAvailable, double? BestScore, FrameReadInfo? Frame = null)`. `Frame` is the `LastRead` of the `FrameReadScope` at the pass. Null when no scope is current.

## 7. `WaitForImageStepResultDetails` (changed, `SequenceRunner.cs`) and `WaitForImageDetailAttributes` (changed, `ExecutionLogModels.cs`) and `ExecutionLogWaitForImageDetailAttributesDto` (changed)

New fields, all omitted from JSON when null:

| Field | Type | Rule |
|---|---|---|
| `CaptureId` | `string?` | `FrameId` of the last pass. Null when the last pass had no frame data. |
| `FrameAgeMs` | `long?` | Age of the frame of the last pass. |
| `BestScore` | `double?` | Best score of the last pass. Null when the last pass had no frame. |
| `PassCount` | `int?` | Number of passes. |
| `StalePassCount` | `int?` | Number of passes on a stale frame. |
| `StaleReason` | `string?` | `stale_frame` when the exit condition is `timeout_elapsed` and the last pass was stale. Else null. |

Execution log detail keys: `captureId`, `frameAgeMs`, `bestScore`, `passCount`, `stalePassCount`, `staleReason`.

## 8. OCR records (changed, `OcrTextEvaluationScope.cs`)

- `OcrTextFailureReasons.StaleFrame = "stale-frame"` (new).
- `OcrTextReadResult(string? Text, string? Parsed, string? FailureReason, string? CaptureId = null, long? FrameAgeMs = null)`.
- `OcrTextEvaluationRecord` gets `string? CaptureId = null, long? FrameAgeMs = null` at the end. `ToLogLine` adds ` capture=<id> ageMs=<n>` when `CaptureId` is set.

## 9. `ConditionEvaluationFailureKind` (changed)

New value `StaleFrame`. The message of the exception contains `stale_frame` and the frame age.

## 10. `CaptureSessionStore` (changed)

New method `CaptureSession GetOrAdd(string id, byte[] png)`: returns the stored capture with this id, or stores a new one under this id. The trim rule does not change.

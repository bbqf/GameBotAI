# Research: Detect does not report a time-limited measurement as an absence

## R-001: Which code path gives the empty response

**Finding**: `ImageDetectionsEndpoints.DetectAsync` starts the match with a linked `CancellationTokenSource` and `CancelAfter(Math.Max(1, TimeoutMs))`. In `catch (OperationCanceledException)`, it returns `Results.Ok(new DetectResponse { LimitsHit = true })`. That object has an empty `matches`, `masked: false` and `retainedPixelCount: 0`. This is exactly the response in the issue.

**Other paths**: `TemplateMatcher` and `ReferenceSetTemplateMatcher` set `limitsHit` only when `maxResults` cuts a non-empty list. Thus a completed match never gives `limitsHit: true` with no matches.

## R-002: Why images with alternates meet the time limit

**Finding**: `ReferenceSetTemplateMatcher.MatchAllAsync` scores the primary and then each alternate, one after the other, with the same cancellation token. The endpoint sets one limit (`TimeoutMs`, default 500 ms) for the whole set.

**Cost of one reference**: `TemplateMatcher` adds each position with a score at or above the threshold to a list, sorts the list, and applies NMS. With threshold `0.05`, most positions on a 1080x1920 screen are candidates. The list, the sort and the NMS pass take a large part of 500 ms.

**Result**: The primary alone stays below the limit (0 of 60 and 0 of 20 empty answers in the issue). Three references take approximately three times longer and go near the limit or above it. The limit expires in some calls only, and the rate increases with the number of alternates.

## R-003: Which fix removes the cause

**Decision**: The time limit of a call is `max(1, TimeoutMs) × referenceCount`. `referenceCount` is 1 plus the number of alternates that loaded (`ReferenceImageSet.Alternates.Count`). The service calculates the value in `long` and caps it at `int.MaxValue` ms.

**Rationale**: Each reference gets the same limit that it gets alone. An image without alternates keeps the same limit. The matcher and its scores do not change.

**Alternatives considered**:
- Make the matcher faster (for example, keep only the best `maxResults` candidates). Rejected: the NMS result depends on the full sorted list, and a change can alter results that the thresholds of the live queues depend on.
- Remove the time limit. Rejected: the limit protects the service from a very slow request.
- Score the references in parallel. Rejected: more CPU load and more memory, and the result order must stay the same.

## R-004: What the service returns when the limit expires

**Decision**: `504` with `{ "code": "detection_timeout", "message": "..." }`.

**Rationale**: The issue permits an explicit failure. The API uses `504 device_timeout` for another operation that did not complete in its time limit. The other detect failures use the same `{ code, message }` shape (feature 085).

**Alternatives considered**:
- Return the matches found before the limit. Rejected: a reference that the service did not score can have a better match, and a partial empty list still looks like an absence.
- `503`. Rejected: `503 emulator_unavailable` means that no screen is available, which is a different failure.

**Request abort**: When the client closes the request (`ct` is cancelled), the service does not log a time limit failure. The catch clause uses `when (!ct.IsCancellationRequested)`, so the abort goes to ASP.NET Core as before.

## R-005: Consumers that do not use the time limit

**Finding**: `ImageDetectionHelper.TryDetect` (sequence `imageVisible` conditions) and `CommandExecutor` (image-anchored taps) call `CreateMatcher` and `ActionExecutionAdapter.TryApplyDetectionCoordinates`. `DetectionCoordinateResolver` calls `MatchAllAsync(screenMat, templateMat, config)` without a cancellation token. `ImageMatchEvaluator` uses `CancellationToken.None`. None of these paths uses `DetectionOptions.TimeoutMs`.

**Decision**: No change to these consumers (spec FR-008).

## R-006: Tests

**Decision**:
- Unit test of the time limit calculation (no OpenCV, no Windows API).
- Integration tests with `WebApplicationFactory<Program>.WithWebHostBuilder(...ConfigureTestServices(...))`. The tests replace `ITemplateMatcher` with a matcher that waits for a fixed time for each reference and then calls the real `TemplateMatcher`. They set `DetectionOptions.TimeoutMs` with `PostConfigure`. This makes the time deterministic.
  - Alternates: 250 ms for each reference, `TimeoutMs` 500, two alternates. Before the fix: 750 ms > 500 ms, so the call gave the empty 200. After the fix: 750 ms < 1500 ms, so the call gives the match.
  - Time limit: a matcher that waits until cancellation. The call gives `504 detection_timeout`.
- The old test `TimeoutReturnsOkWithLimitsHit` records the defect as the expected behaviour. The fix removes it; the new time limit test replaces it.
- Contract test: the OpenAPI detect operation has a `504` example with `detection_timeout`, and the description names `limitsHit`.

# Contract: Frame freshness in steps, screenshot headers, and step log (B-043)

## 1. `GET /api/emulator/screenshot`

The selection of the session and the error answers do not change.

### Answer from the frame of the capture loop

| Header | Value | Change |
|---|---|---|
| `X-Capture-Id` | The frame id of the frame in memory. | Changed: the same id for each call that returns the same frame. A new id only after a new capture completed (a stale repeat keeps the id). |
| `X-Capture-Age-Ms` | Milliseconds since the capture of the frame. | No change. A stale repeat keeps the capture time of the first frame, so the age grows. |
| `X-Capture-Unchanged-Ms` | No change. | No change. |
| `X-Capture-Stale` | `true` or `false`. | Changed: `true` when the liveness report is stale (current rule), or the frame age is more than the effective limit (section 2), or the frame is a stale repeat. |

The endpoint stores the frame under its id, so `POST /api/images/crop` with `sourceCaptureId` = this id works.

### Answer from a direct capture

No change: a new random id, `X-Capture-Age-Ms: 0`.

## 2. Configuration

| Key | Default | Minimum | Description |
|---|---|---|---|
| `Service:DeviceLiveness:FrameMaxAgeMs` | 5000 | 1000 | A frame older than the effective limit `max(FrameMaxAgeMs, 2 × capture interval)` is stale. A step does not use a stale frame. |

## 3. `WaitForImage` step details (execution log)

The step entry of a `WaitForImage` step (`stepType: waitForImage`) and `detailAttributes` get these keys. Each key is omitted when its value is null.

| Key | Type | Description |
|---|---|---|
| `captureId` | string | The frame id of the frame of the last pass. |
| `frameAgeMs` | integer | The age of that frame at the read. |
| `bestScore` | number | The best score of the last pass. |
| `passCount` | integer | The number of passes. |
| `stalePassCount` | integer | The number of passes on a stale frame. |
| `staleReason` | string | `stale_frame` when the step ended with `timeout_elapsed` and the last pass was on a stale frame. |

`exitCondition`, `status`, and the other keys do not change. A timeout stays `exitCondition: timeout_elapsed` with status `success`.

## 4. Condition log lines (step message)

- `imageVisible`: one line for each image check: `imageVisible(<imageId>) capture=<frameId> ageMs=<n> score=<s>`. With no frame data: `imageVisible(<imageId>) capture=none score=<s>`.
- `ocrText`: the current line, plus ` capture=<frameId> ageMs=<n>` at the end when the frame data is known. Example: `ocrText: read='250' expected='250' result=true capture=3f2a... ageMs=420`.
- Stale read: `ocrText: read='' expected='250' result=false reason=stale-frame capture=<id> ageMs=<n>`.

## 5. Stale read of a condition

When the frame is stale at the read of an `imageVisible` or `ocrText` leaf, the read waits up to `FrameMaxAgeMs` for a fresh frame. When no fresh frame comes, the step fails. The failure message is:

`Step '<stepKey>' condition evaluation failed: stale_frame: the frame <frameId> is <n> ms old (limit <effective limit> ms).`

The effective limit is `max(FrameMaxAgeMs, 2 × capture interval)` (section 2).

## 5a. Other readers in a step

In a sequence run, a `WaitForImage` command step, a `WaitForStill` step, and the readiness probe of `EnsureGameRunning` get no frame when the frame in memory is stale. The current no-frame rule of each reader applies.

## 6. Device liveness

- A stale repeat is not a completed capture for the rule `capture_stalled`.
- The periodic liveness watch of a queue run runs every `QueueCheckIntervalMs` for the whole run, also while a step runs. The API schema text of the liveness check says this.

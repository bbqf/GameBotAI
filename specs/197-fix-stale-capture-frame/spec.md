# Feature Specification: Do not read a stale capture frame in a step (B-043)

**Feature Branch**: `197-fix-stale-capture-frame`  
**Created**: 2026-11-20  
**Status**: Draft  
**Input**: GitHub issue #407, "B-043: In a queue run, WaitForImage and ocrText read an old frame: timeout while the image is on the screen". Closes #407.

## Background

The service keeps one capture loop for each device session. The loop captures the screen of the device and keeps the last frame in memory. The steps of a sequence (`WaitForImage`, the `imageVisible` and `ocrText` conditions) and the endpoint `GET /api/emulator/screenshot` read this last frame.

At times the capture of a device stalls. A capture call can hang until its time limit. After a capture time-out, the service starts no new capture for a period (the device is "suspect"). During this period, the last frame in memory does not change. The steps read the same old frame again and again, and they cannot know that it is old. The screenshot endpoint gives a new `X-Capture-Id` for each read of the same old frame. The device can also return the same pixels again after a time-out.

Data from the issue:

- Probe `ZZZ.B043Probe`: after one capture call that hung for 60.2 s, 58 screenshots in a row had the same MD5, and each had a new `X-Capture-Id`. The screen is animated, so outside this window no two frames in a row are equal. All 5 timed-out waits of the probe are in this window.
- Production: 15 `WaitForImage` timeouts in 84 runs, 13 of them in 4 clusters. In each cluster, a wait and its retry both time out.
- One `ocrText` condition read the same text (`2)`) three times in about 12 s. The real text on the screen was `250`.

The liveness check gives `capture_stalled` only when no capture completed for longer than the stall limit (default 60 s). A stale frame that is shorter than this limit, or a capture that returns the same pixels, is not detected.

## Clarifications

### Session 2026-11-20

The pipeline runs with no manual review. Each answer is the most reasonable option, chosen from the spec and the code.

- Q: Which rule makes a frame "not older than the step": a frame captured after the step started, or a frame age limit? → A: A frame age limit (`FrameMaxAgeMs`, default 5000 ms, minimum 1000 ms) plus the stale-repeat flag. Rationale: a "captured after the step started" rule adds up to one capture interval to each condition read; an age limit catches the stall windows of the issue (5 s to 60 s) with no added time on a normal loop.
- Q: How long does an `imageVisible` or `ocrText` read wait for a fresh frame before it fails? → A: Up to `FrameMaxAgeMs`, with a check at each capture interval; then the read fails with the stale reason. Rationale: the wait has a known upper limit, and a normal loop makes a fresh frame in much less time.
- Q: Which reason code names a stale read? → A: `stale_frame` for `WaitForImage` and `imageVisible` (the snake_case style of the step outcomes), and `stale-frame` in the `ocrText` reason list (the kebab-case style of `OcrTextFailureReasons`, for example `capture-failed`). Rationale: each code follows the style of the list that it is added to.
- Q: Is the capture id in the step log the same value as `X-Capture-Id` of the screenshot endpoint? → A: Yes. The capture loop gives each new frame one frame id. The step log and the screenshot endpoint both use this id, and the endpoint stores the frame for crop requests under this id. Rationale: the operator can then compare a step result with a screenshot by its id (request 2 of the issue).
- Q: Where in the execution log does the frame data go? → A: For `WaitForImage`, as new fields of the current step details (`captureId`, `frameAgeMs`, `bestScore`, `passCount`, `stalePassCount`, `staleReason`). For `imageVisible` and `ocrText`, as `capture=<id> ageMs=<n>` (and `score=<s>` for an image) in the current condition log line of the step. Rationale: this uses the current log shapes and adds no new log entry type.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A step does not count a stale frame as a fresh read (Priority: P1)

An operator runs a queue. A sequence has a `WaitForImage` step or an `imageVisible` or `ocrText` condition. The capture of the device stalls for some seconds during the step. The step must not decide on the old frame as if it were the current screen. When the capture becomes fresh again before the step ends, the step reads the fresh frame and finds the image.

**Why this priority**: This is the defect of the issue. Wrong decisions on old frames cause false timeouts, retries, and wrong branches in production runs.

**Independent Test**: Use a test capture source that stops to give new frames (the frame in memory stays the same and gets old), then gives a fresh frame that shows the image. Run a `WaitForImage` step with a time limit that is longer than the stall. Make sure that the step does not finish on a stale pass, and that it detects the image on the first fresh frame.

**Acceptance Scenarios**:

1. **Given** the frame in memory is older than `FrameMaxAgeMs`, **When** a `WaitForImage` pass reads it, **Then** the pass is a stale pass: it does not count as "image not seen" and it does not count as "image seen", and the wait continues.
2. **Given** a `WaitForImage` step that sees only stale frames until its time limit, **When** the time limit ends, **Then** the step ends with `exitCondition: timeout_elapsed` and status `success` (the current rule), and its log names the stale frame as the cause (reason `stale_frame`).
3. **Given** the frame becomes fresh again during the wait and shows the image, **When** the next pass reads it, **Then** the step ends with `image_detected`.
4. **Given** an `imageVisible` or `ocrText` condition, **When** the frame in memory is stale at the time of the read, **Then** the condition waits for a fresh frame for up to `FrameMaxAgeMs`; if no fresh frame comes, the read fails with the named stale reason (`stale_frame`, or `stale-frame` for `ocrText`), and the step fails with a message that contains `stale_frame`. The condition is not true, also with `negate`.
5. **Given** a `WaitForImage` step in `until: absent` mode, **When** a pass reads a stale frame, **Then** the pass does not count as an absent capture.
6. **Given** the stale frame in memory shows the image, but the frame is older than `FrameMaxAgeMs`, **When** a `WaitForImage` pass reads it, **Then** the step does not end with `image_detected` on this frame. Before the fix, the step ends with `image_detected` on the old frame.
7. **Given** a negated `imageVisible` condition and a stale frame that does not show the image, **When** the condition is evaluated and no fresh frame comes, **Then** the step fails with `stale_frame`. Before the fix, the condition is true on the old frame.

---

### User Story 2 - The step log shows which frame the step read (Priority: P2)

An operator looks at a `WaitForImage` timeout or a condition result in the execution log of a queue run. The operator must see which frame the step read (its capture id and its age), whether the frame was stale, and the best score, so that the operator can compare the result with a screenshot of the same time.

**Why this priority**: Without this data, the operator cannot tell a stale-frame failure from a real failure. This is request 2 of the issue.

**Independent Test**: Run a `WaitForImage` step and an `imageVisible` and an `ocrText` condition with a test capture source. Read the step details and the condition log lines of the execution log. Make sure that each one gives the capture id, the frame age, and the best score (image) or the read text (OCR). The `WaitForImage` step details also give the stale data.

**Acceptance Scenarios**:

1. **Given** a `WaitForImage` step ends (any exit condition), **When** the operator reads its step details, **Then** the details give the capture id and the frame age of the last pass, the best score of the last pass, the number of passes, and the number of stale passes.
2. **Given** an `imageVisible` condition is evaluated, **When** the operator reads the condition log line of the step, **Then** the line gives the capture id, the frame age, and the best score of the read.
3. **Given** an `ocrText` condition is evaluated, **When** the operator reads the condition log line of the step, **Then** the line gives the capture id, the frame age, and the read text.
4. **Given** a read failed on a stale frame, **When** the operator reads the step log, **Then** the log gives the stale reason (`stale_frame`, or `stale-frame` for `ocrText`) and the age of the stale frame.

---

### User Story 3 - The screenshot endpoint does not hide a stale frame (Priority: P2)

An operator or a probe script calls `GET /api/emulator/screenshot` many times. When the frame in memory did not change, the endpoint must not give a new capture id as if the frame were new. When the frame is stale, the endpoint must mark it as stale.

**Why this priority**: The probe of the issue found the defect through this endpoint. A new id for the same old frame misleads each client.

**Independent Test**: Call the endpoint two times with no new capture between the calls. Make sure that both answers have the same `X-Capture-Id`. Make the frame older than `FrameMaxAgeMs`, and make sure that `X-Capture-Stale` is `true`.

**Acceptance Scenarios**:

1. **Given** no new capture completed between two calls, **When** a client calls the endpoint two times, **Then** both answers have the same `X-Capture-Id`.
2. **Given** a new capture completed between two calls, **When** a client calls the endpoint two times, **Then** the answers have different `X-Capture-Id` values.
3. **Given** the frame in memory is stale, **When** a client calls the endpoint, **Then** the answer has `X-Capture-Stale: true` and `X-Capture-Age-Ms` gives the age of the frame.
4. **Given** a capture after a capture time-out returns the same pixels as the frame before it, **When** a client calls the endpoint, **Then** the answer keeps the capture id of the earlier frame and has `X-Capture-Stale: true`.

---

### User Story 4 - The liveness check sees a stall while a step runs (Priority: P3)

An operator has a queue run with a long step. The capture stalls during the step. The liveness check must detect `capture_stalled` during the step, not only at the start of the next sequence.

**Why this priority**: Request 3 of the issue. The periodic liveness watch of a queue run already runs during steps; this story makes sure that a capture that returns only stale repeats also counts as a stall, and documents the answer to the question of the issue owner.

**Independent Test**: Use a test capture source that, after a capture time-out, returns only the same pixels. Set `FrameMaxAgeMs` equal to or more than the stall limit. Make the time pass beyond the stall limit while a step runs. Make sure that the liveness report of the session gives `not_live` with `capture_stalled`.

**Acceptance Scenarios**:

1. **Given** a capture loop runs with `FrameMaxAgeMs` equal to or more than the stall limit, and each capture after a time-out returns the same pixels, **When** this continues for longer than the stall limit, **Then** the liveness report gives `not_live` with the reason `capture_stalled`.
2. **Given** a queue run and one of its steps runs, **When** the liveness report becomes `not_live` with `capture_stalled`, **Then** the periodic liveness watch of the run sees the state at its next check (it does not wait for the next sequence start).
3. **Given** a fresh frame with different pixels comes after the stall, **When** the liveness report is evaluated, **Then** the state is `live` again.

---

### Edge Cases

- The device shows a still screen (no animation), and captures complete in time. Same pixels with no capture time-out before them are a normal fresh frame, not a stale repeat. They get a new capture id only when a new capture completed, and they are not stale.
- A still screen after a capture time-out: the same-pixel captures are stale repeats for at most `FrameMaxAgeMs`. After this time, the next same-pixel capture is a normal new frame, so a still screen never blocks the steps for longer than `FrameMaxAgeMs`.
- The first frame of a capture loop: it is fresh when its age is within the limit.
- No frame in memory at all: the current behaviour stays (a pass with no frame).
- `FrameMaxAgeMs` is shorter than two capture intervals (for example, the capture interval is 2000 ms): the effective limit is two capture intervals (FR-003), so a normal frame is not stale between two captures.
- A step with a time limit shorter than `FrameMaxAgeMs`: the step can still end on the first fresh pass; when all passes are stale, it ends with `timeout_elapsed` and reason `stale_frame` in the log.
- The capture loop restarts (new session or device repair): the stale-repeat state starts from nothing.
- Triggers and other readers that do not run in a step: they keep their current behaviour, except that the screenshot endpoint marks a stale frame.
- Other readers of the frame in memory that run in a step of a sequence run (a `WaitForImage` command step, a `WaitForStill` step, the readiness probe of `EnsureGameRunning`): a stale frame is "no frame" for them, and the current no-frame rule of each reader applies. Thus no step of a sequence run decides on a stale frame.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST keep, for each frame in memory, the time of the capture that made it, a frame id that changes only when a new capture completed, and a stale-repeat flag.
- **FR-002**: The service MUST mark a frame as a stale repeat when the capture before it timed out (or failed) and the new capture returned the same pixels as the frame before it. A stale-repeat frame MUST keep the frame id and the capture time of the frame before it. The flag stays until a capture returns different pixels, or until the repeats continue for `FrameMaxAgeMs` (then the screen is still, and the next same-pixel capture is a normal new frame with a new id).
- **FR-003**: The service MUST treat a frame as stale when its age is more than the effective frame age limit, or when it is a stale repeat. `FrameMaxAgeMs` MUST be configurable in the device liveness options, with a default of 5000 ms and a minimum of 1000 ms. The effective limit is `max(FrameMaxAgeMs, 2 × CaptureIntervalMs)`, so that a normal frame never becomes stale between two captures. In this spec, `FrameMaxAgeMs` means this effective limit where a rule uses it.
- **FR-004**: A `WaitForImage` pass that reads a stale frame MUST be a stale pass. A stale pass MUST NOT end the wait, and MUST NOT count as an absent capture in `until: absent` mode. The wait MUST continue on its normal schedule.
- **FR-005**: When a `WaitForImage` step ends with only stale passes after its last fresh pass, or with no fresh pass at all, the step MUST keep `exitCondition: timeout_elapsed` and status `success`, and its log MUST give the reason `stale_frame`.
- **FR-006**: An `imageVisible` or `ocrText` condition read in a step MUST NOT use a stale frame. When the frame is stale, the read MUST wait for a fresh frame up to `FrameMaxAgeMs`, with a check at each capture interval. When no fresh frame comes, the read MUST fail with the stale reason (`stale_frame` for `imageVisible`, `stale-frame` for `ocrText`). The failed read is a condition evaluation failure: the step fails with a message that contains `stale_frame`, as for each other condition evaluation failure. Thus the condition is never true on a stale frame, also when it has `negate`.
- **FR-007**: The step details of a `WaitForImage` step MUST give these new fields: `captureId` and `frameAgeMs` of the last pass, `bestScore` of the last pass, `passCount`, `stalePassCount`, and `staleReason` (`stale_frame`, or null when the step did not end on stale passes).
- **FR-008**: For a step with an `imageVisible` condition, the condition log line of the step MUST give `capture=<id> ageMs=<n> score=<s>` of the read. For an `ocrText` condition, the `ocrText` log line MUST give `capture=<id> ageMs=<n>` in addition to the read text. A failed read MUST give the stale reason (FR-006).
- **FR-009**: `GET /api/emulator/screenshot` MUST give the frame id (FR-001) as `X-Capture-Id`: the same id for the same frame in memory, and a new id only for a new frame. The id in the step log (FR-007, FR-008) MUST be the same value. The endpoint MUST store the frame for crop requests under this id, so crop requests work as before.
- **FR-010**: `GET /api/emulator/screenshot` MUST give `X-Capture-Stale: true` when the frame is stale (FR-003), in addition to the current stale rule of the liveness report.
- **FR-011**: A stale-repeat capture MUST NOT update the last-capture time of the liveness data, so the `capture_stalled` rule does not count it as a completed capture. Thus, when the time with no capture other than a stale repeat is more than the stall limit, the liveness report MUST give `not_live` with `capture_stalled`. With the default values, a repeat run ends after `FrameMaxAgeMs` (FR-002), before the stall limit; this rule acts when `FrameMaxAgeMs` is equal to or more than `CaptureStallLimitMs`.
- **FR-012**: The documentation of the liveness check (the API schema text) MUST say that the periodic liveness watch of a queue run also runs while a step runs, and that a stale-repeat capture does not count as a completed capture.
- **FR-013**: The change MUST NOT change the outcome rule of `WaitForImage` (a timeout gives status `success` with `exitCondition: timeout_elapsed`), and MUST NOT change the result of a step that reads only fresh frames.

### Key Entities

- **Frame in memory (cached frame)**: The last frame of the capture loop of one session. Attributes: the PNG bytes, the capture time, the frame id, the stale-repeat flag.
- **Frame read**: One read of a frame by a step. Attributes: the frame id, the frame age at the read, the stale flag, the best score or the read text, the reason when the read failed.
- **Device liveness options**: The configurable limits of the liveness check. New value: `FrameMaxAgeMs`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a test with a stalled capture of 60 s, then a fresh frame that shows the image, a `WaitForImage` step with a time limit of 70 s ends with `image_detected` in 100% of runs, and no pass on the stale frame ends the wait.
- **SC-002**: In a test with only stale frames, 100% of `WaitForImage` timeouts give the reason `stale_frame` in the log, and 0% of stale passes count as an absent capture.
- **SC-003**: In 100% of tests, the `WaitForImage` step details and the `imageVisible` and `ocrText` condition log lines in the execution log give the capture id and the frame age of the read.
- **SC-004**: Two screenshot calls with no new capture between them give the same capture id in 100% of tests; a stale frame gives `X-Capture-Stale: true` in 100% of tests.
- **SC-005**: A capture loop with `FrameMaxAgeMs` equal to or more than the stall limit, which gives only stale repeats for longer than the stall limit, gives `capture_stalled` in 100% of tests.
- **SC-006**: All current tests of `WaitForImage`, conditions, liveness, and the screenshot endpoint pass with no change to their expected results, except where this spec changes the rule.
- **SC-007**: In a test where the stale frame shows the image, 0% of `WaitForImage` steps end with `image_detected` on the stale frame, and 0% of negated `imageVisible` conditions are true on a stale frame.

## Assumptions

- The cause of the stale window is the capture loop: after a capture time-out the device is suspect, and no new capture starts for up to the stall limit, so the frame in memory gets old. A device can also return the same pixels after a time-out. The fix covers both cases.
- The `FrameMaxAgeMs` default (5000 ms) is ten times the default capture interval (500 ms) and half of the default capture time limit (10000 ms). A normal capture loop makes a new frame well within this limit.
- The game screen is animated in the observed cases, but a still screen is possible. Thus "same pixels" alone is not stale; only same pixels after a failed or timed-out capture is a stale repeat.
- The liveness watch of a queue run already runs every `QueueCheckIntervalMs` during the whole run (also during a step). The answer to the question of the issue owner is "yes, the periodic watch covers a step that runs". The gap was that a stale repeat counted as a completed capture.
- Out of scope: changes to the game, the emulator, the image templates, the `WaitForImage` outcome rule, the web UI, and new features that the issue does not ask for.

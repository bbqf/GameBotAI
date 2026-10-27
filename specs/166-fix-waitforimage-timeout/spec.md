# Feature Specification: Fix WaitForImage timeout on an image that is on screen (B-038)

**Feature Branch**: `166-fix-waitforimage-timeout`  
**Created**: 2026-10-27  
**Status**: Implemented  
**Input**: GitHub issue #350 (https://github.com/bbqf/GameBotAI/issues/350). "Fix: WaitForImage times out on an image that is on screen (B-038)". Closes #350.

## Background

A `WaitForImage` step waits for its full timeout when the reference image is on screen. Then the step reports `completed_timeout` with the reason `timeout_elapsed`. For the same image, `POST /api/images/detect` gives a score of 0.99999994. It gives this score on captures before the step and on captures after the step. An `imageVisible` condition on the same image is true in queue runs. Thus image detection works, but the `WaitForImage` step does not find the image.

The issue reporter measured this on 2026-10-27, after the deployment of the `KeyInput` key-name fix. The reporter used the image `pns-city-anchor` (the word "World" in the bottom bar) on `emulator-5558`.

Measured behaviour (from the issue):

| Path | Setting | Outcome |
| --- | --- | --- |
| `POST /api/commands/{id}/force-execute` | 0.85, 3 s | `completed_timeout`, `timeout_elapsed` |
| `POST /api/steps/execute` | 0.85, 3 s | `completed_timeout`, `timeout_elapsed` |
| `POST /api/steps/execute` | 0.5, 6 s | `completed_timeout`, after 12.1 s |
| `POST /api/sequences/{id}/execute` | 0.85, 5 s | the step ran 5.0 s (`startedAt` to `endedAt`), `waitForImageDetails` null |

`imageLoadStatus` is `loaded`. An image that is not uploaded gives `completed_image_unavailable`. Thus the image lookup itself works.

Two extra symptoms:

- With a 6 s timeout, the step took 12.1 s. This is approximately two times the timeout. (Research R-003: the server step took 6.0 s. The extra time was outside the step.)
- In a sequence run, `waitForImageDetails` is null.

## Clarifications

### Session 2026-10-27

- Q: What is the "found status" on each path? → A: The existing success outcome: status `executed` with reason `image_detected` on the step and command paths, and exit condition `image_detected` in the sequence `waitForImageDetails`. Rationale: the issue asks for no new statuses, and these values already exist in the API and the web UI.
- Q: What is the limit on the time to timeout when the image is not on screen? → A: The step does not start a new detection pass after `timeoutMs` elapses. A pass that started before the deadline can finish. Thus the step completes in not more than `timeoutMs` plus one detection pass. Rationale: a pass cannot stop in the middle, but a pass that starts after the deadline adds time with no use.
- Q: At which level do the automated tests run? → A: Service-level tests that use a fixed screen frame through the session screen source, one for each of the three paths, plus a test that compares the step result with `POST /api/images/detect` on the same frame. Rationale: the defect is between the endpoints and the matcher, so tests of the matcher alone do not find it.
- Q: Must a timed-out step report the best score that it saw? → A: No API change. The service writes the best score of the last detection pass to the service log at debug level. Rationale: the issue does not ask for new fields, but a log value helps to find a similar defect in the future.

### Session 2026-10-27 (loop 1, from the analyze findings)

- Q: Which sequence steps must have `waitForImageDetails` not null (I1)? → A: Only an inline sequence `WaitForImage` step. A command step that contains a `WaitForImage` step reports the `WaitForImage` outcome in its command step outcomes, and its `waitForImageDetails` stays null. Rationale: research R-004 shows that the reporter's sequence step was a command step, and null is correct for that step type.
- Q: What is the "best score" in the timeout log line (I2)? → A: The best score at or above the matcher threshold in the last detection pass, or "none", plus whether the pass had a frame. Rationale: a true score below the threshold needs one more match after the deadline, and FR-010 does not permit that.
- Q: Which paths write the timeout log line (C1)? → A: All three paths: `CommandExecutor` (step-execute and force-execute) and `SequenceRunner` (inline sequence step). Rationale: FR-012 applies to each step that times out, and the same diagnosis is necessary in sequences.
- Q: On which paths do the tests check that the found step completes before `timeoutMs` (C2)? → A: On all three paths. Rationale: FR-002 applies through FR-004 to all paths.
- Q: Which edge cases must have automated tests (C3)? → A: All four: the image comes on screen after the start of the step, low `confidence` (0.5) stays in the time limit, a match on a stored alternate counts as a match, and no frame yet gives no found status. Rationale: each case is cheap to test with a fixed or changed frame, and each case is in the issue scope.
- Q: What caused the 12.1 s result for a 6 s timeout (I3)? → A: Research R-003 shows that the server step took 6.0 s. The extra time was outside the step (on the client side). FR-010 is hardening, not the fix of the reported defect.
- Q: (loop 2, I1) Does the deadline rule remove the last pass of a step whose `timeoutMs` is not more than the poll interval? → A: No. When the remaining time is not more than the poll interval, the last wait ends 50 ms before the deadline, and the step does one last pass before the deadline. "At or after the deadline" is the term in FR-010, the plan, and the tests. Rationale: the step must not lose the late-image detection that the code before the fix has.
- Q: (loop 2, I2) What happens to the capture loop when the idle sweep removes a `POST /api/sessions` session? → A: The service stops the loop when the session manager removes the session for any reason, with a test. Rationale: before the fix these sessions had no loop, so a loop that stays on is a new resource leak.
- Q: (loop 2, C1) Must a test check that `DELETE` stops the loop and that stub mode starts no loop? → A: Yes, both, as integration tests. Rationale: FR-013 is the fix of the defect, so each part needs a test.
- Q: (loop 2, A1) What is the margin in the timeout tests? → A: Elapsed time not more than `timeoutMs` + 500 ms, and less than 2 × `timeoutMs`. Rationale: fixed numbers make the tests clear and not flaky.
- Q: (loop 2, U1) How do the deadline unit tests stay deterministic? → A: They assert on the number of passes and on "no pass starts at or after the deadline" with a 30 ms tolerance. Rationale: Principle II requires deterministic tests, and exact times on the real clock can race.
- Q: What is the root cause of the reported defect? → A: Research R-001: `POST /api/sessions` makes a session but does not start its background capture loop. The `WaitForImage` step reads the frame from that loop, so it gets no frame and times out. The fix starts the capture loop when `POST /api/sessions` makes a session with a device, and stops it when the session is deleted. Rationale: `POST /api/sessions/start` and queue runs already start the loop, and `imageVisible` works in queue runs for this reason.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A WaitForImage step finds an image that is on screen (Priority: P1)

An automation author runs a command or a sequence that has a `WaitForImage` step. The reference image is on screen. The step must find the image and complete with a found status. The step must not wait for its full timeout.

**Why this priority**: Every command that waits for a screen response with `WaitForImage` waits for its full timeout. Then it continues as if the screen did not respond. A new task (resource gathering) uses `WaitForImage` for every game response. That task cannot use fixed waits.

**Independent Test**: Put a screen frame that contains the reference image into the session screen source. Run a `WaitForImage` step on each of the three paths. Make sure that each step completes with a found status before `timeoutMs` elapses.

**Acceptance Scenarios**:

1. **Given** a running session whose screen shows the reference image with a score at or above the step `confidence`, **When** the author sends `POST /api/steps/execute` with a `WaitForImage` step for that image, **Then** the step completes with a found status, and not with `completed_timeout` / `timeout_elapsed`.
2. **Given** the same session, **When** the author sends `POST /api/commands/{id}/force-execute` for a command that has the `WaitForImage` step, **Then** the step completes with a found status.
3. **Given** the same session, **When** the author sends `POST /api/sequences/{id}/execute` for a sequence that has an inline `WaitForImage` step, **Then** the step completes with a found status, and the step result contains the `WaitForImage` details (they are not null). For a sequence command step that contains the `WaitForImage` step, the command step outcomes show the found status.
4. **Given** the image is on screen at the start of the step, **When** the step runs on any of the three paths, **Then** the step completes before `timeoutMs` elapses.
5. **Given** a capture for which `POST /api/images/detect` gives a score at or above the step `confidence`, **When** a `WaitForImage` step runs against the same frame with that `confidence`, **Then** the step finds the image.

---

### User Story 2 - A WaitForImage step still times out correctly when the image is not on screen (Priority: P2)

An automation author runs a `WaitForImage` step for an image that is not on screen. The step must time out with `completed_timeout` / `timeout_elapsed`. The time to timeout must be near `timeoutMs`.

**Why this priority**: The timeout result is the signal that the screen did not respond. It must stay correct, and it must not take approximately two times the timeout.

**Independent Test**: Put a screen frame that does not contain the reference image into the session screen source. Run a `WaitForImage` step. Make sure that the step completes with `completed_timeout` / `timeout_elapsed`, and that the duration is near `timeoutMs`.

**Acceptance Scenarios**:

1. **Given** a running session whose screen does not show the reference image, **When** a `WaitForImage` step runs on any of the three paths, **Then** the step completes with `completed_timeout` / `timeout_elapsed`.
2. **Given** the same session and a step with `timeoutMs` = 6000, **When** the step runs, **Then** the step completes in not more than `timeoutMs` plus one detection pass (SC-003). It does not take approximately 12 s.
3. **Given** a reference image id that is not uploaded, **When** a `WaitForImage` step runs, **Then** the step completes with `completed_image_unavailable`, as before.

### Edge Cases

- The image is not on screen at the start of the step, but it shows on screen before the timeout. The step finds it on a later poll and completes with a found status before the timeout.
- The step `confidence` is low (for example 0.5), so many positions on the screen score at or above it. The step must still complete in the time limit, and it must not take approximately two times the timeout.
- The session has no captured frame yet. The step polls until a frame is available or the timeout elapses. It does not report a found status without a frame.
- The reference image has stored alternates. A match on any alternate counts as a match, as on `POST /api/images/detect`.
- The `WaitForImage` step has no detection target. The step is a plain delay and reports `executed` / `delay_elapsed`, as before.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When the reference image is on the session screen with a score at or above the step `confidence`, a `WaitForImage` step MUST complete with a found status. It MUST NOT complete with `completed_timeout` / `timeout_elapsed`.
- **FR-002**: When the reference image is on screen at the start of the step, the step MUST complete before `timeoutMs` elapses.
- **FR-003**: For the same frame and the same reference image (with its alternates), the `WaitForImage` step MUST find the image when `POST /api/images/detect` gives a match with a score at or above the step `confidence`.
- **FR-004**: FR-001 to FR-003 MUST apply to the step-execute path (`POST /api/steps/execute`), the command force-execute path (`POST /api/commands/{id}/force-execute`), and the sequence-execute path (`POST /api/sequences/{id}/execute`).
- **FR-005**: In a sequence run, the result of an inline `WaitForImage` step MUST contain its `WaitForImage` details (`waitForImageDetails` not null). A command step that contains a `WaitForImage` step reports the outcome in its command step outcomes.
- **FR-006**: When the reference image is not on screen, the step MUST complete with `completed_timeout` / `timeout_elapsed`. The time from the start of the step to the timeout result MUST NOT be more than `timeoutMs` plus one detection pass (SC-003).
- **FR-007**: A reference image id that is not uploaded MUST still give `completed_image_unavailable`.
- **FR-008**: A `WaitForImage` step with no detection target MUST stay a plain delay with `executed` / `delay_elapsed`.
- **FR-009**: Automated tests MUST cover the found case and the timeout case on each of the three paths. The tests MUST run at service level with a fixed screen frame through the session screen source. The found-case tests MUST check the status and that the step completes before `timeoutMs`. One test MUST compare the step result with `POST /api/images/detect` on the same frame. The found-case tests MUST fail without the fix. Automated tests MUST also cover the first four edge cases in the Edge Cases section.
- **FR-010**: The step MUST NOT start a detection pass at or after the deadline (the start of the step plus `timeoutMs`). A pass that started before the deadline can finish. When the remaining time is not more than the poll interval, the last wait MUST end 50 ms before the deadline (or immediately, if less than 50 ms remain), and the step MUST do one last pass. Thus a step with `timeoutMs` not more than the poll interval does at least two passes when `timeoutMs` is more than 50 ms, as the code before the fix does. The deadline tests MUST be deterministic: they assert on the number of passes and on "no pass starts at or after the deadline" with a tolerance of 30 ms, not on exact times. Write the unit tests for this rule (`CommandExecutor` and `SequenceRunner`) before the deadline fix. Where possible, these tests MUST fail before the fix: for example, use a matcher or a screen source whose pass takes time, so that without the fix a pass starts after the deadline. Record the failure before the fix in the task notes. If a test cannot fail before the fix, record the reason.
- **FR-011**: The found status MUST be the existing success outcome: status `executed` with reason `image_detected`, and exit condition `image_detected` in the sequence `waitForImageDetails`.
- **FR-012**: When a step times out on any of the three paths, the service MUST write one debug-level log line. The line gives whether the last detection pass had a frame, and the best score at or above the matcher threshold in that pass (or "none"). The API response does not change.
- **FR-013**: When `POST /api/sessions` makes a session that has a device, the service MUST start the background capture loop of that session. When the session is deleted, the service MUST stop that loop. When the session manager removes the session for another reason (for example, the idle sweep), the service MUST also stop that loop, so that no loop stays on for a session that does not exist. Stub mode (no capture service) does not change. Automated tests MUST check that `DELETE /api/sessions/{id}` stops the loop (the capture service has no cached frame and no metrics for the session after the call), that the removal of an idle session stops the loop, and that stub mode starts no loop.

### Out of scope (non-goals)

- No change to the `imageVisible` condition. It works correctly.
- No change to `POST /api/images/detect`. It works correctly.
- No change to the image lookup or to the `completed_image_unavailable` behaviour.
- No change to the `KeyInput` key-name fix.
- No new `WaitForImage` parameters and no new API endpoints.

### Key Entities

- **WaitForImage step**: A command or sequence step with a detection target (reference image id, `confidence`, offsets, selection strategy, optional region) and a `timeoutMs`.
- **Step outcome**: The result of the step. It has a status (`executed`, `completed_timeout`, `completed_image_unavailable`), a reason (`image_detected`, `timeout_elapsed`, `image_unavailable`, `delay_elapsed`), and the detection details (image load status, configured confidence, detection confidence, resolved point).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the reference image on screen at a score at or above `confidence`, 100% of `WaitForImage` steps in the automated tests complete with a found status on all three paths.
- **SC-002**: With the image on screen at the start of the step, the step completes in less than `timeoutMs`.
- **SC-003**: With the image not on screen, the step completes with `completed_timeout` / `timeout_elapsed` in not more than `timeoutMs` plus the time of one detection pass (FR-010). In the automated tests, the limit is `timeoutMs` + 500 ms, and the time must also be less than 2 × `timeoutMs`.
- **SC-004**: For each frame in the automated tests, the `WaitForImage` result (found or not found) agrees with the `POST /api/images/detect` result at the same `confidence`.
- **SC-005**: The new found-case tests fail on the code before the fix and pass on the code after the fix.

## Assumptions

- The defect is in how the `WaitForImage` step gets the screen frame or runs the match, not in the matcher itself. `POST /api/images/detect` and the `imageVisible` condition use the same matcher and find the image.
- "Found status" is defined in FR-011.
- The poll interval is the existing capture interval of the service. This feature does not change it.
- Automated tests use a fixed screen frame and a fixed reference image. They do not need a real emulator.
- The PR description MUST contain a performance note (Principle IV) on the capture loop that `POST /api/sessions` now starts for each session: one loop at `CaptureIntervalMs`, the same cost as a session from `POST /api/sessions/start` or a queue session. Write this note when you write the PR, together with the version bump.
- Code line references in plan and research: `CommandExecutor.ExecuteWaitForImageStepAsync` starts at `CommandExecutor.cs:606`. The first detection pass is at line 682, the poll loop is at lines 697-720, and the timeout return is at lines 722-733.

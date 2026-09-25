# Feature Specification: Make a wedged emulator visible to the API (B-019)

**Feature Branch**: `106-wedged-device-liveness`  
**Created**: 2026-09-25  
**Status**: Implemented  
**Input**: GitHub issue #220 (B-019): "a wedged emulator is invisible to the API - health is green, inputs report dispatched, screenshots return a stale frame". Full description: see the issue and the feature description that started this spec.

## Background

A device can go into a "wedged" state. In this state, the device applies no input and renders no new frame. But the ADB transport stays up, and `adb get-state` returns `device`. Today, all API responses report such a device as good:

- `GET /api/sessions/{id}/health` reports `adb.ok: true`, because it checks only the transport state.
- `GET /api/emulator/screenshot` returns `200` and the last cached frame. The frame can be many minutes old, or byte-identical to frames from many minutes before. No header or field tells this. When no frame is in the cache, the direct capture has no time limit, and the request does not return.
- `POST /api/sessions/{id}/inputs` has no time limit. On one wedged device, a tap did not return in 30 s. On a different wedged device, a tap returned `dispatched: true` in 0.3 s, but the screen did not change.
- A queue bound to a wedged device stays `Running`. It records no failure that tells the cause.

On 2026-09-21 and 2026-09-23, this fault stopped production farms for hours. Only a person who looked at the screen found it.

## Clarifications

### Session 2026-09-25

The pipeline runs with no manual review. The pipeline chose each answer below, with the rationale.

- Q: Which HTTP status and error code does `POST /api/sessions/{id}/inputs` return when an input times out? → A: `504 Gateway Timeout` with error code `device_timeout`, and the `results` array. Rationale: the device did not answer, so it is not a client error (400); 504 agrees with the current session-create timeout.
- Q: Which HTTP status does `POST /api/sessions/{id}/inputs` return when the inputs were sent but the device is not live? → A: `503 Service Unavailable` with error code `device_not_live`, the liveness reason, and the `results` array (all `dispatched: false`). Rationale: the service is up but the device cannot do the work; a caller that checks only the status code sees the failure.
- Q: What names and units do the new screenshot headers and health fields use? → A: headers `X-Capture-Age-Ms`, `X-Capture-Unchanged-Ms`, `X-Capture-Stale` (`true`/`false`); health block `liveness` with `state`, `reason`, `frameAgeMs`, `unchangedMs`, `stale`, `lastInputAt`, `lastInputOutcome`. A capture timeout returns `504` with error code `capture_timeout`. Rationale: milliseconds agree with the other duration fields of the API (`watchdogTimeoutMs`, `delayMs`).
- Q: For which sequence does a queue record the device-not-live failure, and does it count in the sequence statistics? → A (updated after analysis round 2): The queue holds a firing when the device is not live for a hard reason (FR-016). It does not fail the firing again at each check. In one fault episode, the first gated firing of each sequence writes one failed execution log entry with the reason `device_not_live: <reason>`. That entry also adds one failure to the `sequenceStats` of the sequence. A held firing keeps its daily retry attempt number, and it runs when the device is live again. For each episode, the queue watch also records one failed entry with no sequence and one failed cycle (FR-017). Rationale: the due sequence did not do its work, so the statistics must show it. One entry for each sequence and episode prevents a flood of log entries. A held firing does not use up a daily retry attempt, so a long fault does not cause the loss of the daily task.
- Q: Does the new "not live" rule change the step results of sequences? → A: No. Sequence input dispatch only updates the liveness data (FR-014). The queue gate (FR-016) holds sequences on a device that is not live for a hard reason. Rationale: step outcomes already have their own timeouts; a second rule in the step path would change sequence behaviour that is out of scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Session health tells when the device is not live (Priority: P1)

An operator or a script that monitors the service calls the session health endpoint. The response tells if the device is live (it renders new frames and accepts input), not only if the ADB transport is up. When the device is wedged, the response tells that the device is not live, and it tells why.

**Why this priority**: This is the minimum change that makes the silent outage visible. One call gives a clear good/bad answer that a script can poll.

**Independent Test**: Use a fake capture source that returns no new frames (or returns the same bytes and ignores input). Call the health endpoint. Make sure that the response reports "not live" with a reason. Then let the fake source return frames that change, and make sure that the response reports "live".

**Acceptance Scenarios**:

1. **Given** a session whose capture loop has completed no capture for longer than the capture-stall limit, **When** the caller gets the session health, **Then** the response reports the device as not live, with the reason "capture stalled" and the age of the last frame.
2. **Given** a session whose most recent input did not complete in the input time limit, **When** the caller gets the session health, **Then** the response reports the device as not live, with the reason "input timeout".
3. **Given** a session whose capture loop runs, and an input after the last frame change, **When** no frame changes during the stale limit after that input, **Then** the session health reports the device as not live, with the reason "no screen change after input".
4. **Given** a live device, **When** the caller gets the session health, **Then** the response reports the device as live, and the current `adb` block does not change.
5. **Given** a live device whose screen is static and to which no input was sent, **When** frames stay byte-identical for longer than the stale limit, **Then** the response reports the capture as stale, but does not report the device as not live.
6. **Given** a session with no capture data (for example, the capture loop has not started), **When** the caller gets the session health, **Then** the service does one bounded direct capture. If the capture fails or does not complete in its time limit, the response reports not live. The call always returns in bounded time.

---

### User Story 2 - Screenshot and snapshot responses tell when the frame is stale (Priority: P1)

A caller gets a screenshot or a session snapshot. The response tells how old the frame is and how long the frame has not changed. When the frame is stale, the response says so. The request always returns in bounded time.

**Why this priority**: The operator and the UI that makes reference images use screenshots all the time. A stale frame with no sign causes wrong decisions. For example, a person can crop a reference image from a frozen screen.

**Independent Test**: Put a frame in the capture cache with an old time stamp, or keep the same bytes for longer than the stale limit. Get the screenshot. Make sure that the stale headers are present and correct. Make the direct capture hang, and make sure that the request returns an error in bounded time.

**Acceptance Scenarios**:

1. **Given** a cached frame, **When** the caller gets the screenshot, **Then** the response has headers that give the frame age, the time for which the frame has not changed, and a stale flag. The `X-Capture-Id` header does not change.
2. **Given** a frame that has not changed for longer than the stale limit, or a frame older than the capture-stall limit, **When** the caller gets the screenshot, **Then** the stale flag is `true`.
3. **Given** no cached frame and a device whose direct capture does not complete, **When** the caller gets the screenshot or the session snapshot, **Then** the response is an error that tells the capture timed out, and it returns within the capture time limit plus a small margin. It does not hang until the client stops.
4. **Given** a live device whose screen changes, **When** the caller gets the screenshot, **Then** the stale flag is `false`.

---

### User Story 3 - Session inputs have a time limit and do not claim a false success (Priority: P1)

A caller sends inputs to a session. The call returns in bounded time. If the device does not respond, the result tells the failure. The call does not report `dispatched: true` for an input to a device that the service knows is not live.

**Why this priority**: A hung input call blocks scripts and hides the fault. A false `dispatched: true` makes the caller think that the action occurred.

**Independent Test**: Use a fake ADB input that never returns. Send one tap. Make sure that the call returns after about the input time limit with a timeout failure. Then mark the device as not live, send a tap that the fake ADB accepts, and make sure that the result is not `dispatched: true`.

**Acceptance Scenarios**:

1. **Given** a device whose input command does not return, **When** the caller sends a tap, **Then** the call returns within the input time limit plus a small margin. The result for that action is `dispatched: false` with a timeout reason. The service does not send the actions after it. The HTTP status tells a device timeout (not a client error).
2. **Given** a session whose device is not live at the time of the call, **When** the caller sends inputs and the transport accepts them, **Then** the results are `dispatched: false` with a reason that tells that the device is not live. The service still sends the inputs, so that a device that is only falsely suspected can change its screen and become live again.
3. **Given** a live device, **When** the caller sends a tap, **Then** the result is `dispatched: true`. The caller sees no change in the response time.
4. **Given** an input that timed out, **When** the caller gets the session health after it, **Then** the health reports not live with the reason "input timeout", until a later input completes or the frame changes.

---

### User Story 4 - A queue bound to a wedged device shows the fault (Priority: P2)

An operator looks at a queue that runs (`GET /api/queues/{id}`) or at the execution logs. When the queue's device is not live, the queue shows this, and it records a failure. It does not stay `Running` with no sign.

**Why this priority**: Stories 1 to 3 make the fault visible to a caller who asks. This story makes it visible for unattended operation. It also lets the current failure policy (pause and notification) act on it.

**Independent Test**: Start a queue on a session with a fake device that goes not live. Make sure that the queue health shows the device liveness as not live. Make sure that the execution logs have a failed entry with a device-not-live reason. Make sure that the consecutive-failure count increases, so that a configured failure policy can trip.

**Acceptance Scenarios**:

1. **Given** a queue that runs and whose device is live, **When** the operator gets the queue, **Then** the queue health includes a device liveness block that reports live.
2. **Given** a queue that runs and whose device becomes not live, **When** the operator gets the queue, **Then** the queue health reports the device as not live. It shows the reason, the time at which the queue first observed the fault, and the number of held firings.
3. **Given** a queue whose device is not live for a hard reason (`capture_stalled`, `input_timeout` or `transport_not_ready`), **When** a sequence is due, **Then** the queue does not run it. It holds the firing and the other firings that are due at the same time. It checks the device again after the queue check interval. The first held firing of each sequence in the fault episode records one failed execution with a device-not-live reason. Later held firings of that sequence in the same episode record nothing.
4. **Given** a queue whose device is not live, **When** the device stays not live for longer than the queue grace period, **Then** the queue records one failed entry and one failed cycle for the fault episode. The consecutive-failure count increases by one. This also applies when no firing is due (for example, during an idle pause), and to a queue that does not cycle. The queue does not record one entry for each check.
5. **Given** a queue whose device becomes live again (for example, after a manual reboot), **When** the queue checks the device again, **Then** the held firings run as normal. Self-reschedule chains continue at their original cadence. The queue does not stop by itself, so that it can continue after a manual recovery. (A failure policy that the operator configured can still pause or stop the queue, see FR-018.)
6. **Given** a queue whose device has the reason `no_change_after_input`, **When** a sequence is due, **Then** the queue runs it as normal. The inputs of the sequence can change the screen and clear the state. The queue health still reports the device as not live.

---

### Edge Cases

- **Static but live screen**: The Android launcher shows the status-bar clock, so a live launcher frame changes at least one time each minute. A full-screen app with no clock (for example, a game on a static menu) can show byte-identical frames. When such a device gets no input, the service reports the capture as stale. It does not report the device as not live, because captures continue to complete and the service sent no input.
- **Stub mode / no device**: A session with no device serial reports liveness as "unknown", not "not live". No new failure occurs in stub mode.
- **Capture loop stopped on purpose**: Sometimes no capture loop runs for the session (for example, the session never started one). Then the service does not use the frame data as a fault signal. The health call uses one bounded direct capture instead.
- **Capture loop restarted**: When the capture loop restarts for a session, the "unchanged since" and "last input" data start again. They start from the first frame of the new loop.
- **Input that has no visible effect**: A tap on an inert area does not change the screen. The "no screen change after input" rule counts the stale limit (minutes) from the first input after the last frame change. Thus one inert tap cannot cause a fault, also on a screen that was static for a long time before the tap. Also, the reason `no_change_after_input` does not hold queue firings. The next firings send inputs that can change the screen, and the state then clears. Thus a static full-screen app and an inert tap cannot stop a queue.
- **Transport down**: When ADB reports a state that is not `device`, `adb.ok: false` stays as it is now. The service reports liveness as not live with the reason "transport not ready".
- **Several sessions on one device**: Each session has its own liveness data, from its own capture loop and its own inputs.
- **Recovery**: When a new, different frame arrives or an input completes in time, the "not live" state for that reason clears. The service computes liveness from the current data at each call. It does not latch the state.
- **Idle pause with no input**: After the HOME key at the start of an idle pause, the queue sends no input. A device that wedges in this period and whose captures still complete stays "live" until a rule applies. See the detection limit in the Assumptions.
- **Hung ADB call in a sequence step**: Out of scope for this feature. The current step and sequence watchdogs handle it.
- **Time-of-day firing held past midnight**: A time-of-day timer is due only from its time of day until midnight. When the queue holds it until after midnight, the firing of that day is lost. The daily retry register does not keep it, because a hold does not arm a retry. The timer is due again at its time of day on the new day. This is a known limit (see the Assumptions).

## Requirements *(mandatory)*

### Functional Requirements

**Liveness data**

- **FR-001**: The service MUST keep liveness data for each session with a device. The data is: the time of the last completed capture, and the time at which the captured frame bytes last changed. It also has the time of the first input after that change, and the time and outcome of the last input that the service sent.
- **FR-002**: The service MUST compute a liveness state for each session from this data: `live`, `not_live`, or `unknown`. The state `unknown` means no device, or no data and no possible probe. The service MUST give a reason for `not_live`: `capture_stalled`, `input_timeout`, `no_change_after_input`, or `transport_not_ready`.
- **FR-003**: The service MUST report `capture_stalled` when the capture loop runs for the session and no capture has completed for longer than the capture-stall limit.
- **FR-004**: The service MUST report `input_timeout` when the most recent input to the session did not complete in the input time limit. A later input that completes in time, or a frame change, MUST clear this reason. When the caller cancels an input before the input time limit, the service MUST NOT report `input_timeout` for it.
- **FR-005**: The service MUST report `no_change_after_input` only while the capture loop runs. The frame bytes must not change for longer than the stale limit. Also, the first input after the last frame change must be older than the stale limit.
- **FR-006**: The service MUST report the capture as `stale` while the capture loop runs and one of two conditions is true. The frame bytes did not change for longer than the stale limit, or the last completed capture is older than the capture-stall limit. The `stale` flag is separate from the liveness state. A stale capture alone MUST NOT make the device `not_live`.

**Session health**

- **FR-007**: `GET /api/sessions/{id}/health` MUST add a `liveness` block to its response: `state`, `reason` (null when not `not_live`), `frameAgeMs`, `unchangedMs`, `stale`, `lastInputAt`, and `lastInputOutcome`. The current fields (`id`, `mode`, `deviceSerial`, `adb`) MUST NOT change.
- **FR-008**: The session can have a device, but no capture loop, or a loop with no completed capture yet. In this case, the health call MUST do one direct capture with a time limit. It MUST use the result of this capture for the liveness state.
- **FR-009**: The health call MUST return in bounded time. The transport check and the direct capture MUST each have a time limit.

**Screenshots and snapshots**

- **FR-010**: `GET /api/emulator/screenshot` MUST add the response headers `X-Capture-Age-Ms` (frame age), `X-Capture-Unchanged-Ms` (time for which the frame has not changed), and `X-Capture-Stale` (`true`/`false`). `GET /api/sessions/{id}/snapshot` MUST add the same headers when the session has capture data. The service MUST expose these headers to browser clients in the same way as `X-Capture-Id`.
- **FR-011**: `GET /api/emulator/screenshot` and `GET /api/sessions/{id}/snapshot` MUST apply a time limit to the direct capture. When the limit is reached, they MUST return `504` with error code `capture_timeout`, not wait until the client stops.

**Inputs**

- **FR-012**: `POST /api/sessions/{id}/inputs` MUST apply a time limit to each input command that it sends to the device. When the limit is reached, the service MUST stop that command and mark the action `dispatched: false` with a timeout reason. It MUST NOT send the actions after it. It MUST return `504` with error code `device_timeout` and the `results` array.
- **FR-013**: When the session's liveness is `not_live` after dispatch, `POST /api/sessions/{id}/inputs` MUST NOT report `dispatched: true` for the actions. It MUST report `dispatched: false` with a reason that tells the device is not live. It MUST return `503` with error code `device_not_live`, the liveness reason, and the `results` array. The service MUST still send the inputs to the device. The 504 rule of FR-012 has precedence over this rule. In a 504 response, the actions before the timed-out action keep their `dispatched` value. The timed-out action is `dispatched: false`, and the service does not send the actions after it.
- **FR-014**: Each input that the service sends (the inputs endpoint and the input dispatch of sequences) MUST update the session's last-input data (FR-001). The not-live rule of FR-013 MUST NOT change the step results of sequences; the queue gate (FR-016) handles sequences.

**Queues**

- **FR-015**: While the queue runs, the queue health in `GET /api/queues/{id}` MUST include a device liveness block for the queue's session. The block has the state, the reason, and the time at which the queue first observed the device as not live. It also has the number of held firings in the current fault episode (`gatedFirings`). A fault episode starts when the queue first observes the state `not_live`. It ends when the queue observes `live` or `unknown`.
- **FR-016**: Before a queue fires a sequence, it MUST check the liveness of its session. The queue MUST do this check one time for each due firing. When the state is `not_live` with the reason `capture_stalled`, `input_timeout` or `transport_not_ready`, the queue MUST hold the firing. The reason `no_change_after_input` MUST NOT hold a firing. A held firing obeys these rules:
  - The queue MUST NOT run the sequence. It MUST NOT run the EveryStep and BeforeEachRun guard sequences of that firing.
  - The firing MUST stay due. A timer firing keeps its original due time. Once-per-run entries and next-cycle-start entries stay queued.
  - A held at-queue-start entry, and the at-queue-start entries after it, MUST move to the next-cycle-start register. Thus the run does not end, and these entries run after a recovery.
  - After the first hold in one pass of the run loop, the queue MUST hold all other firings that are due in that pass. It does not evaluate the device again for them. Each of these held firings obeys the same rules. Thus each due sequence gets its log entry in the episode.
  - A held firing MUST NOT use up a daily retry attempt. It keeps its attempt number.
  - The queue MUST wait the queue check interval and then check again. When the device is live again, the held firings run. Thus self-reschedule chains continue at their original cadence.
  - In one fault episode, the first held firing of each sequence MUST write one failed execution log entry with the reason `device_not_live: <reason>`. It MUST also add one failure to the sequence statistics of that sequence.
  - Later held firings of the same sequence in the same episode MUST NOT write a log entry or a statistics failure. The queue health counts them in `gatedFirings`.
- **FR-017**: A queue that runs MUST check its device liveness at an interval, also when no firing is due. The device can stay `not_live` for longer than the queue grace period. Then the queue MUST record one failed execution log entry with no sequence for the fault episode. It MUST also record one failed cycle, so that the consecutive-failure count increases by one and the failure policy acts. This rule applies to each `not_live` reason. It also applies when held firings wrote entries in the episode, and to a queue that does not cycle. The queue MUST NOT record another one until the device becomes live and then not live again.
- **FR-018**: A queue MUST NOT stop, reboot the device, or recover the device by itself because of this fault. The failure policy that the operator configured is not an action "by itself". The fault cycle of FR-017 can trip this policy. Then the policy can notify, pause or stop the queue (for example `notifyAndStop`), as for each other failed cycle. The new code never stops the queue directly.

**Configuration and documentation**

- **FR-019**: The operator MUST be able to configure these values: the stale limit, the capture-stall limit, the input time limit, and the capture time limit. The operator MUST also be able to configure the transport check time limit (`TransportCheckTimeoutMs`), the queue grace period, and the queue check interval (`QueueCheckIntervalMs`). The defaults are in the Assumptions section.
- **FR-020**: The OpenAPI description MUST document the new health fields and the new screenshot headers. It MUST also document the new input failure reasons and status codes, and the new queue health fields.

### Key Entities

- **Device liveness record (per session)**: the last completed capture time, the last frame change time, and the first input time after that change. It also has the last input time, the last input outcome (completed / timed out / failed / cancelled), and if a capture loop runs.
- **Liveness report**: the computed state (`live` / `not_live` / `unknown`), the reason, the frame age, the unchanged duration, the stale flag, and the last input data. It is part of the session health and the queue health.
- **Device-not-live execution record**: a failed execution log entry for a queue that tells the device was not live, with the reason. A held firing writes it for its sequence (at most one for each sequence and episode). The queue watch writes one with no sequence for each episode.
- **Fault episode (per queue run)**: the period from the first `not_live` observation to the next `live` or `unknown` observation. It has the start time, the reason, and the number of held firings. It also has the sequences that already have a log entry, and a mark for the recorded fault cycle.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A device that renders no new frame and applies no input gets the "not live" state in the session health. When captures stop, this occurs within the capture-stall limit. When captures continue, it occurs within the stale limit plus one capture interval after the first input that the device does not apply. With the defaults, this is 5 minutes plus a few seconds.
- **SC-002**: With the fault in SC-001, a screenshot response has the stale flag set within the same period.
- **SC-003**: An action of `POST /api/sessions/{id}/inputs` can get no answer from the device. Then the request returns within the input time limit plus 2 seconds (12 seconds with the defaults). This time starts at the first action that does not answer. The request reports a failure. The actions before that action add only their usual time.
- **SC-004**: A screenshot or snapshot request never waits longer than the capture time limit plus 2 seconds when the device does not return a frame.
- **SC-005**: A queue bound to a wedged device records at least one failed execution with a device-not-live reason. When captures stop, this occurs within the capture-stall limit plus the queue grace period. When captures continue, it occurs within the stale limit plus the queue grace period after the first input that the device does not apply. The detection limit in the Assumptions applies.
- **SC-006**: In the automated tests, the service reports a static screen with no input as live (0 false faults). It reports frames that change as live and not stale.
- **SC-007**: All current health, screenshot, inputs, and queue tests continue to pass. The current response fields do not change.
- **SC-008**: A queue whose device stays not live for N queue check intervals writes a bounded number of execution log entries. The number is at most one entry for each sequence plus one watch entry, for all values of N.

## Assumptions

- Default limits: the stale limit is 5 minutes, and the capture-stall limit is 60 seconds. The input time limit is 10 seconds for each action, the same as the step time limit of `POST /api/steps/execute`. The capture time limit is 10 seconds. The transport check time limit (`TransportCheckTimeoutMs`) is 5 seconds. The queue grace period is 2 minutes. The queue check interval (`QueueCheckIntervalMs`) is 30 seconds.
- "The frame did not change" means the captured PNG bytes are byte-identical. The issue shows that a wedged device returns byte-identical frames. A live device with a visible status bar clock changes its frame at least one time each minute.
- `dispatched: true` means that the device transport accepted the input command in the time limit. It also means that the service does not know that the device is not live. The service cannot prove for each input that the screen changed.
- Detection limit: after the HOME key at the start of an idle pause, the queue sends no input. A device that wedges in this period, and whose captures still complete, gives no fault signal. The service detects it only when the captures stop. Or it detects it after the next firing sends input and the stale limit passes.
- Known limit: a time-of-day firing that the queue holds past midnight is lost for that day. This feature does not change the daily retry register to keep it.
- `GET /api/adb/devices` stays a transport list. It does not get liveness data. The session health is the liveness probe.
- The service only detects and reports the fault. Automatic recovery (for example, an emulator reboot) is out of scope. The root cause of the freeze and issue #217 (B-018) are out of scope.
- The web UI does not need to show the new data in this feature. It must continue to work with the changed responses.

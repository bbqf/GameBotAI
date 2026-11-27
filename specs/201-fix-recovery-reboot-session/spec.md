# Feature Specification: Keep the queue Running and bind a new session after a recovery reboot (B-047)

**Feature Branch**: `201-fix-recovery-reboot-session`  
**Created**: 2026-11-27  
**Status**: Implemented  
**Input**: GitHub issue #416, "B-047: Recovery reboot (deviceRecovery reboot-instance) leaves the queue Stopped with emulator connection lost mid-run". Closes #416. Labels: bug, P1.

## Background

A queue with `deviceRecovery.action = reboot-instance` reboots its LDPlayer instance when the device stays not live for longer than `afterMs` (feature 121, feature 199). The `deviceRecovery` description says: "After the last failed attempt, the service sends one 'recovery failed' alert. The queue stays Running."

On 2026-11-27 the queue `PNS.Production (Monday)` (id `6d6c3e513d0d46f4b3f4661a8be10771`, `emulatorSerial` `emulator-5554`, `emulatorInstanceName` `Monday`, `deviceRecovery = { action: reboot-instance, afterMs: 300000, maxAttempts: 2, cooldownMs: 180000 }`, `pauseWhenIdle: true`, `resumeOnServiceStart: false`) did a recovery reboot after a `device_not_live: no_change_after_input` episode. The reboot occurred (a new LDPlayer process started at 02:13:39 UTC). But:

- The sequence `PNS.EnsureEmulatorReady` failed at 02:11:47 UTC on step `pace-boot` with "Command '...' could not be executed: Session not found".
- The queue run ended with "Queue 'PNS.Production (Monday)' failed: emulator connection lost mid-run ('emulator-5554')."
- `GET /api/queues/{id}` gave `status: Stopped`, with no `health`, no `deviceRecoveryState` and no `stoppedReason`.
- `GET /api/sessions/running` had no session for `emulator-5554`, also after `adb devices` listed `emulator-5554` as `device` again.
- The queue stayed `Stopped` for more than 4 hours. None of its 35 entries ran in that time.

Analysis of the code (read only):

- The liveness watch of the run starts a recovery attempt in the background (`QueueLivenessWatch`, `QueueDeviceRecoveryRunner`). The attempt reboots the instance, waits until the device probe answers, rebinds the session, and waits for the state `live`.
- The rebind (`QueueExecutionService.RebindSessionAsync`) first stops the old session, then binds a new one. When the device is not listed by ADB at that time (the reboot is in progress), the bind throws. The attempt then fails, but the old session is already removed. `handle.SessionId` keeps the ID of a session that does not exist.
- The run loop does not hold its firings for the reason `no_change_after_input` (it is not a hard reason). So the loop continues to run sequences while the reboot occurs. A step of such a sequence fails with "Session not found".
- Before the next firing, the run loop (`EnsureSessionBound`) sees that the session is gone and tries one bind. The device is not listed during the reboot, so the bind throws. The loop changes this to `QueueConnectionLostException`, and the run ends with the reason `Failure` ("emulator connection lost mid-run"). The end of the run sets the status `Stopped` and releases the device claim. No later step binds a session again.
- The liveness watch does nothing when the run has no session (`CheckOnceAsync` returns when the session is not there). So a recovery attempt that removed the session cannot start a second attempt, and it cannot send the "recovery failed" alert.
- The health of the queue (`health.deviceLiveness`, which has `recoveryState`) is only shown while the queue runs, and only when the run has a session. Thus a stopped queue shows no reason.

## Clarifications

### Session 2026-11-27

The pipeline runs with no manual review. Each answer is the most reasonable option, chosen from the issue, the spec, and the code.

- Q: Which queues get the new behaviour (wait for the device and bind again, no stop) when the session is lost and the device cannot be bound? → A: Only a queue whose stored `deviceRecovery.action` is `reboot-instance`. A queue with `action: none` keeps the current behaviour (the run ends with "emulator connection lost mid-run"). Rationale: the issue is about a recovery reboot; the description of `deviceRecovery` gives the promise "the queue stays Running" only for that action; a change for all queues is scope that the issue does not ask for.
- Q: When the attempts are used up (`maxAttempts` reached) and the session is still not bound, does the queue stop with a new `stoppedReason`, or does it stay Running? → A: It stays `Running`. It sends the one "recovery failed" alert, keeps its held and booked firings, and continues to try a bind at each liveness check. `health.deviceLiveness` shows `state: not_live`, a reason, and `recoveryState: exhausted`. No new `stoppedReason` field is added. Rationale: the `deviceRecovery` description says "The queue stays Running"; a Running queue with `health` and `recoveryState` tells the operator why no firing runs, which meets the expected result of the issue with no change of the API schema.
- Q: Which not-live reason does the queue show while it has no bound session? → A: The current hard reason `transport_not_ready`. Rationale: the device transport is not ready while it reboots; the reason is hard, so the run loop holds the firings (feature 106 rule) with no new reason value in the API.
- Q: Does a sequence step that fails with "Session not found" during the reboot change? → A: No. The step and its sequence keep the status `failure`. The fix makes sure that the run loop starts no new firing while no session is bound, and that the run does not end. Rationale: a step that loses its device did fail; the non-goals keep sequence failures as they are.
- Q: How often does the queue try to bind a session when none is bound? → A: At each liveness check (`QueueCheckIntervalMs`) and before each firing that the run loop wants to start. A bind needs that ADB lists the device. The queue does not try a bind while a recovery attempt runs, because the attempt does its own rebind. Rationale: this uses the current check interval and adds no new configuration value.
- Q: What does a firing that the queue holds because no session is bound write to the execution log and the statistics? → A: The same as a firing that the liveness gate holds (feature 106): the first held firing of each sequence in the episode writes one failed sequence entry with the message `device_not_live: transport_not_ready` and one failure in the statistics. The next held firings of that sequence write nothing. Rationale: the operator already knows this record from feature 106, and one entry for each check interval would fill the log.
- Q (analyze U1): Does the queue try a bind while a recovery attempt runs? → A: No. The recovery attempt binds the session itself after the device answers. Rationale: a bind during the reboot can bind on the device before the attempt waits for it, and the run loop and the attempt must not bind two sessions.
- Q (analyze F2): When the operator sets `deviceRecovery` to `none` while the queue waits, which part ends the run? → A: The run loop, at its next bind try that fails. The watch only writes the new value to the run. Rationale: the watch never stops the run (feature 106 rule).
- Q: How does the service log a bind that fails while the queue waits for its device? → A: One warning for the first failed bind of the episode, and debug entries for the next failed binds. One information entry when the bind succeeds again. Rationale: a bind is tried at each check interval; a warning each time would fill the service log during a long reboot.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The queue binds a new session after a recovery reboot and continues (Priority: P1)

An operator has a queue with `deviceRecovery.action = reboot-instance`. The device stays not live, and the queue reboots the instance. During the reboot the device session goes away. When the device is available again, the queue binds a new session on the same serial and runs its held and booked firings. The queue status stays `Running` all the time.

**Why this priority**: This is the defect in the issue. Today one recovery reboot stops all automation of the account until an operator starts the queue again, and the start discards the bookings that did not run yet.

**Independent Test**: In a test, run a queue with `reboot-instance`. Make the recovery reboot remove the session, and make the device not listed for some time, then listed again. The queue status stays `Running`, a new session for the serial exists after the device is listed, and the next due firing runs on that session.

**Acceptance Scenarios**:

1. **Given** a running queue with `reboot-instance` whose recovery attempt stopped the old session, **When** the bind of a new session fails because ADB does not list the device, **Then** the run does not end, and the queue status stays `Running`.
2. **Given** the same queue, **When** the run loop wants to start a firing while no session is bound, **Then** the loop holds the firing (it does not run it and does not drop it), and it does not end the run with "emulator connection lost mid-run".
3. **Given** the same queue, **When** ADB lists the device again, **Then** the queue binds a new session on the same serial in one liveness check interval or before the next firing, and `GET /api/sessions/running` shows that session.
4. **Given** the new session and a device that is live, **When** the next firing is due, **Then** the firing runs, and the held firings and the bookings (reschedule-self) that did not run before the reboot run as normal.
5. **Given** the recovery attempt itself binds the new session and the device becomes live, **When** the attempt ends, **Then** the attempt counts as a success, as today.

---

### User Story 2 - The operator sees why the queue does not run, and gets the alert (Priority: P2)

An operator opens the queue while the queue waits for its device after a recovery reboot. The queue shows `status: Running` and a `health.deviceLiveness` with `state: not_live`, a reason, and a `recoveryState`. When all recovery attempts failed and the queue still has no live session, the operator gets the one "recovery failed" alert.

**Why this priority**: The issue asks that the queue tells the operator why it stopped. Today it shows `Stopped` with no reason. This story makes the failure visible; story 1 removes most of the cases.

**Independent Test**: In a test, keep the device not listed after a recovery reboot. Read `GET /api/queues/{id}`: `status` is `Running`, and `health.deviceLiveness` has `state: not_live` and `reason: transport_not_ready`. Let the attempts reach `maxAttempts`: one "recovery failed" alert is sent, and `recoveryState` is `exhausted`.

**Acceptance Scenarios**:

1. **Given** a running queue with `reboot-instance` that has no bound session, **When** the operator reads `GET /api/queues/{id}`, **Then** the response has `status: Running` and `health.deviceLiveness` with `state: not_live`, `reason: transport_not_ready`, and a `recoveryState` (`running`, `idle` or `exhausted`).
2. **Given** the same queue and a fault episode that is older than `afterMs` plus the cooldown, **When** the liveness check runs, **Then** the watch starts the next recovery attempt (up to `maxAttempts`), also when no session is bound.
3. **Given** the last allowed attempt fails and no live session is bound, **When** the attempt ends, **Then** the service sends exactly one "recovery failed" alert for the episode, and the queue stays `Running` with `recoveryState: exhausted`.
4. **Given** the queue is exhausted, **When** ADB lists the device again later, **Then** the queue binds a new session and continues (story 1, scenario 3), and the "device live again" message is sent as today.

---

### Edge Cases

- The bind fails for another cause (for example the session capacity is reached). The queue with `reboot-instance` stays `Running` and tries again at the next check. The log has a warning with the cause.
- The operator stops or deletes the queue while it waits for its device. The stop works as today: the run ends with the stop reason, the wait ends, and no session is left.
- A recovery attempt stops the old session while a step runs. The step fails with "Session not found", and its sequence fails (no change). The run continues.
- The device comes back while a recovery attempt runs. Only one new session is bound for the queue. The run loop and the watch use the session of the run handle, so they do not bind two sessions.
- A queue with `deviceRecovery.action = none` loses its session and the device cannot be bound. No change: the run ends with "emulator connection lost mid-run".
- The operator changes `deviceRecovery` to `none` while the queue waits for its device. The run reads the stored settings at each check (feature 199), The watch writes the new value to the run at its next check. At the next bind try of the run loop that fails, the run ends with "emulator connection lost mid-run", as for a queue with `none`.
- The service shuts down while the queue waits. The run ends as at each shutdown, and the resume pass of the next start works as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a running queue with stored `deviceRecovery.action = reboot-instance` has no bound session and the bind of a new session fails, the run MUST NOT end. The queue status MUST stay `Running`, and the device claim of the queue MUST stay with the queue.
- **FR-002**: While such a queue has no bound session, the run loop MUST hold each firing that it wants to start. A held firing MUST stay due, as a firing that the liveness gate holds (feature 106). The loop MUST wait `QueueCheckIntervalMs` between two tries.
- **FR-002a**: The first firing of each sequence that the queue holds because no session is bound MUST write one failed sequence entry with the message `device_not_live: transport_not_ready` and one failure in the statistics of the sequence. The next held firings of that sequence in the same episode MUST write nothing (feature 106 rule).
- **FR-003**: While such a queue has no bound session, the queue MUST try to bind a new session on its serial at each liveness check and before each firing that it wants to start. While a recovery attempt runs, the queue MUST NOT try this bind, because the attempt binds the session itself. When the bind succeeds, the queue MUST use the new session for the watch, the gate and the run loop.
- **FR-004**: While such a queue has no bound session, the fault episode of the run MUST record the state `not_live` with the reason `transport_not_ready`. The liveness watch MUST continue its checks, its alerts, and its recovery attempts (`afterMs`, `cooldownMs`, `maxAttempts`), also when no session is bound.
- **FR-005**: A recovery attempt whose rebind fails MUST count as a failed attempt, as today. When the last allowed attempt fails, the service MUST send exactly one "recovery failed" alert for the episode, also when no session is bound.
- **FR-006**: `GET /api/queues/{id}` MUST show `health.deviceLiveness` (with `state`, `reason` and `recoveryState`) for such a running queue, also when no session is bound.
- **FR-007**: A sequence step that fails with "Session not found" because the recovery removed the session MUST keep the status `failure`. This failure MUST NOT end the run of a queue with `reboot-instance`.
- **FR-008**: A queue with stored `deviceRecovery.action = none` (or with no `deviceRecovery`) MUST keep the current behaviour: when the session is lost and the device cannot be bound, the run ends with "emulator connection lost mid-run ('<serial>')".
- **FR-009**: The fix MUST NOT change the `deviceRecovery` settings schema, the not-live detection rules, the manual start (`POST /api/queues/{id}/start`), or the names and meanings of the current API fields.
- **FR-010**: An operator stop, a delete, and a service shutdown MUST end a queue that waits for its device in the same way as a queue that runs firings.
- **FR-010a**: The service log MUST have one warning for the first failed bind of an episode, debug entries for the next failed binds, and one information entry when a bind succeeds again.
- **FR-011**: The queue MUST NOT bind more than one session for itself at a time. When the run binds a new session, it MUST stop the capture of the old session ID if one exists.

### Key Entities

- **Run handle of a queue**: The state of one queue run. It has the session ID of the run (`SessionId`), the fault episode (`Liveness`), the held firings, and the bookings. The run loop, the liveness gate, the watch and the recovery runner read the session ID from it.
- **Fault episode**: The not-live state of the run (feature 106, feature 121). It has the state, the reason, `notLiveSince`, the held firings, the alert flags, and the recovery attempt count. The `recoveryState` (`idle`, `running`, `exhausted`) is calculated from it.
- **Device recovery settings**: `deviceRecovery` of the stored queue (`action`, `afterMs`, `maxAttempts`, `cooldownMs`). The run reads them again at each check (feature 199).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of the test runs where a recovery reboot removes the session and the device is listed again, the queue status stays `Running` and the queue runs its next due firing on a new session.
- **SC-002**: A queue with `reboot-instance` binds a new session in at most one liveness check interval after ADB lists the device again.
- **SC-003**: No queue run with `reboot-instance` ends with "emulator connection lost mid-run" because of a recovery reboot.
- **SC-004**: While such a queue waits for its device, 100% of reads of `GET /api/queues/{id}` show `status: Running` and a `health.deviceLiveness` with a reason and a `recoveryState`.
- **SC-005**: When all attempts fail, the operator gets exactly one "recovery failed" alert for the episode.
- **SC-006**: The tests of queues with `deviceRecovery.action = none` show the same end of the run as before the fix.

## Assumptions

- "The device is available again" means that ADB lists the serial of the queue, so that the session manager can bind a session on it.
- The current hard reason `transport_not_ready` is a correct description of a device that ADB does not list during a reboot. No new reason value is added.
- The bookings and held firings of the run stay in the run handle while the queue waits. They are not lost, because the run does not end.
- The web UI shows the status and the health that the service gives. It needs no change, so the feature does not change the web UI and has no phone support statement.

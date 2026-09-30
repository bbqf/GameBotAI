# Feature Specification: Device Not-Live Alert, Optional Recovery, and Capture Pile-Up Guard

**Feature Branch**: `121-device-not-live-alert-recovery`  
**Created**: 2026-09-30  
**Status**: Implemented  
**Input**: GitHub issue #261 (FR-014): "alert on a not-live device, optionally reboot the instance, and do not pile up captures". Closes #261.

## Background

Since PR #231 (issue #220), `GET /api/queues/{id}` reports `health.deviceLiveness`. The state `not_live` has the reasons `capture_stalled`, `input_timeout`, `transport_not_ready`, and `no_change_after_input`. The field `gatedFirings` counts the held firings. The queue holds every due firing while the device is not live. The queue never stops or repairs the device by itself. This detection works and this feature does not change it.

On 2026-09-30, three of four LDPlayer 9 instances were `not_live` for a long time.

- `gatedFirings` was 144, 120, and 100.
- `health.lastNotificationAt` was `null`.
- `failurePolicyConfigured` was `false`.
- No notification was sent, although a Telegram target was enabled.

About 960 `screencap` processes (state `D`) piled up on each blind device. A healthy device had 1.

The operator repaired each device by hand:

1. Stop the queue.
2. Run `ldconsole.exe reboot --name <instance>`.
3. Wait 75 seconds.
4. Start the queue.

## Clarifications

### Session 2026-09-30

- Q: Is the alert time a queue field or a service setting? → A: A service setting (`DeviceLiveness` alert-after value), default 5 minutes (300000 ms). Rationale: the issue says "configured time". It lists only `deviceRecovery` as a new queue field. The test in the issue sets the alert time to 2 minutes through configuration.
- Q: What are the defaults and limits of `deviceRecovery` members? → A: `action` default `none`. `afterMs` default 300000, minimum 60000. `maxAttempts` default 2, range 1 to 5. `cooldownMs` default 180000, minimum 0. Rationale: safe values that match the 5-minute alert time and the 3-minute stagger.
- Q: What does `cooldownMs` mean? → A: The minimum wait between the end of one recovery attempt of a queue and the start of the next attempt of the same queue. The service-wide stagger time is separate (FR-012).
- Q: What happens to the queue after `maxAttempts` failed attempts? → A: The queue stays Running and keeps holding firings (no change to the gate). The service starts no more attempts in that episode. The service sends one "recovery failed" alert. If the device becomes live later, the service sends the normal "device live again" message.
- Q: How does the service count alerts when two queues share one emulator instance? → A: Each queue has its own episode and its own alert. The recovery is for the instance. The service starts at most one reboot for one instance per episode window. The second queue waits for that recovery.
- Q: Is the capture no-pile-up rule only for not-live devices? → A: No. It applies to every device at all times: at most one unfinished capture per device. After `CaptureStallLimitMs`, the service starts no new capture for that device until the first capture ends or the device is repaired. A session rebind after recovery counts as repaired.
- Q (analyze A1): FR-013 says one unfinished capture, SC-004 says 2 or less. Which is right? → A: Both. The limit of 2 `screencap` processes on the device counts two captures. One is the unfinished capture that timed out. The other is at most one capture in progress at the moment of the check. The service starts no new capture while one is unfinished.
- Q (analyze A2): What is the check interval and the minimum alert time? → A: The liveness watch checks at most once a minute. So the alert arrives within the alert time plus 1 minute. The minimum of the alert time setting is 1 second, to allow short test values.
- Q (analyze I1): Are the new read-only health members part of the requirements? → A: Yes. `health.deviceLiveness` gains the read-only members `alertSent`, `recoveryAttempts`, and `recoveryState`. They do not change the detection rules (FR-017).
- Q (analyze G3): Where does the `ldconsole.exe` path come from? → A: From the existing emulator control setting that the service already uses to start and stop LDPlayer instances. No new path setting is added. If no path is set, the attempt fails and counts as failed (see Edge Cases).
- Q (analyze U1): What are the values of `recoveryState`? → A: `idle`, `running`, `exhausted` (see FR-017).
- Q (analyze D1): Must the work update `specs\STATUS.md` and the earlier spec 106? → A: Yes. The constitution (Principle V) needs it. See FR-018.
- Q (analyze U2): How is SC-003 measured? → A: The gap between the two start times is the stagger time or more. See SC-003.
- Q: Does the "live again" message use the same channel rules? → A: Yes. Same targets, no dependence on `notificationLevel` or failure policy, and it also sets `health.lastNotificationAt`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Alert the operator about a not-live device (Priority: P1)

The operator must learn about a long not-live device without a look at the screen. When the device of a queue stays `not_live` for longer than a configured time, the service sends one notification to every enabled notification target. When the device is live again, the service sends one more notification.

**Why this priority**: The operator learns of the fault only by chance today. An alert gives the most value at the lowest risk, and it works with recovery off.

**Independent Test**: Block the capture of a device. Start its queue with `deviceRecovery` off and an alert time of 2 minutes. After 2 minutes, one notification arrives and `health.lastNotificationAt` is set. No second notification arrives in the next 10 minutes.

**Acceptance Scenarios**:

1. **Given** a Running queue with no failure policy and a not-live device, **When** the device stays not live for longer than the alert time, **Then** the service sends exactly one "device not live" notification to every enabled target and sets `health.lastNotificationAt`.
2. **Given** an alert was sent for a not-live episode, **When** the device stays not live for more time, **Then** the service sends no other message for that episode.
3. **Given** an alert was sent for a not-live episode, **When** the device becomes live again, **Then** the service sends one "device live again" notification.
4. **Given** a device that is not live for less than the alert time and then becomes live, **When** the device is live again, **Then** the service sends no notification.
5. **Given** a queue with `notificationLevel` set to any value, **When** an alert is due, **Then** the alert is sent regardless of that value.

---

### User Story 2 - Optional automatic recovery by instance reboot (Priority: P2)

The operator can turn on automatic recovery for a queue. When the device stays not live for `afterMs`, the service reboots the emulator instance named in `emulatorInstanceName`, waits until the device is ready, binds a new session, and lets the queue run the held firings.

**Why this priority**: This removes the manual repair. It has more risk than the alert, so it is off by default and comes second.

**Independent Test**: Repeat the test of story 1 with `deviceRecovery.action` set to `reboot-instance` and `afterMs` set to 3 minutes. The instance reboots once, the queue runs its held firings, and a second notification reports that the device is live.

**Acceptance Scenarios**:

1. **Given** a queue with `deviceRecovery.action` = `none` or no `deviceRecovery`, **When** the device is not live for a long time, **Then** the service does not reboot any instance.
2. **Given** a queue with `action` = `reboot-instance` and a not-live device, **When** the device stays not live for `afterMs`, **Then** the service reboots the instance named in `emulatorInstanceName`.
3. **Given** a reboot started, **When** `adb` reports the state `device` and a capture completes, **Then** the service binds a new session to the queue and the queue runs the held firings.
4. **Given** a recovery attempt that does not make the device live, **When** the attempts reach `maxAttempts`, **Then** the service stops the attempts and sends one alert that recovery failed.
5. **Given** a queue with `action` = `reboot-instance` and no `emulatorInstanceName`, **When** the queue is saved, **Then** the service rejects the change with a clear validation error.

---

### User Story 3 - One recovery at a time (Priority: P2)

When many devices fail together, the service must not reboot them together. It starts at most one recovery at a time across all queues. It waits at least a configured time between two recoveries (default 3 minutes).

**Why this priority**: The report of #220 shows that four queues started within one second were followed by a fault on all four devices. A reboot storm can cause more faults.

**Independent Test**: Block the capture of two devices together, with recovery on for both queues. The two recoveries start at least one stagger time apart.

**Acceptance Scenarios**:

1. **Given** two queues that need recovery at the same time, **When** the first recovery runs, **Then** the second recovery waits until the first ends and the stagger time passes.
2. **Given** one recovery ended, **When** another queue needs recovery before the stagger time ends, **Then** that recovery waits for the rest of the stagger time.

---

### User Story 4 - No pile-up of captures on a blind device (Priority: P3)

The service keeps at most one unfinished capture for each device. When a capture does not end within `CaptureStallLimitMs`, the service starts no other capture for that device until the first one ends or the device is repaired.

**Why this priority**: It stops the growth of about 960 stuck `screencap` processes. It is a separate protection and it can be tested alone.

**Independent Test**: Block the capture of a device. Count the `screencap` processes on the device over 10 minutes. The count stays at 2 or less and does not grow.

**Acceptance Scenarios**:

1. **Given** a capture that is not ended, **When** the next capture interval is due, **Then** the service starts no second capture for that device.
2. **Given** a capture that is not ended after `CaptureStallLimitMs`, **When** more capture intervals pass, **Then** the service starts no new capture for that device until the first capture ends or the device is repaired.
3. **Given** a healthy device, **When** captures end on time, **Then** the capture rate is the same as before this feature.

---

### Edge Cases

- The queue is stopped while the device is not live: the service cancels the alert timer and any recovery wait for that queue. It sends no "live again" message if no alert was sent.
- The queue is stopped or deleted while a reboot runs: the service ends the recovery and frees the recovery slot.
- The service restarts during a not-live episode: the episode timer starts again. The service sends at most one alert for each episode it observes. Acceptance: after a restart, a device that stays not live gets a fresh episode timer and exactly one alert. A test MUST cover this.
- Tests MUST cover these recovery cases:
  - (a) The queue is stopped or deleted during the reboot itself: the recovery slot is free after that.
  - (b) A capture of a blocked device ends late: the service allows captures for that device again.
  - (c) Two queues use one emulator instance: after the shared reboot, the second queue binds its own new session and counts its own recovery attempt.
- Two queues use the same emulator instance: the service treats them as one device for the recovery slot and does not reboot the instance twice for one episode.
- The reboot tool is missing or fails to start: the attempt counts as failed and the service logs the cause.
- The device recovers by itself before `afterMs`: the service starts no reboot.
- A capture ends late, after the no-new-capture rule applied: the service allows new captures again.
- A not-live episode that ends and starts again is a new episode with its own alert.
- No notification target is enabled: the service logs that no target was available and does not fail.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When the device of a Running queue stays `not_live` for longer than the alert time (default 5 minutes), the system MUST send one notification to every enabled notification target.
- **FR-002**: When the device is live again after an alert, the system MUST send one more notification to every enabled target.
- **FR-003**: The system MUST send no other notification for a not-live episode, except the failed-recovery alert in FR-009.
- **FR-004**: The alert MUST NOT need a failure policy and MUST NOT depend on `notificationLevel`. The system MUST set `health.lastNotificationAt` when it sends the alert.
- **FR-005**: The alert time MUST be a configurable setting with the default 5 minutes.
- **FR-006**: A queue MUST accept an optional field `deviceRecovery` with the members `action` (`none` or `reboot-instance`), `afterMs`, `maxAttempts`, and `cooldownMs`. The default MUST be off (`none`). A queue without the field MUST behave as before this feature.
- **FR-007**: When `action` is `reboot-instance` and the device stays `not_live` for `afterMs`, the system MUST reboot the instance named in `emulatorInstanceName` with `ldconsole.exe reboot --name <instance>`.
- **FR-008**: After a reboot, the system MUST wait until `adb` reports the state `device` and a capture completes. Then the system MUST bind a new session to the queue and let the queue run the held firings.
- **FR-009**: After `maxAttempts` failed recovery attempts, the system MUST stop the attempts and send one alert that recovery failed.
- **FR-010**: The system MUST reject a `deviceRecovery` with `action` = `reboot-instance` when the queue has no `emulatorInstanceName`, or when `afterMs`, `maxAttempts`, or `cooldownMs` has an invalid value. The error MUST be a validation error (HTTP 400), not a server error.
- **FR-010a**: The defaults and limits are: `afterMs` default 300000 and minimum 60000; `maxAttempts` default 2 and range 1 to 5; `cooldownMs` default 180000 and minimum 0. `cooldownMs` is the minimum wait between two attempts of the same queue.
- **FR-011**: The system MUST start at most one recovery at a time for all queues.
- **FR-012**: The system MUST wait at least a configured stagger time (default 3 minutes) between the start of two recoveries. The stagger time MUST be a configurable setting.
- **FR-013**: The system MUST keep at most one unfinished capture for each device.
- **FR-014**: When a capture does not end within `CaptureStallLimitMs`, the system MUST start no other capture for that device until the first capture ends or the device is repaired.
- **FR-015**: The system MUST NOT change how `health.deviceLiveness` detects or reports the not-live state.
- **FR-016**: The API documentation (OpenAPI) and the queue health description MUST describe the new alert, the `deviceRecovery` field, and the updated statement about device repair.
- **FR-017**: `health.deviceLiveness` MUST also show the read-only members `alertSent` (true or false), `recoveryAttempts` (number of attempts in the episode), and `recoveryState`. `recoveryState` has three values: `idle` (no recovery runs and attempts are not used up), `running` (a recovery waits for the slot or a reboot runs), and `exhausted` (the attempts reached `maxAttempts`). These members MUST NOT change the detection rules of FR-015.
- **FR-018**: The work MUST keep the living documents in step: `docs\architecture.md` (with a refreshed "Last reviewed" date), `specs\STATUS.md` (Status of spec 121 set to Implemented), and the Status line of the earlier liveness spec (106) MUST point to spec 121 as the spec that iterates it.

### Key Entities

- **Not-live episode**: One period in which the device of a queue is `not_live`. It starts when the state turns `not_live` and ends when the state turns live or the queue stops. It has a start time, an alert-sent flag, and a recovery attempt count.
- **Device recovery settings (`deviceRecovery`)**: Optional queue setting with `action`, `afterMs`, `maxAttempts`, and `cooldownMs`.
- **Recovery runner (`QueueDeviceRecoveryRunner` in code)**: The per-queue service component that runs the alert timer and the recovery steps for one queue. Naming note: the settings record keeps the name "device recovery settings" (the `deviceRecovery` field). The runner MUST NOT share the name `QueueDeviceRecovery` with the settings record.
- **Recovery slot**: A service-wide lock that allows one recovery at a time and records the time the last recovery started.
- **Capture in flight**: The one unfinished capture of a device. It blocks the start of a new capture.

## Assumptions

- The notification targets and the send method are the ones that exist from feature 120 (sequence and queue notifications).
- `cooldownMs` is the minimum wait between two recovery attempts of the same queue. The stagger time between different queues is a service setting.
- Only the LDPlayer 9 `ldconsole.exe` reboot is in scope. The path of the tool comes from the existing emulator control setting.
- The alert time and the stagger time are service settings, not queue fields. The alert time has a minimum of 1 second. The liveness watch checks at most once a minute.
- The `ldconsole.exe` path comes from the existing emulator control setting. The feature adds no path setting.
- Existing queues keep their behavior: no recovery and no new capture limit on a healthy device.
- Delivery order: the recovery coordinator interface and the coordinator (the owner of the recovery slot) are defined before the recovery runner. A recovery then never runs without the one-at-a-time slot.
- SC-002 and SC-004 are checked with fakes in automated tests and on a real device by the quickstart steps. The quickstart MUST list two manual checks: the `screencap` process count on the device, and the recovery timing.
- The tasks step makes all new service registrations in ONE task. Parallel tasks then do not conflict on `GameBotServiceSetup.cs`.
- A task that only runs tests (for example the one-at-a-time tests) is verification only. The tasks step marks such tasks as verification-only.
- The spec folder of the earlier liveness feature (issue #220) MUST be named with its exact folder name in the tasks step. Tasks MUST NOT use "find by search" notes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a device that stays not live, the operator receives exactly one alert within the alert time plus 1 minute, and no second message in the next 10 minutes.
- **SC-002**: With recovery on, a blind device is live again and its held firings run without manual action, within the time of one reboot cycle (about 75 seconds) plus `afterMs` plus 2 minutes.
- **SC-003**: For two blind devices with recovery on, the start of the two recoveries is at least the stagger time apart: the gap between the two start times is the stagger time or more. For SC-002, the manual check passes when the held firings run within the stated time after the reboot starts.
- **SC-004**: On a blind device, the count of `screencap` processes stays at 2 or less over 10 minutes. The limit includes the one unfinished capture that timed out.
- **SC-005**: A queue with no `deviceRecovery` shows no change in behavior, and a healthy device shows no change in capture rate.
- **SC-006**: The operator needs no manual repair step for a not-live device when recovery is on and the reboot succeeds.

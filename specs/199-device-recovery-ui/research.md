# Research: Device Recovery Option in the Queue Form

**Feature**: 199-device-recovery-ui | **Date**: 2026-11-24

## R-001: Tell an absent `deviceRecovery` from an explicit `null` on update (FR-005a)

**Result**: `PUT /api/queues/{id}` binds `UpdateQueueRequest` with System.Text.Json. The property `DeviceRecovery` is `QueueDeviceRecoveryDto?`. Both an absent member and `null` give `null`. The endpoint then sets `queue.DeviceRecovery = null` (`QueuesEndpoints.cs:116-127`).

**Decision**: Give `UpdateQueueRequest.DeviceRecovery` a setter that also sets a flag `DeviceRecoverySpecified` (marked `[JsonIgnore]`). System.Text.Json calls a setter only for a member that is in the JSON, also for an explicit `null`. The endpoint keeps `queue.DeviceRecovery` when the flag is false. When the flag is true, it maps the value as now (`null` clears).

**Rationale**: One small change in the request class. No custom converter, no read of the raw body. The rule is clear in a unit test.

**Alternatives considered**:
- `JsonElement?` or a raw body read: more code, and the Swagger schema of the request changes.
- A custom `Optional<T>` converter: a new type for one member.
- Web UI fix only: other clients still lose the settings (clarification Q1 chose both).

## R-002: Apply a saved change to a running queue (FR-012a)

**Result**: `QueueExecutionService.StartQueueAsync` reads the queue one time (`QueueExecutionService.cs:253`). `QueueLivenessWatch` keeps that object in `_queue` and reads `_queue.DeviceRecovery` at each check (`QueueLivenessWatch.cs:176`). `FileQueueRepository.GetAsync` gives a new object for each read. So a save does not reach the run. `QueueDeviceRecoveryRunner.SendRecoveryFailedIfUsedUp` reads `queue.DeviceRecovery.MaxAttempts` from the same old object (`QueueDeviceRecoveryRunner.cs:171`). The settings import (`QueueSettingsImporter.cs:531`) also writes `DeviceRecovery` through the repository.

**Decision**: The watch reads the stored queue at the start of each check, through a delegate `Func<CancellationToken, Task<ExecutionQueue?>>` that `QueueExecutionService` gives (it calls `IQueueRepository.GetAsync`). The watch uses the `DeviceRecovery` and the `EmulatorInstanceName` of the stored queue for the recovery decision and for the attempt. When the read fails or gives null, the watch keeps the last known values and logs one debug line. The episode (`QueueLivenessEpisode`) does not change, so the attempt count stays (clarification Q3).

**Rationale**: The stored file is the one source of the settings. The rule covers each write path: the update endpoint, the settings import, and a future path. The cost is one small file read each minute for each running queue (production: 4 queues).

**Alternatives considered**:
- The update endpoint pushes the new settings into `QueueRunHandle`: fast, but the import path (Domain project) cannot reach the handle. Two write paths need two pushes.
- Restart of the watch on save: it resets the timers of the episode.

**Effects of the rule (spec edge cases)**:
- Turn on during an old episode: `TryBeginRecovery` compares the episode age with the new `afterMs` at the next check, so the attempt starts at once (with the one-at-a-time slot).
- Turn off during an attempt: the attempt that runs ends normally. The next check sees `Reboots == false` and starts no new attempt.
- Lower `maxAttempts` below the count: `TryBeginRecovery` refuses. `DeviceLivenessReader.DeriveRecoveryState` reads the stored settings in the detail endpoint, so it shows `exhausted`. The runner gets the current `MaxAttempts` from the watch for the "recovery failed" alert.

## R-003: Note near Save for a running queue

**Result**: The update endpoint comment says "the saved settings apply at the next start" (`QueuesEndpoints.cs:109-110`). The form note is "Settings apply at the next start. Smart scheduling applies to the next booking." (`QueueForm.tsx:270`).

**Decision**: Change the form note to: "Settings apply at the next start. Smart scheduling applies to the next booking. Device recovery applies within 1 minute." Change the endpoint comment to match.

## R-004: Data for the "Not live" mark (FR-011a)

**Result**:
- The phone view reads `GET /api/queues` for the cards and `GET /api/queues/{id}` for the detail (`PhoneQueuesView.tsx:100`, poll 5 s). The list already adds `operatorPause` for a running queue from the run handle (`QueuesEndpoints.cs:64`). The detail already has `health.deviceLiveness`.
- The Monitoring page reads `GET /api/sessions/monitoring`. Each session has `ownerQueueId` and `ownerQueueName`. The endpoint already reads `IQueueRunRegistry` (`SessionMonitoringEndpoints.cs:35-49`).

**Decision**:
- `QueueResponse` gets `deviceLiveness` (`"live"` or `"not_live"`). Only the list sets it, only for a running queue whose episode has a report. It is absent in other cases (`JsonIgnore` when null, as `operatorPause`).
- `MonitoredSessionDto` gets `ownerQueueDeviceLiveness` with the same values and the same rule, for a session whose owner queue runs.
- Both read `handle.Liveness.Snapshot().State`. They do not evaluate the session again. So the mark can be up to one watch interval (1 minute) old. The edit view reads the detail, which evaluates the session again.
- The phone detail reads `health.deviceLiveness.state` from the detail it reads now.

**Rationale**: No new endpoint, no new request from the web UI. A snapshot read takes a lock and copies 8 values.

**Alternatives considered**: Evaluate the session in the list read (as the detail does): more work on each 5 s poll for each queue, for a mark that a 1-minute age does not hurt.

## R-005: Units and precision in the form (FR-002, edge case)

**Decision**: The form shows `afterMs` and `cooldownMs` in minutes. The display value is `ms / 60000`, rounded to one decimal place. The form keeps the stored milliseconds of the queue. On save, when the operator did not change a field, the form sends the stored milliseconds. When the operator changed it, the form sends `Math.round(minutes * 60000)`. The minute inputs accept steps of 0.1. Maximum attempts is a whole number.

**Form checks** (before the save, the same limits as `QueueDeviceRecoveryValidator`):
- Wait time: 1 minute or more (60000 ms), and the milliseconds fit a 32-bit integer.
- Maximum attempts: a whole number from 1 to 5.
- Time between attempts: 0 minutes or more, and the milliseconds fit a 32-bit integer.
- Option on and an empty emulator instance name: "Device recovery needs an emulator instance name."

## R-006: What the form sends (FR-004 to FR-006)

| Option | Queue had `deviceRecovery` | The form sends |
|---|---|---|
| on | any | `{ action: "reboot-instance", afterMs, maxAttempts, cooldownMs }` |
| off | yes | `{ action: "none", afterMs, maxAttempts, cooldownMs }` |
| off | no (null) | no `deviceRecovery` member (R-001: the service keeps null) |

Create sends the same table with "Queue had" = no.

## R-007: Duplicate (FR-010)

**Result**: `POST /api/queues/{id}/duplicate` already clones `source.DeviceRecovery` and validates it with the new instance name (`QueuesEndpoints.cs:182-192`). The modal sends no recovery member.

**Decision**: No code change. Add a test of the web UI flow: the copy shows the recovery settings in its edit form.

## R-008: Phone support level (Constitution III)

**Result**: `docs/mobile-ui-principles.md` gives Queues the level `reduced`. `PhoneQueuesView` has no queue settings form ("Use a larger screen ..." hints).

**Decision**: The option is `desktop-only`, as the rest of the queue settings. The "Not live" mark is `full` on the phone card and the phone detail. The Monitoring area is `full`, so its mark works at 360 px. The spec changed FR-012 and SC-005 to match.

## R-009: Device state view in the edit view (FR-011)

**Result**: `RunningQueueControls` reads `getQueueHealth` each 2.5 s on the edit page of a running queue (`RunningQueueControls.tsx:36`). `QueueHealthDto` in `queues.ts` has no `deviceLiveness` type yet.

**Decision**: Add the `deviceLiveness` type to `QueueHealthDto`. Add a small component `QueueDeviceState` that `RunningQueueControls` shows with the health that it reads now. No new poll.

Text of the view:
- Live: "Device: live".
- Not live: "Device: not live (<reason>) since <time>". Then "Alert sent" or "No alert yet". Then "Recovery: <state>, attempts <n>" when the queue has recovery on, or "Recovery: off".
- No report yet: no line.

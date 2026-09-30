# Data Model: Device Not-Live Alert, Optional Recovery, and Capture Pile-Up Guard

## 1. Device recovery settings (persisted, queue field `deviceRecovery`)

Domain record `QueueDeviceRecovery` in `GameBot.Domain.Queues`. It is an optional member of `ExecutionQueue`.

| Member | Type | Default | Rule |
|--------|------|---------|------|
| `action` | string | `none` | One of `none`, `reboot-instance`. |
| `afterMs` | int | 300000 | Minimum 60000. |
| `maxAttempts` | int | 2 | Range 1 to 5. |
| `cooldownMs` | int | 180000 | Minimum 0. |

Cross-field rules:

- `action` = `reboot-instance` needs a non-blank `emulatorInstanceName` on the queue.
- A queue with `deviceRecovery` = null behaves as `action` = `none`.
- `action` = `none` ignores the other members, but the validator still checks their ranges.
- On update, an absent `deviceRecovery` member in the request sets the field to null (same rule as `failurePolicy`). The route never keeps an old value for an absent field.
- A queue copy (duplicate route) copies the field.

Storage: one nullable object in the queue JSON file. An old file has no member and reads as null.

## 2. Service settings (persisted in configuration, section `Service:DeviceLiveness`)

New members of `DeviceLivenessOptions`. `Normalized()` applies each minimum.

| Member | Default | Minimum | Use |
|--------|---------|---------|-----|
| `AlertAfterMs` | 300000 | 1000 | The episode age after which the alert is sent. |
| `RecoveryStaggerMs` | 180000 | 0 | The least time between the starts of two reboots. |
| `RebootReadyTimeoutMs` | 180000 | 1000 | The limit to wait for the device after a reboot, and for the `live` state after a rebind. |

## 3. Not-live episode (memory only, extends `QueueLivenessEpisode`)

New fields. The existing fields (`state`, `reason`, `notLiveSince`, `gatedFirings`, `faultCycleRecorded`, `lastReport`) do not change.

| Field | Type | Meaning |
|-------|------|---------|
| `alertSent` | bool | The "device not live" alert is claimed for this episode. |
| `liveAgainPending` | bool | The episode closed with `live` after an alert. The "live again" message is not claimed yet. |
| `recoveryFailedSent` | bool | The "recovery failed" alert is claimed for this episode. |
| `attempts` | int | The count of finished recovery attempts in this episode. |
| `lastAttemptEndedAt` | DateTimeOffset? | The end time of the last attempt. Used for `cooldownMs`. |
| `recoveryRunning` | bool | One recovery task runs for this queue. |

State transitions:

```text
(no episode) --not_live--> Open
Open --age > AlertAfterMs--> Open, alertSent = true        (one alert)
Open --age > afterMs, attempts < maxAttempts, cooldown passed--> Open, recoveryRunning = true
Open --attempt ends not live--> Open, attempts + 1
Open --attempts = maxAttempts, attempt failed--> Open, recoveryFailedSent = true   (one alert)
Open --live--> Closed; liveAgainPending = alertSent; all other fields cleared
Open --unknown or queue stop--> Closed; liveAgainPending = false; all fields cleared
Closed --not_live--> Open (new episode, new alert)
```

`recoveryState` (API only, not stored) comes from these fields: `running` when `recoveryRunning` is true; else `exhausted` when `attempts` reached `maxAttempts` and the state is `not_live`; else `idle`.

Claims (`TryClaimAlert`, `TryClaimLiveAgain`, `TryClaimRecoveryFailed`) return true one time. One lock protects all fields. The queue run handle owns the episode, so a stopped queue drops it.

## 4. Alert work item (memory only, notification channel)

Record `QueueAlert`:

| Field | Type | Meaning |
|-------|------|---------|
| `QueueId` | string | The queue. The worker reads the name when it sends. |
| `Kind` | enum | `NotLive`, `LiveAgain`, `RecoveryFailed`. |
| `Reason` | string? | The liveness reason for `NotLive`. Null for the other kinds. |
| `RaisedAt` | DateTimeOffset | The local time of the claim. |
| `OnCompleted` | callback (time, succeeded, error) | Sets the last notification data of the queue run. |

The work item is never dropped and is not part of `MaxQueuedJobs`.

## 5. Recovery slot (memory only, `DeviceRecoveryCoordinator`)

| Field | Type | Meaning |
|-------|------|---------|
| `slot` | semaphore (1) | One reboot start at a time for the service. |
| `lastStartedAt` | DateTimeOffset? | The start of the last reboot. Used for `RecoveryStaggerMs`. |
| `inFlight` | map instance name -> reboot task | Shares one reboot between queues of one instance. The key ignores case. |

## 6. Capture in flight (memory only, `DeviceCaptureGate`)

One state for each device serial (key ignores case).

| State | Meaning | Next capture |
|-------|---------|--------------|
| `Idle` | No unfinished capture. | Allowed. |
| `InFlight` | One capture runs on the host. | Refused. |
| `Suspect` | The last capture timed out. The device process can still run. | Refused. |

Transitions:

```text
Idle --TryBegin--> InFlight
InFlight --Completed or Failed--> Idle
InFlight --TimedOut--> Suspect
Suspect --device check finds no screencap process--> Idle
Suspect --StartCapture for the serial (repair)--> Idle
```

The gate also keeps `suspectSince` and `lastCheckAt` for the check every `CaptureStallLimitMs`.

## 7. API projections

- `QueueResponse` and `QueueDetailResponse` get `deviceRecovery` (null when not set).
- `QueueHealthResponse.deviceLiveness` gets three read-only members: `alertSent` (bool), `recoveryAttempts` (int), and `recoveryState` (`idle`, `running`, `exhausted`). See [contracts/device-recovery-api.md](contracts/device-recovery-api.md).
- `health.lastNotificationAt`, `lastNotificationSucceeded`, and `lastNotificationError` also show the result of an alert.

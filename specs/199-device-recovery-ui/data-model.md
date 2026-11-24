# Data Model: Device Recovery Option in the Queue Form

**Feature**: 199-device-recovery-ui | **Date**: 2026-11-24

This feature adds no stored field. The file format of a queue does not change.

## Stored (no change)

### `QueueDeviceRecovery` (Domain, feature 121)

| Member | Type | Default | Limits |
|---|---|---|---|
| `Action` | string | `none` | `none` or `reboot-instance` |
| `AfterMs` | int | 300000 | 60000 or more |
| `MaxAttempts` | int | 2 | 1 to 5 |
| `CooldownMs` | int | 180000 | 0 or more |

`reboot-instance` needs a non-blank `EmulatorInstanceName` on the queue.

## Request (changed rule)

### `UpdateQueueRequest`

| Member | Change |
|---|---|
| `DeviceRecovery` | The setter also sets `DeviceRecoverySpecified = true`. |
| `DeviceRecoverySpecified` | NEW, `[JsonIgnore]`, bool. False when the JSON has no `deviceRecovery` member. |

Update rule: `DeviceRecoverySpecified == false` → keep the stored value. `true` with `null` → clear. `true` with an object → map and validate as now.

`CreateQueueRequest` does not change: an absent member means no recovery.

## Responses (new read-only members)

### `QueueResponse` (list item of `GET /api/queues`)

| Member | Type | When present |
|---|---|---|
| `deviceLiveness` | `"live"` or `"not_live"` | The list read only. The queue runs and its episode has a report. Absent in all other cases. |

### `MonitoredSessionDto` (`GET /api/sessions/monitoring`)

| Member | Type | When present |
|---|---|---|
| `ownerQueueDeviceLiveness` | `"live"` or `"not_live"` | The session has an owner queue that runs, and its episode has a report. Else `null`. |

Source of both: `QueueRunHandle.Liveness.Snapshot().State` (no new evaluation of the session).

## Service state (changed source)

### `QueueLivenessWatch`

| Item | Before | After |
|---|---|---|
| Recovery settings | `_queue.DeviceRecovery` from the start of the run | The stored queue, read at the start of each check |
| Instance name for the attempt | `_queue.EmulatorInstanceName` | The stored queue, read at the start of each check |
| Read failure | not applicable | Keep the last known values |

`QueueLivenessEpisode` does not change. The attempt count, the cooldown, and the alert flags stay with the episode.

## Web UI form state

### `QueueFormValue` (new members)

| Member | Type | Meaning |
|---|---|---|
| `deviceRecoveryOn` | boolean | The option. |
| `recoveryAfterMinutes` | number | The wait time before the first reboot, in minutes. |
| `recoveryMaxAttempts` | number | The maximum attempts. |
| `recoveryCooldownMinutes` | number | The time between attempts, in minutes. |
| `storedDeviceRecovery` | `QueueDeviceRecoveryDto \| null` | The stored value when the form opened. It is not shown. The form uses it to send the stored milliseconds for a field that did not change, and to choose what to send (research R-006). |

Defaults for a new queue: off, 5, 2, 3, null.

### `QueueDeviceRecoveryDto` (web UI type, NEW in `services/queues.ts`)

`{ action: 'none' | 'reboot-instance'; afterMs: number; maxAttempts: number; cooldownMs: number }`

### `QueueDeviceLivenessDto` (web UI type, NEW in `services/queues.ts`)

`{ state: 'live' | 'not_live'; reason?: string | null; notLiveSince?: string | null; gatedFirings?: number; alertSent?: boolean; recoveryAttempts?: number; recoveryState?: 'idle' | 'running' | 'exhausted' }`

`QueueHealthDto` gets `deviceLiveness?: QueueDeviceLivenessDto | null`. `QueueDto` gets `deviceRecovery?: QueueDeviceRecoveryDto | null` and `deviceLiveness?: 'live' | 'not_live'`. `QueueUpdate` and `QueueCreate` get `deviceRecovery?: QueueDeviceRecoveryDto | null`. `MonitoredSessionDto` gets `ownerQueueDeviceLiveness?: 'live' | 'not_live' | null`.

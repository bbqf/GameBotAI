# Data Model: Emulator Monitoring

All records are in memory. No file or database changes.

## MonitoredSession (response item, built on each request)

| Field | Type | Source / rule |
|-------|------|---------------|
| `id` | string | `EmulatorSession.Id` |
| `name` | string | `gameId` plus device serial, for example `pns (emulator-5558)`. If no serial: the game id. |
| `gameId` | string | `EmulatorSession.GameId` |
| `deviceSerial` | string or null | `EmulatorSession.DeviceSerial` |
| `state` | enum | see state table |
| `reason` | string or null | liveness reason when `no_answer` |
| `ownerQueueId` | string or null | `EmulatorSession.OwnerQueueId` |
| `ownerQueueName` | string or null | queue name from the queue store, for the warning text |
| `restartAvailable` | bool | true when the host has ldconsole and the serial maps to an LDPlayer instance |
| `restartUnavailableReason` | string or null | `no_device_serial`, `unsupported_serial`, `ldconsole_missing`, `platform_unsupported` |
| `startedAtUtc` | string (ISO 8601) | `EmulatorSession.StartTime` |

### State table (first matching rule wins)

| Order | Condition | `state` |
|-------|-----------|---------|
| 1 | A restart record for the session has state `restarting` | `restarting` |
| 2 | Liveness report state is `NotLive` | `no_answer` |
| 3 | Session status is `Stopping` | `stopping` |
| 4 | Otherwise | `live` |

The UI shows `ended` (not an API value) when a selected session leaves the list.

## SessionRestartRecord (in memory, last record for each session id)

| Field | Type | Rule |
|-------|------|------|
| `sessionId` | string | key |
| `state` | enum `restarting`, `success`, `failure` | |
| `step` | enum `starting`, `rebooting`, `waiting_for_device`, `reconnecting`, `checking_capture`, `done` | progress text for the UI |
| `startedAtUtc` | DateTimeOffset | set on accept |
| `endedAtUtc` | DateTimeOffset or null | set on `success` or `failure` |
| `reason` | string or null | set on `failure`. One of: `timeout`, `instance_not_found`, `reboot_failed`, `reconnect_failed`, `control_unavailable`, `cancelled`, `internal_error` |

### State transitions

```text
(none) --POST--> restarting --device answers and capture works--> success
                 restarting --120000 ms pass--> failure(timeout)
                 restarting --ldconsole error--> failure(reboot_failed | instance_not_found | ...)
success|failure --POST--> restarting      (a new record replaces the old record)
restarting --POST--> refused (409 restart_in_progress); the record does not change
```

### Rules

- One record in state `restarting` for each session at most (FR-013). The check and set are one atomic step.
- A record is dropped when its session is removed and its state is not `restarting`.
- A restart for session A never reads or changes the record or the instance of session B (SC-007). The instance index comes only from the serial of session A.

## Options: SessionMonitoringOptions (config section `Service:Monitoring`)

| Field | Default | Meaning |
|-------|---------|---------|
| `RestartTimeoutMs` | 120000 | Total limit of one restart (FR-016) |
| `RestartPollIntervalMs` | 3000 | Wait between device checks (FR-022). Separate from the fixed 2 s screenshot interval. Must be greater than 0 and less than `RestartTimeoutMs`. |

The all-sessions list (FR-002) has no status filter. The `stopping` state stays in the list until the session is removed.

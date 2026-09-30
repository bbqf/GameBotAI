# Contract: Device Recovery API and Alert Messages

This feature adds one queue field, three read-only health members, two message kinds, and three settings. It adds no new route.

## 1. Queue field `deviceRecovery`

Routes that accept it: `POST /api/queues`, `PUT /api/queues/{id}`. Routes that return it: every route that returns a queue (`GET /api/queues`, `GET /api/queues/{id}`, and the create, update, and duplicate responses).

Request and response member (camelCase JSON):

```json
{
  "deviceRecovery": {
    "action": "reboot-instance",
    "afterMs": 300000,
    "maxAttempts": 2,
    "cooldownMs": 180000
  }
}
```

| Member | Type | Required | Default | Allowed |
|--------|------|----------|---------|---------|
| `action` | string | no | `none` | `none`, `reboot-instance` |
| `afterMs` | integer | no | 300000 | 60000 or more |
| `maxAttempts` | integer | no | 2 | 1 to 5 |
| `cooldownMs` | integer | no | 180000 | 0 or more |

Rules:

- The member `deviceRecovery` is optional. A queue with no member behaves as before this feature. The response shows `null`.
- On `PUT`, an absent `deviceRecovery` sets the field to null.
- The response always shows the members with their default values filled in.
- Duplicate (`POST /api/queues/{id}/duplicate`) copies the field.

### Validation errors (HTTP 400, never 500)

The body uses the existing queue error shape. The text names the member and the allowed range.

| Case | Error text (STE) |
|------|------------------|
| Unknown `action` | `deviceRecovery.action must be one of: none, reboot-instance (was: '<value>')` |
| `reboot-instance` with no instance name | `deviceRecovery.action 'reboot-instance' needs emulatorInstanceName. Set emulatorInstanceName on the queue.` |
| `afterMs` below 60000 | `deviceRecovery.afterMs must be at least 60000 (was: <value>)` |
| `maxAttempts` outside 1 to 5 | `deviceRecovery.maxAttempts must be from 1 to 5 (was: <value>)` |
| `cooldownMs` below 0 | `deviceRecovery.cooldownMs must be at least 0 (was: <value>)` |

The validator checks the ranges also when `action` is `none`. The validator runs before the queue is saved, so a rejected request changes nothing.

## 2. Queue health

`GET /api/queues/{id}` returns `health.deviceLiveness` as before (feature 106). Three read-only members are added. They do not change the detection rules.

| Member | Type | Meaning |
|--------|------|---------|
| `alertSent` | boolean | The "device not live" alert was sent for the open episode. False when no episode is open. |
| `recoveryAttempts` | integer | The count of finished recovery attempts in the open episode. 0 when no episode is open. |
| `recoveryState` | string | `idle` (no recovery runs and the attempts are not used up), `running` (a recovery waits for the slot or a reboot runs), `exhausted` (`maxAttempts` reached and the device is not live). Always one of these three values. |

`health.lastNotificationAt`, `health.lastNotificationSucceeded`, and `health.lastNotificationError` show the last alert result. They show it also when the queue has no failure policy.

The OpenAPI descriptions of `deviceRecovery`, these members, and the text "the queue can reboot the instance when deviceRecovery is on" are added through schema filters (FR-016). The old statement that the queue never repairs the device is replaced.

## 3. Alert messages

The worker sends each message as plain text to every enabled notification target. It ignores `notificationLevel` and the failure policy.

| Kind | When | Text |
|------|------|------|
| `NotLive` | The episode is older than `AlertAfterMs`. One time for each episode. | `🔴 <queue name> : device not live (<reason>)` |
| `LiveAgain` | The device is live after a `NotLive` alert. One time for each episode. | `🟢 <queue name> : device live again` |
| `RecoveryFailed` | The last allowed attempt failed. One time for each episode. | `🔴 <queue name> : device recovery failed after <n> attempts` |

`<reason>` is one of `capture_stalled`, `input_timeout`, `transport_not_ready`, `no_change_after_input`. When the queue name is blank, the queue ID is used.

No other message is sent for an episode.

## 4. Settings (`Service:DeviceLiveness`)

| Setting | Default | Minimum |
|---------|---------|---------|
| `AlertAfterMs` | 300000 | 1000 |
| `RecoveryStaggerMs` | 180000 | 0 |
| `RebootReadyTimeoutMs` | 180000 | 1000 |

A value below the minimum is raised to the minimum. The existing settings do not change.

## 5. External command

Recovery runs one command: `ldconsole.exe reboot --name "<emulatorInstanceName>"`. The path comes from the existing `LdConsoleResolver`. Exit code 0 means success. Any other result, or a missing tool, counts as a failed attempt.

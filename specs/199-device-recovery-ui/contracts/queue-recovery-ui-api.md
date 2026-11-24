# Contract: Device Recovery in the Web UI and the API

**Feature**: 199-device-recovery-ui | **Date**: 2026-11-24

## 1. `PUT /api/queues/{id}`: `deviceRecovery` rule (changed)

This rule replaces the rule "On `PUT`, an absent `deviceRecovery` sets the field to null" of `specs/121-device-not-live-alert-recovery/contracts/device-recovery-api.md`.

| Request body | Result |
|---|---|
| No `deviceRecovery` member | The stored value stays. |
| `"deviceRecovery": null` | The stored value is cleared (no recovery). |
| `"deviceRecovery": { ... }` | Mapped and validated as in feature 121. The errors and their texts do not change. |

A saved change applies to a running queue at its next liveness check (1 minute or less). The current not-live episode keeps its attempt count.

`POST /api/queues` does not change: no member means no recovery.

The OpenAPI description of `deviceRecovery` on the update request MUST state the three rows of the table and the running-queue rule.

## 2. `GET /api/queues`: `deviceLiveness` (new, list only)

```json
[
  { "id": "q1", "name": "PNS", "status": "Running", "operatorPause": "none", "deviceLiveness": "not_live" },
  { "id": "q2", "name": "Farm", "status": "Stopped" }
]
```

- Values: `live`, `not_live`.
- Present only for a running queue whose liveness episode has a report. Absent in all other cases.
- The value can be up to one liveness check interval old. `GET /api/queues/{id}` gives the current state in `health.deviceLiveness`.

## 3. `GET /api/sessions/monitoring`: `ownerQueueDeviceLiveness` (new)

```json
{ "sessions": [ { "id": "s1", "ownerQueueId": "q1", "ownerQueueName": "PNS", "state": "live", "ownerQueueDeviceLiveness": "not_live" } ] }
```

- Values: `live`, `not_live`, or `null`.
- `null` when the session has no owner queue, the owner queue does not run, or its episode has no report.
- The member `state` of feature 141 does not change.

## 4. Web UI

### Queue form (create and edit, desktop and tablet layout)

Position: after the row "Emulator instance name | Emulator instance index".

| Element | Label | Accessible name | Shown |
|---|---|---|---|
| Checkbox | "Reboot the instance when the device is not live" | `Device recovery` | always |
| Help text | "When the device stays not live, the service reboots the emulator instance. It needs the emulator instance name." | — | always |
| Number input, step 0.1, min 1 | "Wait before the first reboot (minutes)" | `Recovery wait minutes` | option on |
| Number input, step 1, min 1, max 5 | "Maximum attempts" | `Recovery maximum attempts` | option on |
| Number input, step 0.1, min 0 | "Time between attempts (minutes)" | `Recovery time between attempts minutes` | option on |

Field messages (role `alert`, at the field):

| Condition | Text |
|---|---|
| Option on, instance name empty | "Device recovery needs an emulator instance name." |
| Wait below 1 minute or too large | "Enter 1 minute or more." |
| Attempts not a whole number from 1 to 5 | "Enter a whole number from 1 to 5." |
| Time between attempts below 0 or too large | "Enter 0 minutes or more." |

A field message stops the save. A 400 from the service shows its `message` in the form error area.

Running queue note near Save: "Settings apply at the next start. Smart scheduling applies to the next booking. Device recovery applies within 1 minute."

### Device state (edit view of a running queue)

Part of the running queue controls. Role `status`.

| `health.deviceLiveness` | Text |
|---|---|
| absent or null | no line |
| `state: live` | "Device: live" |
| `state: not_live` | "Device: not live (<reason>) since <local time>" |
| `alertSent` | "Alert sent" or "No alert yet" (only when not live) |
| recovery | "Recovery: <idle \| running \| exhausted>, attempts <n>" when the queue has `action: reboot-instance`, else "Recovery: off" (only when not live) |

### "Not live" mark

| View | Source | Shown when |
|---|---|---|
| Phone queue card | `QueueDto.deviceLiveness` | `not_live` |
| Phone queue detail | `detail.health.deviceLiveness.state` | `not_live` |
| Monitoring session row (`SessionList`) and the header of the selected session | `MonitoredSessionDto.ownerQueueDeviceLiveness` | `not_live` |

The mark is the text "Not live" in a small badge with a warning color that passes the contrast rule. It wraps with the text and causes no horizontal scroll at 360 px.

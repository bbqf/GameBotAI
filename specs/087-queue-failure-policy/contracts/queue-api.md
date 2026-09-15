# Contract: Queue API Changes

**Feature**: 087-queue-failure-policy | **Date**: 2026-09-14

Every change here is **additive**. No existing field changes name, type, or meaning, and no existing
status code changes.

---

## 1. `failurePolicy` on the queue body

Accepted by `POST /api/queues` and `PUT /api/queues/{id}`; returned by `GET /api/queues` and
`GET /api/queues/{id}`.

```json
{
  "name": "PNS Daily 5558",
  "emulatorSerial": "emulator-5558",
  "cycleExecution": true,
  "failurePolicy": {
    "consecutiveFailedCycles": 5,
    "action": "notify",
    "notifyUrl": "http://127.0.0.1:9099/gamebot-alerts"
  }
}
```

Omitted or `null` ⇒ no policy; the queue behaves exactly as today (FR-004).

### Validation (400 with an actionable message)

| Condition | Message |
|---|---|
| `consecutiveFailedCycles` < 1 | `failurePolicy.consecutiveFailedCycles must be at least 1 (was: 0)` |
| `action` not one of the four | `failurePolicy.action must be one of: notify, stop, pause, notifyAndStop (was: 'halt')` |
| `notifyUrl` not an absolute http/https URI | `failurePolicy.notifyUrl must be an absolute http or https URL (was: 'localhost:9099')` |
| Notifying action, no URL anywhere | `failurePolicy.action 'notify' requires a destination: set failurePolicy.notifyUrl or Service:Notifications:DefaultUrl` |

The last one is the case FR-006 exists to prevent: a policy that looks configured and can never fire.

---

## 2. `GET /api/queues/{id}` — new `health` fields

The `health` block is unchanged in presence semantics: present **only** when the same response
reports `status: "Running"` (feature 086's conjunction rule). Eight fields are added to it.

```json
{
  "id": "9f2c...",
  "name": "PNS Daily 5558",
  "status": "Running",
  "failurePolicy": { "consecutiveFailedCycles": 5, "action": "pause", "notifyUrl": null },
  "health": {
    "runStartedAt": "2026-09-12T10:53:04+02:00",
    "cyclesCompleted": 417,
    "lastCycleStartedAt": "2026-09-14T19:12:40+02:00",
    "lastCycleCompletedAt": "2026-09-14T19:18:02+02:00",
    "lastCycleStatus": "failure",
    "consecutiveFailedCycles": 39,
    "currentEntryIndex": 2,
    "currentSequenceId": "seq-alliance-help",

    "failurePolicyConfigured": true,
    "failurePolicyTripped": true,
    "paused": true,
    "pausedAt": "2026-09-14T19:18:02+02:00",
    "pauseReason": "failure policy: 5 consecutive failed cycles",
    "lastNotificationAt": null,
    "lastNotificationSucceeded": null,
    "lastNotificationError": null
  }
}
```

**`AuthHeaderValue` never appears in this or any other response.** Asserted by a contract test.

---

## 3. `POST /api/queues/{id}/resume` (new)

Releases a run paused by a tripped `pause` policy. Follows the `{id}/stop` convention.

| Case | Status | Body |
|---|---|---|
| Paused run resumed | `200` | `{ "id": "...", "status": "Running", "resumed": true }` |
| Queue is running but **not** paused | `200` | `{ "id": "...", "status": "Running", "resumed": false }` |
| Queue is not running | `200` | `{ "id": "...", "status": "Stopped", "resumed": false }` |
| Unknown queue | `404` | standard error body |

**Why 200 for the not-applicable cases**: this mirrors `{id}/monitor` and `{id}/cycles`, which
return a state rather than an error for "known queue, nothing running" — so a polling or scripted
client renders a state instead of handling an exception. `resumed` is the boolean a caller branches
on. Resuming is idempotent: a second call returns `resumed: false` and changes nothing.

**Effects of a successful resume** (FR-020): clears `PausedAt`/`PauseReason`, clears `PolicyTripped`
so the policy is re-armed, and releases the run loop's gate. Due-ness is then re-evaluated normally
— a time-of-day slot not yet fired today fires, an elapsed relative/live schedule fires at once
(FR-018a).

---

## 4. `POST /api/queues/{id}/stop` — unchanged

A paused run is stopped by the existing endpoint with existing semantics (FR-021). No change.

---

## 5. Sequence `notify` action step (User Story 3)

A new action type accepted wherever sequence steps are authored.

```json
{
  "stepId": "step-7",
  "stepType": "Action",
  "primitiveAction": {
    "type": "notify",
    "schemaVersion": "1",
    "payload": {
      "message": "Unrecognised screen; BACK did not dismiss it.",
      "url": null
    }
  }
}
```

The fields live under `payload` (the action's parameter bag), matching how every other authorable
action type — `reschedule-self`, `wait-for-image`, the primitives — is already shaped. An earlier
draft of this document showed them as siblings of `type`; that was wrong and is corrected here.

### Validation (rejected at save time, FR-023)

| Condition | Message |
|---|---|
| `message` missing or blank | `notify action requires a non-empty 'message'` |
| `message` longer than 1000 chars | `notify action 'message' must be at most 1000 characters (was: 1240)` |
| `url` present but not absolute http/https | `notify action 'url' must be an absolute http or https URL` |
| No destination anywhere | `notify action requires a destination: set 'url' or Service:Notifications:DefaultUrl` |

### Runtime

Raises a `sequence.notify` event (see [notification-payload.md](./notification-payload.md)). The step
**always succeeds** — a delivery failure is recorded on the run's health and in the application log
but never fails the step or the enclosing sequence (FR-024). This is deliberate: a guard sequence
that cannot reach its alert receiver must still complete its own logic.

# Contract: A queue with recovery on that has no bound session

No new endpoint and no new field. This contract gives the values of the current fields while a queue with `deviceRecovery.action = reboot-instance` has no bound session.

## GET /api/queues/{id}

While the queue waits for its device:

```json
{
  "id": "6d6c3e513d0d46f4b3f4661a8be10771",
  "status": "Running",
  "deviceRecovery": { "action": "reboot-instance", "afterMs": 300000, "maxAttempts": 2, "cooldownMs": 180000 },
  "health": {
    "deviceLiveness": {
      "state": "not_live",
      "reason": "transport_not_ready",
      "notLiveSince": "2026-11-27T03:10:17+01:00",
      "stale": true,
      "frameAgeMs": null,
      "unchangedMs": null,
      "gatedFirings": 3,
      "alertSent": true,
      "recoveryAttempts": 1,
      "recoveryState": "idle"
    }
  }
}
```

- `status` is `Running` (today: `Stopped`).
- `health` is present (today: absent). The other fields of `health` keep their meaning.
- `health.deviceLiveness.recoveryState` is `running` while an attempt runs, `exhausted` after `maxAttempts` failed attempts, and else `idle`.
- `notLiveSince` is the start of the episode. When the episode was open before the session was lost, it keeps that time.

After a bind that succeeds, the values come from the new session again, as today.

## GET /api/sessions/running

- While the queue waits: no session for the serial of the queue.
- After a bind that succeeds: one session for the serial, with the owner queue ID of the queue. Never two sessions for one queue.

## Execution log

- The run root of the queue stays open. No `failure` root with "emulator connection lost mid-run" for a queue with recovery on.
- The first held firing of each sequence in the episode writes one sequence entry with the status `failure` and the summary `device_not_live: transport_not_ready`.

## Alerts

- "device not live", "recovery failed" and "device live again" are sent with the feature 121 rules. "recovery failed" is sent one time for the episode, also when no session is bound.

## No change

- A queue with `deviceRecovery.action = none`: the run ends with "Queue '<name>' failed: emulator connection lost mid-run ('<serial>')." and `status: Stopped`, as today.
- `POST /api/queues/{id}/start`, `POST /api/queues/{id}/stop`, and the `deviceRecovery` schema.

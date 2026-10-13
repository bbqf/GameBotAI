# Contract: Monitoring API

Base: `/api/sessions`. Same token gate as the other routes. Errors use the existing shape
`{ "error": { "code", "message", "hint" } }`. All messages are STE.

## GET /api/sessions/monitoring

List all sessions that the service holds. It does not probe the device.

200 response:

```json
{
  "sessions": [
    {
      "id": "a1b2",
      "name": "pns (emulator-5558)",
      "gameId": "pns",
      "deviceSerial": "emulator-5558",
      "state": "live",
      "reason": null,
      "ownerQueueId": "q1",
      "ownerQueueName": "PNS Daily 5558",
      "restartAvailable": true,
      "restartUnavailableReason": null,
      "startedAtUtc": "2026-10-13T08:00:00Z"
    }
  ]
}
```

- `state`: `live`, `no_answer`, `restarting`, `stopping`.
- An empty list gives `{ "sessions": [] }` (200).

## GET /api/sessions/{id}/snapshot (existing, no change)

Returns `image/png`. 404 `not_found`; 504 `capture_timeout`. The UI adds `?t=<ms>` to avoid a cached picture.
Staleness headers stay as defined in feature 106.

## POST /api/sessions/{id}/restart

Start a restart of the whole emulator of the session. No body.

| Status | Code | When |
|--------|------|------|
| 202 | - | Restart accepted. Body is the record (below). |
| 404 | `not_found` | Unknown session. |
| 409 | `restart_in_progress` | A restart for this session runs. The record does not change. |
| 422 | `restart_unavailable` | The system cannot control the emulator. Body has `reason`. The emulator is not changed (FR-014). |

202 body and `GET` body:

```json
{
  "sessionId": "a1b2",
  "state": "restarting",
  "step": "rebooting",
  "startedAtUtc": "2026-10-13T08:10:00Z",
  "endedAtUtc": null,
  "reason": null
}
```

- `state`: `restarting`, `success`, `failure`.
- `step`: `starting`, `rebooting`, `waiting_for_device`, `reconnecting`, `checking_capture`, `done`.
- `reason` (failure only): `timeout`, `instance_not_found`, `reboot_failed`, `reconnect_failed`, `control_unavailable`, `cancelled`, `internal_error`.
- `restart_unavailable` `reason`: `no_device_serial`, `unsupported_serial`, `ldconsole_missing`, `platform_unsupported`, `instance_not_found`.

## GET /api/sessions/{id}/restart

Return the last restart record of the session. 404 `not_found` for an unknown session. 404 `no_restart` when the session
never restarted.

## Rules

- A restart request never changes a session other than `{id}` (SC-007).
- The restart ends in at most `RestartTimeoutMs` (120000 ms). Then `state` = `failure`, `reason` = `timeout`.
- Each restart writes log entries with the session id, the step, and the result (FR-017).
- OpenAPI: all three routes, their DTOs, and the enum values appear with descriptions (FR-018), through an `ISchemaFilter`.
- Route clash (FR-021): `GET /api/sessions/monitoring` is a literal route and wins over `GET /api/sessions/{id}`. A contract test proves
  that the list returns 200 (not `not_found`) and that a real session id still reaches `{id}`, `{id}/snapshot`, and `{id}/health`.
- Reconnect (FR-015): every successful restart reconnects the session (capture loop stop and start, same session id). A test with a
  fake reconnector proves it (FR-023).
- Contract tests: 404, 409 (two POST calls with a fake slow control), 422 (session without a serial), 202 then GET shows
  `success` (fake control), the list includes a session with a `no_answer` state, and no change of a second session.

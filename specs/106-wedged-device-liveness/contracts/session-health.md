# Contract: `GET /api/sessions/{id}/health` (FR-007 to FR-009)

The response gets a new `liveness` block. The fields `id`, `mode`, `deviceSerial` and `adb` do not change.

## Response 200: ADB session

```json
{
  "id": "4f1c2e...",
  "mode": "ADB",
  "deviceSerial": "emulator-5558",
  "adb": { "ok": true, "stdout": "device", "stderr": "" },
  "liveness": {
    "state": "not_live",
    "reason": "no_change_after_input",
    "frameAgeMs": 480,
    "unchangedMs": 412000,
    "stale": true,
    "lastInputAt": "2026-09-25T09:41:07.113+00:00",
    "lastInputOutcome": "completed"
  }
}
```

When the transport check fails or does not answer in `TransportCheckTimeoutMs`, the `adb` block keeps its current error form, `{ "ok": false, "error": "<text>" }`. The `liveness.state` is then `not_live` with the reason `transport_not_ready`.

## Response 200: stub session (no device)

```json
{
  "id": "4f1c2e...",
  "mode": "STUB",
  "deviceSerial": null,
  "adb": { "ok": true },
  "liveness": {
    "state": "unknown",
    "reason": null,
    "frameAgeMs": null,
    "unchangedMs": null,
    "stale": false,
    "lastInputAt": null,
    "lastInputOutcome": null
  }
}
```

## `liveness` fields

| Field | Type | Values |
|---|---|---|
| `state` | string | `live`, `not_live`, `unknown` |
| `reason` | string or null | `capture_stalled`, `input_timeout`, `no_change_after_input`, `transport_not_ready`. Null when `state` is not `not_live`. |
| `frameAgeMs` | integer or null | Milliseconds since the last completed capture. `0` after a direct capture. Null when no capture data exists. |
| `unchangedMs` | integer or null | Milliseconds since the frame bytes last changed. Null when no capture loop data exists. |
| `stale` | boolean | `true` when a capture loop runs, and `unchangedMs > StaleLimitMs` or `frameAgeMs > CaptureStallLimitMs`. `false` when no capture loop runs. A stale capture alone does not make the state `not_live`. |
| `lastInputAt` | date-time or null | Start of the last input command sent through the service (inputs endpoint or sequence input). |
| `lastInputOutcome` | string or null | `pending`, `completed`, `timed_out`, `failed`, `cancelled`. `cancelled`: the caller stopped the input before the input time limit. |

## Behavior

1. The service runs `adb get-state` with the limit `TransportCheckTimeoutMs`.
2. The service evaluates the tracker data (data-model section 4).
3. The session has a device, but no capture loop runs, or the loop has no completed capture (rule 7, `NeedsProbe`). Then the service does one direct capture with the limit `CaptureTimeoutMs`. Success gives `live`. Failure or time-out gives `not_live` with `capture_stalled`.
4. The worst response time is `TransportCheckTimeoutMs + CaptureTimeoutMs` plus a small margin (15 s with the defaults).

## Errors

| Status | Body | When |
|---|---|---|
| 404 | `{ "error": { "code": "not_found", ... } }` | No change. |

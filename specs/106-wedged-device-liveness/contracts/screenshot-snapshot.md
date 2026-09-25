# Contract: screenshot and snapshot staleness (FR-010, FR-011)

Endpoints: `GET /api/emulator/screenshot` and `GET /api/sessions/{id}/snapshot`.

## New response headers (200)

| Header | Value | Description |
|---|---|---|
| `X-Capture-Age-Ms` | integer, invariant culture | Milliseconds since the frame was captured. `0` for a direct capture. |
| `X-Capture-Unchanged-Ms` | integer, invariant culture | Milliseconds since the frame bytes last changed. `0` for a direct capture when no capture loop runs. |
| `X-Capture-Stale` | `true` or `false` | The stale flag of the session liveness report. `false` for a direct capture when no capture loop runs. |

`X-Capture-Id` does not change. It stays on the screenshot endpoint only.

Where each endpoint adds the headers:

| Endpoint | Source of the frame | Headers |
|---|---|---|
| screenshot | cached frame of the capture loop | always |
| screenshot | direct capture | always |
| snapshot | direct capture | only when the session has capture data (a capture loop runs, or ran, for the session) |

The service exposes all four headers to browser clients: `WithExposedHeaders("X-Capture-Id", "X-Capture-Age-Ms", "X-Capture-Unchanged-Ms", "X-Capture-Stale")`.

## Example (stale cached frame)

```http
HTTP/1.1 200 OK
Content-Type: image/png
X-Capture-Id: 7a0c...
X-Capture-Age-Ms: 312
X-Capture-Unchanged-Ms: 845210
X-Capture-Stale: true
```

## Capture time-out (new, 504)

The direct capture gets the limit `CaptureTimeoutMs`. When the limit is reached, the service kills the `adb` process and returns `504`. The service registers the kill on the token before the read starts, so a blocked read of the screenshot pipe also stops (research R-004). The response time is at most `CaptureTimeoutMs` plus 2 s (SC-004).

Screenshot endpoint (its current error form):

```json
{ "error": "capture_timeout", "message": "The device did not return a screenshot in 10000 ms. Check the emulator, or restart it." }
```

Snapshot endpoint (the session error form):

```json
{ "error": { "code": "capture_timeout", "message": "The device did not return a screenshot in 10000 ms.", "hint": "Check the emulator, or restart it." } }
```

A cancel by the client (the request token) is not a `504`. The current behavior for it does not change.

## Current errors (no change)

| Status | Endpoint | Body |
|---|---|---|
| 404 | screenshot | `session_not_found` |
| 409 | screenshot | `ambiguous_session` |
| 503 | screenshot | `emulator_unavailable` (no session, or a direct capture that failed with an error that is not a time-out) |
| 404 | snapshot | `not_found` |

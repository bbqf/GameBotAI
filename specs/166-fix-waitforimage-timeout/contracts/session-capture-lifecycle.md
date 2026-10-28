# Contract: Session capture lifecycle (`POST /api/sessions`, `DELETE /api/sessions/{id}`)

The request and response shapes do not change. Only the side effect on the background capture loop changes.

## `POST /api/sessions`

- Request, status codes, and response body: no change.
- New side effect: when the service runs in ADB mode (`BackgroundScreenCaptureService` is registered) and the new session has a device serial, the service starts the capture loop of the session before it returns `201`.
- After the first capture, these calls read real frames for the session:
  - `POST /api/steps/execute` and `POST /api/commands/{id}/force-execute` (`WaitForImage` and `PrimitiveTap` steps),
  - `POST /api/sequences/{id}/execute` (inline `WaitForImage` steps and `imageVisible` conditions),
  - `POST /api/images/detect` with `sessionId`,
  - `GET /api/emulator/screenshot?sessionId=...` (from the cache, not the direct ADB fallback).
- Stub mode (`GAMEBOT_USE_ADB=false`): no capture service, no change.
- If the start of the loop fails, the service writes a warning log and still returns `201` for the session that it made.

## `DELETE /api/sessions/{id}`

- Request, status codes, and response body: no change.
- New side effect: the service stops the capture loop of the session (if one runs) before it stops the session.
- After the call, the capture service has no cached frame and no capture metrics for the session.

## Removal of a session for another reason (no endpoint)

- When the session manager removes a session for any reason (for example the idle sweep, or a queue run end), the service stops the capture loop of the session.
- The idle sweep runs at the start of a session manager call, as before. The loop stops at that time.
- No loop stays on for a session that is not in the session table.

## Unchanged paths (for reference)

- `POST /api/sessions/start` and `POST /api/sessions/stop` (`SessionsController`): already start and stop the loop through `SessionService`.
- Queue runs: already start the loop in `QueueExecutionService.BindQueueSession`.

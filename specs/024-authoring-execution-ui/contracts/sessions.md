# Sessions Contracts

## GET /api/sessions/running
- Purpose: Return all running sessions for display in the execution UI.
- Query: none.
- Response 200:
```json
{
  "sessions": [
    {
      "sessionId": "string",
      "gameId": "string",
      "emulatorId": "string",
      "startedAtUtc": "2026-01-28T00:00:00Z",
      "lastHeartbeatUtc": "2026-01-28T00:01:00Z",
      "status": "running"
    }
  ]
}
```

## POST /api/sessions/start
- Purpose: Start a new session and enforce single running session per game/emulator.
- Body:
```json
{
  "gameId": "string",
  "emulatorId": "string",
  "options": {}
}
```
- Behavior: If a session already exists for the pair, attempt stop; on stop failure, remove prior entry and continue with the new session.
- Response 200:
```json
{
  "sessionId": "string",
  "runningSessions": [
    {
      "sessionId": "string",
      "gameId": "string",
      "emulatorId": "string",
      "status": "running"
    }
  ]
}
```

## POST /api/sessions/stop
- Purpose: Stop a session from banner or running list.
- Body:
```json
{
  "sessionId": "string"
}
```
- Response 200:
```json
{ "stopped": true }
```
- If session not found, respond 404 with actionable message.

## Error Handling
- 400: validation errors (missing gameId/emulatorId/sessionId) with messages preserved in UI.
- 404: session not found for stop.
- 500: unexpected server error; include correlation id if available.

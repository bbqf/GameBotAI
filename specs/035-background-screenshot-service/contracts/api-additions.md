# API Contract Additions: Background Screenshot Service

**Feature**: 034-background-screenshot-service  
**Date**: 2025-04-13

## Modified Endpoints

### GET /api/sessions/running

**Change**: `RunningSessionDto` response objects now include `captureRateFps`.

**Response body** (modified fields only):

```json
{
  "sessions": [
    {
      "sessionId": "abc-123",
      "gameId": "game1",
      "emulatorId": "emulator-5554",
      "startedAtUtc": "2025-04-13T10:00:00Z",
      "lastHeartbeatUtc": "2025-04-13T10:05:00Z",
      "status": "Running",
      "captureRateFps": 2.3
    }
  ]
}
```

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| captureRateFps | number | yes | Frames per second from background capture loop. `null` when no capture loop is active for this session. |

**Backward compatibility**: Additive field. Existing clients that do not read `captureRateFps` are unaffected.

---

### POST /api/sessions/start

**Change**: Same — `runningSessions` array in response now includes `captureRateFps` per session.

---

### GET /api/emulator/screenshot

**Change**: Now serves the cached frame from the background capture service instead of calling ADB directly.

**New optional query parameter**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| sessionId | string | (first running session) | Optional session ID to fetch screenshot for. When omitted, serves frame from the first running session. |

**Response**: Unchanged — `image/png` with `X-Capture-Id` header.

**Behavior change**:
- Previously: Synchronous ADB screencap (200–1000ms latency).
- Now: Returns cached frame from background service (<5ms).
- If no cached frame is available (loop hasn't captured yet), returns 503 with existing error format.
- If no running session exists, returns 503 with existing error format.

---

## DTO Changes

### RunningSessionDto (C# backend)

```csharp
public sealed class RunningSessionDto {
    public required string SessionId { get; init; }
    public required string GameId { get; init; }
    public required string EmulatorId { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime LastHeartbeatUtc { get; init; }
    public RunningSessionStatus Status { get; init; }
    public double? CaptureRateFps { get; init; }  // NEW
}
```

### RunningSessionDto (TypeScript frontend)

```typescript
export type RunningSessionDto = {
  sessionId: string;
  gameId: string;
  emulatorId: string;
  startedAtUtc: string;
  lastHeartbeatUtc: string;
  status: 'Running' | 'Stopping' | 'running' | 'stopping';
  captureRateFps?: number | null;  // NEW
};
```

## No New Endpoints

All changes are additive modifications to existing endpoints. No new routes are introduced.

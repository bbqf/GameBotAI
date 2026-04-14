# Data Model: Background Screenshot Service

**Feature**: 034-background-screenshot-service  
**Date**: 2025-04-13

## Entities

### CachedFrame

The immutable snapshot of a single captured screenshot, held in memory per session.

| Field | Type | Description |
|-------|------|-------------|
| PngBytes | byte[] | Raw PNG data from ADB screencap |
| Bitmap | System.Drawing.Bitmap | Decoded bitmap for detection/trigger consumers |
| Timestamp | DateTimeOffset | UTC time when the frame was captured |
| Width | int | Image width in pixels |
| Height | int | Image height in pixels |

**Identity**: Transient; replaced atomically on each capture cycle. No external ID.  
**Lifecycle**: Created by capture loop on each successful capture. Replaced by next capture. Previous frame's Bitmap is disposed by the capture loop after atomic swap.  
**Relationships**: Owned by a single `SessionCaptureLoop` instance.

---

### SessionCaptureLoop (internal)

Manages the background capture thread for one active session.

| Field | Type | Description |
|-------|------|-------------|
| SessionId | string | Owning session identifier |
| DeviceSerial | string | ADB device serial for screencap |
| CurrentFrame | CachedFrame? | Latest captured frame (volatile reference) |
| CaptureRateFps | double | Rolling average FPS over last N captures |
| IsRunning | bool | Whether the loop is actively capturing |
| CancellationTokenSource | CancellationTokenSource | Used to signal graceful shutdown |

**Identity**: Keyed by `SessionId` in the service's dictionary.  
**Lifecycle**: Created when session starts → runs until session stops or service disposes → CancellationToken cancelled → thread exits → Bitmap disposed.  
**State transitions**: Idle → Running → Stopping → Stopped (disposed).

---

### CaptureMetrics

Lightweight value object exposed to API consumers.

| Field | Type | Description |
|-------|------|-------------|
| CaptureRateFps | double? | Frames per second (null if no capture loop active) |
| FrameCount | long | Total frames captured since loop start |
| LastCaptureUtc | DateTimeOffset? | Timestamp of most recent successful capture |

**Identity**: Computed on demand; not persisted.  
**Relationships**: One per active session, read from `SessionCaptureLoop`.

---

### RunningSessionDto (modified)

Existing DTO augmented with capture metrics.

| Field | Type | Change |
|-------|------|--------|
| SessionId | string | Existing |
| GameId | string | Existing |
| EmulatorId | string | Existing |
| StartedAtUtc | DateTime | Existing |
| LastHeartbeatUtc | DateTime | Existing |
| Status | RunningSessionStatus | Existing |
| CaptureRateFps | double? | **NEW** — null when no capture loop is active |

---

## Relationships

```
SessionService (1) ──manages──> (0..*) SessionCaptureLoop
SessionCaptureLoop (1) ──holds──> (0..1) CachedFrame
SessionCaptureLoop (1) ──uses──> (1) AdbClient (via session's DeviceSerial)
BackgroundCaptureScreenSource ──reads from──> SessionCaptureLoop.CurrentFrame
EmulatorImageEndpoints ──reads from──> SessionCaptureLoop.CurrentFrame (PNG bytes)
RunningSessionDto ──includes──> CaptureMetrics.CaptureRateFps
```

## Validation Rules

- `DeviceSerial` must be non-null/non-whitespace when starting a capture loop.
- `CaptureIntervalMs` (global config) must be ≥50ms to prevent CPU thrashing (clamped at registration).
- `CachedFrame.PngBytes` must be non-null and non-empty (failed captures are not cached).
- `CachedFrame.Bitmap` must be non-null and match `PngBytes` dimensions.
- Rolling window size for FPS calculation: fixed at 10 samples (not configurable).

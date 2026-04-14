# Research: Background Screenshot Service

**Feature**: 034-background-screenshot-service  
**Date**: 2025-04-13

## Research Tasks

### R1: Background thread pattern for per-session capture loop

**Context**: Need a dedicated background thread per active session that continuously captures ADB screenshots.

**Decision**: Use a plain `Thread` (or `Task.Run` with a long-running option) per session, managed by a service class. The service holds a `ConcurrentDictionary<string, SessionCaptureLoop>` keyed by session ID. Each loop runs `while (!cancelled)`: capture → cache → delay.

**Rationale**:
- `IHostedService` / `BackgroundService` is designed for singleton application-lifetime services, not per-session dynamic lifecycles. A custom managed thread per session is cleaner.
- `Task.Run(LongRunning)` allocates a dedicated thread and integrates well with `CancellationToken` for clean shutdown.
- The service itself can be registered as a singleton and orchestrated by `SessionService` lifecycle hooks.

**Alternatives considered**:
- `System.Threading.Timer` per session: simpler but doesn't prevent overlapping captures when ADB is slow. A loop with delay-after-completion naturally serializes.
- `Channel<T>` producer-consumer: over-engineered for single-producer single-consumer with one cached item.
- `IHostedService` per session: ASP.NET doesn't support dynamic hosted service registration; would need a container managing internal loops anyway.

---

### R2: Thread-safe dual-format frame caching (PNG + Bitmap)

**Context**: Consumers need both `Bitmap` (detection pipeline, trigger evaluators) and `byte[]` PNG (HTTP endpoint). Both must be available concurrently from multiple reader threads while the capture thread writes.

**Decision**: Each `SessionCaptureLoop` holds a `volatile` reference to an immutable `CachedFrame` record containing both formats plus metadata (timestamp, width, height). Swap is atomic via reference assignment. Readers get a snapshot reference; no locking needed for reads.

**Rationale**:
- Immutable record + volatile reference swap is lock-free for readers, which is the hot path (FR-002 demands <5ms).
- The writer (capture thread) creates a new `CachedFrame` each cycle and atomically publishes via `Volatile.Write` or `Interlocked.Exchange`.
- `Bitmap` is not thread-safe for concurrent pixel access, but consumers already clone it (existing `CachedScreenSource` returns clones). The background service will store the Bitmap for cloning by consumers.

**Alternatives considered**:
- `ReaderWriterLockSlim`: adds lock overhead on every read; unnecessary since reference swap is atomic.
- `lock(obj)` on read/write: adds contention; existing `CachedScreenSource` uses this but we can do better.
- Store only PNG, decode on read: violates spec decision (Q5: cache both formats).

---

### R3: Capture rate metric calculation (rolling window)

**Context**: Need to track actual FPS and expose it. Rolling window smoothing specified.

**Decision**: Maintain a circular buffer of the last 10 capture timestamps (or durations). Compute FPS as `count / totalSpan` over the window. Expose as a `double` on the session capture loop, read by the sessions API.

**Rationale**:
- Simple, low overhead, no allocations in steady state.
- 10-sample window smooths jitter while remaining responsive to actual rate changes.
- UI formatting (FPS vs s/frame) is done client-side: if `fps >= 1` show "X.Y FPS", else show "X.Y s/frame".

**Alternatives considered**:
- Exponential moving average: slightly smoother but harder to reason about "last N" semantics.
- Per-frame stopwatch only: instantaneous, too noisy for display.

---

### R4: Session lifecycle integration (start/stop capture loop)

**Context**: Capture loops must start when a session enters Running state and stop when the session is removed.

**Decision**: Hook into `SessionService.StartSession()` and `SessionService.StopSession()` (plus `SyncFromSessionManager` cleanup). The `BackgroundScreenCaptureService` exposes `StartCapture(sessionId, deviceSerial)` and `StopCapture(sessionId)` methods. `SessionService` calls these at the appropriate lifecycle points.

**Rationale**:
- `SessionService` already manages the high-level lifecycle (start, stop, sync). Adding capture start/stop calls here keeps lifecycle logic centralized.
- The capture service itself doesn't need to know about session management — it just receives start/stop commands with session ID and device serial.

**Alternatives considered**:
- Event-based (pub/sub): adds indirection; the call sites are already known and few.
- Polling from capture service: wasteful; the lifecycle events are discrete and known.
- Integration in `SessionManager` (Emulator layer): would add UI-service-level concerns to the emulator layer. Better to keep in Service layer where DI wiring happens.

---

### R5: Rerouting existing consumers to background service

**Context**: 5 production call sites use `IScreenSource.GetLatestScreenshot()`. The HTTP endpoint uses `ISessionManager.GetSnapshotAsync()`.

**Decision**: 
- **IScreenSource path**: Replace DI registration. Instead of `AdbScreenSource` → `CachedScreenSource` chain, register a new `BackgroundCaptureScreenSource` that implements `IScreenSource` and reads from the background service's cached frame. This preserves the `IScreenSource` contract — existing consumers are oblivious to the change.
- **HTTP endpoint path**: Modify `EmulatorImageEndpoints` to read PNG bytes from the background service instead of calling `GetSnapshotAsync()`. Add optional `sessionId` query param; default to first running session if not specified.
- **AdbScreenSource**: Retained for use exclusively by the background capture loop (the loop needs something to actually call ADB). Not registered in DI for consumers anymore.

**Rationale**:
- Minimal consumer-side changes: `IScreenSource` contract is unchanged; HTTP endpoint change is isolated to one endpoint.
- Clean separation: `AdbScreenSource` → background loop (writer); `BackgroundCaptureScreenSource` → consumers (reader).
- The existing `CachedScreenSource` TTL wrapper becomes unnecessary when the background service provides always-fresh frames. It can be removed from the DI chain.

**Alternatives considered**:
- Modify `AdbScreenSource` directly to read from cache: conflates the writer (ADB caller) with the reader (cache provider). Less clean.
- Keep `CachedScreenSource` in the chain: redundant caching layer; the background service already serves cached frames.

---

### R6: API endpoint additions for capture metrics

**Context**: Need to expose capture rate per session for the UI.

**Decision**: Add a `captureRate` property to the existing `RunningSessionDto` / `RunningSessionsResponse`. The sessions running endpoint already returns session data; augmenting it avoids a new endpoint. The capture rate is a nullable `double?` (null when no capture loop is active for that session).

**Rationale**:
- Backward-compatible: existing clients ignore unknown fields.
- No new endpoint needed; data joined at the service layer when building the DTO.
- UI polls this endpoint already for session status; FPS updates piggyback on the same poll cycle.

**Alternatives considered**:
- Separate `/api/sessions/{id}/capture-rate` endpoint: over-engineered for a single field.
- WebSocket push: adds complexity; polling at 2s intervals is sufficient per SC-004.

---

### R7: UI display of capture rate metric

**Context**: Execution tab displays running sessions in a list. Need to add FPS metric.

**Decision**: Add a `<span>` in the running session row showing formatted capture rate. Client-side formatting: if `captureRate >= 1`, show `"{rate.toFixed(1)} FPS"`; if `captureRate > 0 && captureRate < 1`, show `"{(1/rate).toFixed(1)} s/frame"`; if `null` or `0`, show `"—"`.

**Rationale**:
- Minimal UI change — one line added per session row.
- Formatting logic is trivial and best done client-side to avoid backend formatting concerns.
- Consistent with existing session row layout (Session ID, Game, Emulator, Status, Stop button).

**Alternatives considered**:
- Dedicated FPS component: over-engineered for a single formatted span.
- Backend-formatted string: couples display format to API; less flexible.

# Feature Specification: Background Screenshot Service

**Feature Branch**: `034-background-screenshot-service`  
**Created**: 2025-04-13  
**Status**: Draft  
**Input**: User description: "I need the background screenshot service to be constantly running and providing all the requests with the latest screenshot instantly. The service has to run in a separate thread, so that request for the last screenshot would be completed instantly if a last screenshot is available. All of the requests for screenshot currently being performed directly via ADB have to be rerouted to this service. The service has to run whenever an active session exists and the actual FPS or seconds per frame, depending on what is >1 has to be reported on the execution tab in UI where running sessions are displayed."

## Clarifications

### Session 2026-04-13

- Q: When multiple sessions target different devices, should the background capture service capture from all devices or only the primary session's device? → A: Per-session capture — run an independent capture loop per active session/device.
- Q: How should consumers resolve which session's cached frame to retrieve? → A: Use the browser's cached/selected session as the default; server-side consumers use the session their execution is bound to; explicit session ID override is supported.
- Q: Should consumers be informed when the cached frame is stale? → A: No staleness policy — always return the last captured frame regardless of age, with no indication or threshold.
- Q: Should the capture interval be a single global configuration value or configurable per session? → A: Single global capture interval shared by all session capture loops.
- Q: What format should the background service cache the frame in? → A: Both PNG bytes and decoded Bitmap — each consumer gets the format it needs with zero conversion.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Instant Screenshot Access for Internal Consumers (Priority: P1)

As the system (command executor, trigger evaluator, image detection pipeline), when I request the latest emulator screenshot, I receive the most recently captured frame instantly from an in-memory cache instead of waiting for a synchronous ADB screencap round-trip.

**Why this priority**: This is the core value of the feature — eliminating per-request ADB latency for every screenshot consumer. All other stories depend on the background capture loop existing and serving cached frames.

**Independent Test**: Can be fully tested by starting a session, waiting for the background loop to capture at least one frame, then requesting a screenshot and verifying it returns a non-null image without invoking ADB directly.

**Acceptance Scenarios**:

1. **Given** an active emulator session exists and the background capture loop has captured at least one frame, **When** any internal consumer requests the latest screenshot, **Then** the consumer receives the most recent cached frame without triggering a new ADB screencap call.
2. **Given** an active emulator session exists but the background capture loop has not yet completed its first capture, **When** a consumer requests the latest screenshot, **Then** the system returns null (no frame available yet) rather than blocking.
3. **Given** no active emulator session exists, **When** a consumer requests the latest screenshot, **Then** the system returns null.

---

### User Story 2 - Automatic Lifecycle Management (Priority: P1)

As the system, when an emulator session starts, the background screenshot capture loop automatically begins. When the last active session stops, the capture loop automatically stops to avoid wasting resources.

**Why this priority**: Without lifecycle management the capture loop either never starts or runs indefinitely, so this is co-equal with P1.

**Independent Test**: Can be tested by starting a session and observing that the capture loop is running (frame count > 0), then stopping the session and observing that the capture loop ceases within 2 seconds.

**Acceptance Scenarios**:

1. **Given** no active sessions exist and the capture loop is idle, **When** a new session is started, **Then** the background capture loop begins capturing frames within 1 second.
2. **Given** one or more active sessions exist, **When** the last active session is stopped, **Then** the background capture loop stops within 2 seconds and releases resources.
3. **Given** multiple sessions are running and one is stopped, **When** at least one other session remains active, **Then** the capture loop continues running uninterrupted.

---

### User Story 3 - ADB Screenshot Consumers Rerouted (Priority: P2)

As a developer, all existing code paths that previously called ADB screencap directly (the `IScreenSource` implementation, the emulator screenshot HTTP endpoint, and any other direct ADB screenshot calls) now retrieve the latest frame from the background screenshot service instead.

**Why this priority**: This delivers the integration benefit — once the cached service exists (P1), existing consumers must be rewired to use it, otherwise the feature provides no end-to-end value.

**Independent Test**: Can be tested by running a command that uses image detection, verifying the detection completes successfully, and confirming through logging or metrics that zero direct ADB screencap calls were made during the detection.

**Acceptance Scenarios**:

1. **Given** the background capture loop is running, **When** the image detection pipeline requests a screenshot via `IScreenSource`, **Then** it receives the cached frame from the background service.
2. **Given** the background capture loop is running, **When** the emulator screenshot HTTP endpoint is called, **Then** it returns the cached frame from the background service.
3. **Given** the background capture loop is not running (no session), **When** the emulator screenshot HTTP endpoint is called, **Then** it returns an appropriate "emulator unavailable" error.

---

### User Story 4 - Capture Rate Displayed in Execution UI (Priority: P3)

As a user viewing the Execution tab in the web UI, I can see the current capture rate metric for each running session. If the rate is above 1 frame per second the metric is displayed as FPS (e.g., "2.5 FPS"). If the rate is below 1 frame per second, it is displayed as seconds per frame (e.g., "1.8 s/frame").

**Why this priority**: This is a display/observability enhancement that depends on the capture loop already being operational (P1/P2).

**Independent Test**: Can be tested by starting a session, navigating to the Execution tab, and verifying the capture rate metric appears with a reasonable value and correct unit.

**Acceptance Scenarios**:

1. **Given** a session is running and the background capture loop is active, **When** the user views the Execution tab, **Then** a capture rate metric is displayed alongside the session information.
2. **Given** the capture loop is producing frames faster than 1 per second, **When** the metric is displayed, **Then** the unit shown is "FPS" (e.g., "3.2 FPS").
3. **Given** the capture loop is producing frames slower than 1 per second, **When** the metric is displayed, **Then** the unit shown is "s/frame" (e.g., "2.1 s/frame").
4. **Given** the capture loop has not yet completed its first capture, **When** the user views the Execution tab, **Then** the capture rate metric shows a placeholder such as "—" or "Initializing…".

---

### Edge Cases

- What happens when the emulator device disconnects mid-capture? The capture loop should log the failure, keep running, and retry on the next cycle. Consumers receive the last successfully captured frame (which may be stale) until a new frame succeeds or the session is stopped.
- What happens when multiple sessions target different devices? The service runs an independent capture loop per active session, each capturing from its own bound device. Starting or stopping one session's capture loop does not affect others.
- What happens if ADB screencap consistently fails? The capture loop continues retrying at its configured interval. The cached frame remains the last successful capture. The FPS metric drops toward zero, making the problem visible to the user in the Execution UI.
- What happens when frame capture takes longer than the configured capture interval? The next capture should start immediately after the current one completes (no overlapping captures). The effective rate will be lower than the target, reflected accurately in the FPS metric.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST run an independent background capture loop per active emulator session, each on its own dedicated thread, continuously capturing screenshots from that session's bound ADB device.
- **FR-002**: System MUST store the most recently captured frame in memory in both PNG byte array and decoded Bitmap formats, so that any consumer can retrieve its needed format instantly without conversion or waiting for ADB.
- **FR-003**: System MUST automatically start the background capture loop when an emulator session transitions to the Running state, and stop it when no Running sessions remain.
- **FR-004**: System MUST reroute all existing `IScreenSource` consumers to retrieve the cached frame from the background capture service instead of calling ADB directly. Server-side consumers (command executors, trigger evaluators) use the session their execution context is bound to.
- **FR-005**: System MUST reroute the emulator screenshot HTTP endpoint to serve the cached frame from the background capture service. The endpoint accepts an optional session ID parameter; when omitted, it defaults to the session currently selected in the client (passed by the browser from its cached session context).
- **FR-006**: System MUST track the actual capture rate (frames captured per second) and expose it via an endpoint or as part of session status data.
- **FR-007**: System MUST display the capture rate on the Execution tab in the web UI for each running session, using "FPS" when the rate is ≥ 1, and "s/frame" when the rate is < 1.
- **FR-008**: System MUST handle ADB capture failures gracefully — the capture loop retries on the next cycle without crashing, and consumers receive the last successful frame (or null if none exists).
- **FR-009**: System MUST not perform overlapping captures — if a capture takes longer than the target interval, the next capture starts immediately after completion rather than concurrently.
- **FR-010**: System MUST release screenshot memory and stop the capture thread when the session ends to avoid resource leaks.

### Key Entities

- **Background Capture Loop**: A continuously running process that captures screenshots at a regular interval for a single session. One loop exists per active session. All loops share a single global capture interval setting. Key attributes: target capture interval (global), active/stopped state, bound device serial, owning session ID.
- **Cached Frame**: The most recently captured screenshot held in memory per session. Key attributes: PNG byte array, decoded Bitmap, timestamp of capture, dimensions. No staleness expiry — the frame is always served regardless of age. Both formats are cached to avoid per-read conversion overhead.
- **Capture Rate Metric**: The measured speed of the capture loop. Key attributes: frames per second (float), last N capture durations for rolling average.

## Assumptions

- The system runs an independent capture loop per active session. Each loop captures from its session's bound device. Server-side consumers resolve frames via their execution-bound session; HTTP consumers default to the browser's cached/selected session when no explicit session ID is provided.
- The default capture interval is a single global configuration value (e.g., ~500ms) that balances freshness with ADB overhead. All per-session capture loops share this interval. The value can be tuned via configuration.
- The capture rate metric uses a rolling window (e.g., last 10 captures) to smooth out individual frame timing jitter.
- All consumers currently using `IScreenSource.GetLatestScreenshot()` or the emulator screenshot endpoint are the complete set of ADB screenshot consumers that need rerouting.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Screenshot consumers receive the latest frame in under 5 milliseconds (in-memory retrieval) instead of the current 200–1000ms ADB round-trip time.
- **SC-002**: The background capture loop starts within 1 second of a session entering the Running state and stops within 2 seconds of the last session ending.
- **SC-003**: Zero direct ADB screencap calls are made by screenshot consumers (detection, triggers, HTTP endpoint) while the background capture loop is active — all reads come from the cached frame.
- **SC-004**: The Execution tab displays a live capture rate metric that updates at least once every 2 seconds and accurately reflects the actual capture speed within 10% tolerance.
- **SC-005**: When ADB capture fails intermittently, consumers continue to receive the last successful frame without errors or crashes, and the capture loop recovers automatically on subsequent attempts.

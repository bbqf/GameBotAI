# Tasks: Background Screenshot Service

**Input**: Design documents from `/specs/034-background-screenshot-service/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-additions.md

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Shared domain models and configuration that all user stories depend on

- [ ] T001 [P] Create CachedFrame immutable record in src/GameBot.Domain/Sessions/CachedFrame.cs with PngBytes (byte[]), Bitmap (System.Drawing.Bitmap), Timestamp (DateTimeOffset), Width (int), Height (int) properties
- [ ] T002 [P] Create CaptureMetrics record in src/GameBot.Domain/Sessions/CaptureMetrics.cs with CaptureRateFps (double?), FrameCount (long), LastCaptureUtc (DateTimeOffset?) properties
- [ ] T003 Add GAMEBOT_CAPTURE_INTERVAL_MS configuration reading to src/GameBot.Service/Program.cs — parse env var with default 500, clamp minimum to 50ms, store in a named options or static config accessible by the capture service

---

## Phase 2: Foundational — Background Capture Service Core

**Purpose**: The BackgroundScreenCaptureService singleton that manages per-session capture loops. MUST be complete before any consumer rerouting or UI work.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T004 Create BackgroundScreenCaptureService class in src/GameBot.Emulator/Session/BackgroundScreenCaptureService.cs — singleton service with ConcurrentDictionary<string, SessionCaptureLoop> keyed by session ID; public methods: StartCapture(sessionId, deviceSerial), StopCapture(sessionId), GetCachedFrame(sessionId), GetCaptureMetrics(sessionId), StopAll(); implements IDisposable to cancel all loops on shutdown
- [ ] T005 Implement SessionCaptureLoop as a private/internal nested class or separate internal class within BackgroundScreenCaptureService — runs Task.Run(LongRunning) loop: capture via AdbScreenSource (injected internally, not DI-resolved for consumers) → store both PNG bytes and decoded Bitmap in CachedFrame so consumers choose their preferred format → atomic swap via Volatile.Write → compute rolling FPS (circular buffer of last 10 capture durations) → delay remainder of interval → repeat; handles CancellationToken for graceful stop; logs capture failures and continues; disposes old Bitmap after swap
- [ ] T006 Write unit tests for BackgroundScreenCaptureService in tests/unit/BackgroundScreenCaptureServiceTests.cs — test StartCapture creates a loop and begins capturing (use a fake/stub ADB provider); test StopCapture cancels the loop and disposes resources; test GetCachedFrame returns null before first capture then returns frame after capture completes; test GetCaptureMetrics returns correct rolling FPS; test concurrent reads do not block; test StopAll disposes all loops; test duplicate StartCapture for same sessionId stops old loop first; include a timing assertion that GetCachedFrame completes in under 5ms (SC-001 validation)
- [ ] T007 Write unit tests for CaptureMetrics rolling FPS calculation in tests/unit/CaptureMetricsTests.cs — test FPS computation with various capture durations; test rolling window evicts old samples correctly; test zero captures returns null/0; test single capture returns correct rate

**Checkpoint**: Background capture service is independently testable with fake ADB provider. Build and all tests must pass.

---

## Phase 3: User Story 1 + 2 — Instant Access & Lifecycle Management (Priority: P1) 🎯 MVP

**Goal**: When a session starts, a background capture loop automatically begins. Consumers can instantly retrieve the cached frame via IScreenSource. When the session stops, the loop stops and resources are released.

**Independent Test**: Start a session → verify capture loop starts and frame becomes available → request screenshot → verify non-null instant response → stop session → verify loop stops

### Implementation

- [ ] T008 Create BackgroundCaptureScreenSource class implementing IScreenSource in src/GameBot.Emulator/Session/BackgroundCaptureScreenSource.cs — constructor takes BackgroundScreenCaptureService + ISessionManager; GetLatestScreenshot() finds the first running session, calls GetCachedFrame(sessionId), returns a clone of the cached Bitmap (or null if no frame/session); Windows-only platform attribute
- [ ] T009 Modify SessionService.StartSession() in src/GameBot.Service/Services/SessionService.cs — after successful session creation, call BackgroundScreenCaptureService.StartCapture(sessionId, deviceSerial) to start the capture loop for the new session
- [ ] T010 Modify SessionService.StopSession() in src/GameBot.Service/Services/SessionService.cs — before or after stopping the session, call BackgroundScreenCaptureService.StopCapture(sessionId) to stop the capture loop and release resources
- [ ] T011 Modify SessionService.SyncFromSessionManager() in src/GameBot.Service/Services/SessionService.cs — in the removal step where sessions are evicted, also call BackgroundScreenCaptureService.StopCapture(sessionId) for each removed session to clean up orphaned capture loops
- [ ] T012 Register BackgroundScreenCaptureService as singleton in src/GameBot.Service/Program.cs — register before IScreenSource; inject ILogger<BackgroundScreenCaptureService> and the capture interval config; inject into SessionService constructor
- [ ] T013 Update IScreenSource DI registration in src/GameBot.Service/Program.cs — when ADB is enabled, replace the AdbScreenSource → CachedScreenSource chain with BackgroundCaptureScreenSource that reads from BackgroundScreenCaptureService; keep AdbScreenSource registered separately (non-DI or named) for internal use by the capture loop only
- [ ] T014 Write unit tests for BackgroundCaptureScreenSource in tests/unit/BackgroundCaptureScreenSourceTests.cs — test returns cached Bitmap clone when frame available; test returns null when no frame; test returns null when no running session; test does not call ADB directly
- [ ] T015 Write unit tests for SessionService lifecycle integration in tests/unit/SessionService/SessionServiceCaptureLifecycleTests.cs — test StartSession calls StartCapture; test StopSession calls StopCapture; test SyncFromSessionManager cleanup calls StopCapture for evicted sessions; use mocked BackgroundScreenCaptureService

**Checkpoint**: MVP complete — background capture loop runs per-session, IScreenSource consumers get instant cached frames, lifecycle is automatic. Build and all tests must pass.

---

## Phase 4: User Story 3 — ADB Consumers Rerouted (Priority: P2)

**Goal**: All existing code paths that called ADB screencap directly now use the background service cache. Zero direct ADB screenshot calls from consumers while the loop is active.

**Independent Test**: Execute a command with image detection → verify detection succeeds using cached frame → verify no direct ADB screencap calls were made by consumers

### Implementation

- [ ] T016 [US3] Modify EmulatorImageEndpoints GET /emulator/screenshot in src/GameBot.Service/Endpoints/EmulatorImageEndpoints.cs — add optional string? sessionId query parameter; read cached PNG bytes from BackgroundScreenCaptureService.GetCachedFrame(sessionId) instead of calling ISessionManager.GetSnapshotAsync(); if sessionId is omitted, resolve from first running session; if no cached frame available, return 503 with existing error format; still store in CaptureSessionStore for crop operations
- [ ] T017 [US3] Verify AdbScreenSource is no longer in the consumer DI chain — confirm that IScreenSource resolves to BackgroundCaptureScreenSource (from T013); ensure AdbScreenSource is only used internally by the capture loop; optionally add an integration test in tests/integration/ that resolves IScreenSource from DI and asserts it is BackgroundCaptureScreenSource type
- [ ] T018 [US3] Write integration test for rerouted screenshot endpoint in tests/integration/EmulatorImageEndpointsCaptureTests.cs — start a test server with background capture service (stub ADB), request GET /emulator/screenshot, verify PNG response, verify the response came from the cached frame not from a direct ADB call

**Checkpoint**: All ADB screenshot consumers are rerouted. Build and all tests must pass.

---

## Phase 5: User Story 4 — Capture Rate Displayed in Execution UI (Priority: P3)

**Goal**: The Execution tab shows a live capture rate metric (FPS or s/frame) for each running session.

**Independent Test**: Start a session → navigate to Execution tab → verify FPS metric appears with correct formatting

### Backend

- [ ] T019 [P] [US4] Add CaptureRateFps property (double?) to RunningSessionDto in src/GameBot.Service/Models/Sessions.cs
- [ ] T020 [P] [US4] Add CaptureRateFps property (double?) to RunningSession domain model in src/GameBot.Domain/Sessions/RunningSession.cs
- [ ] T021 [US4] Modify SessionService to populate CaptureRateFps in src/GameBot.Service/Services/SessionService.cs — in ToRunning() and SyncFromSessionManager() update step, call BackgroundScreenCaptureService.GetCaptureMetrics(sessionId) and set CaptureRateFps on the RunningSession/DTO

### Frontend

- [ ] T022 [P] [US4] Add captureRateFps field to RunningSessionDto type in src/web-ui/src/services/sessionsApi.ts — add optional `captureRateFps?: number | null` property
- [ ] T023 [US4] Display capture rate metric in running session rows in src/web-ui/src/pages/Execution.tsx — in the runningSessions.map() list, add a span showing formatted capture rate: if captureRateFps >= 1, show "{fps.toFixed(1)} FPS"; if captureRateFps > 0 && < 1, show "{(1/fps).toFixed(1)} s/frame"; if null/undefined/0, show "—"

### Tests

- [ ] T024 [P] [US4] Write unit test for capture rate formatting logic in src/web-ui/src/__tests__/captureRate.spec.ts — test FPS formatting (>=1 shows FPS), s/frame formatting (<1), null/zero shows dash
- [ ] T025 [US4] Write contract test verifying RunningSessionDto includes captureRateFps in tests/contract/ — verify GET /api/sessions/running response schema includes the new nullable field

**Checkpoint**: Capture rate metric visible in UI. Build, backend tests, and frontend tests must pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, cleanup, and documentation

- [ ] T026 [P] Run full build and test suite (`dotnet build -c Debug && dotnet test -c Debug`) — verify zero warnings, all tests pass, no regressions from the 454 existing tests
- [ ] T027 [P] Run web-ui tests (`npx jest --coverage` in src/web-ui) — verify frontend tests pass including new capture rate tests
- [ ] T028 Run quickstart.md validation in specs/034-background-screenshot-service/quickstart.md — manually verify the end-to-end flow: start service, start session, check FPS in UI, verify screenshot endpoint returns cached frame
- [ ] T029 [P] Add CHANGELOG.md entry for the background screenshot service feature — document user-visible change: capture rate metric in Execution tab

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 (CachedFrame, CaptureMetrics models)
- **Phase 3 (US1+US2)**: Depends on Phase 2 (BackgroundScreenCaptureService must exist)
- **Phase 4 (US3)**: Depends on Phase 3 (DI wiring and lifecycle hooks must be in place)
- **Phase 5 (US4)**: Depends on Phase 3 (capture metrics must be flowing); can run in parallel with Phase 4
- **Phase 6 (Polish)**: Depends on all prior phases

### User Story Dependencies

- **US1+US2 (P1)**: Combined into one phase because they are tightly coupled (the capture loop IS the lifecycle). Can start after Phase 2
- **US3 (P2)**: Depends on US1+US2 completion (need the DI swap and working capture loops)
- **US4 (P3)**: Depends on US1+US2 (needs capture metrics flowing). Independent of US3 — can run in parallel with US3

### Within Each Phase

- Models/records before services (CachedFrame before BackgroundScreenCaptureService)
- Service implementation before DI registration
- DI registration before consumer rewiring
- Tests can be written in parallel with implementation when they are in separate files

### Parallel Opportunities

**Phase 1**: T001 and T002 can run in parallel (separate files)

**Phase 2**: T004 and T005 are sequential (T005 is nested within T004); T006 and T007 can run in parallel after T004+T005

**Phase 3**: T008 can start once T004 is done; T009–T011 are sequential modifications to the same file; T014 and T015 can run in parallel after implementation tasks

**Phase 4**: T016–T018 are sequential (endpoint change → verify → test)

**Phase 5**: T019, T020, T022 can run in parallel (different files); T023 depends on T022; T024 can run in parallel with T023; T025 depends on T019

**Phase 6**: T026, T027, T029 can run in parallel

---

## Implementation Strategy

### MVP First (Phase 1 + 2 + 3)

1. Complete Phase 1: Setup models (CachedFrame, CaptureMetrics)
2. Complete Phase 2: Core service + tests
3. Complete Phase 3: DI wiring, lifecycle hooks, IScreenSource swap
4. **STOP and VALIDATE**: Background capture loop runs, IScreenSource returns cached frame, session start/stop triggers loop start/stop
5. This is a deployable MVP — consumers get instant screenshots

### Incremental Delivery

1. Setup + Foundational + US1/US2 → MVP (instant cached screenshots)
2. Add US3 → All consumers rerouted (full integration)
3. Add US4 → FPS metric visible in UI (observability)
4. Polish → Full validation and documentation

---

## Notes

- US1 and US2 are combined in Phase 3 because they are inseparable — the capture loop (US1) needs lifecycle management (US2) to function, and lifecycle management has no meaning without the capture loop
- The existing CachedScreenSource TTL wrapper becomes unnecessary after this feature and can be removed from the DI chain (handled in T013)
- AdbScreenSource remains in the codebase for use by the background capture loop internally but is no longer registered as the DI-resolved IScreenSource for consumers
- The capture interval config (GAMEBOT_CAPTURE_INTERVAL_MS) is global, not per-session, per spec clarification

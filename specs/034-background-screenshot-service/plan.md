# Implementation Plan: Background Screenshot Service

**Branch**: `034-background-screenshot-service` | **Date**: 2025-04-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/034-background-screenshot-service/spec.md`

## Summary

Replace the synchronous ADB-screencap-on-demand model with a background capture service that runs a dedicated capture loop per active emulator session. The service caches the latest frame in both PNG and Bitmap formats so that all consumers (`IScreenSource`, HTTP endpoints) receive instant in-memory responses. Capture rate metrics (FPS / s-per-frame) are exposed via the sessions API and displayed in the Execution tab UI.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend); TypeScript ES2020 / React 18 (frontend)  
**Primary Dependencies**: ASP.NET Core Minimal API, SharpAdbClient/ADB integration (via existing `AdbClient`), System.Drawing (Bitmap), existing `ISessionManager`, `IScreenSource`, `CachedScreenSource` infrastructure  
**Storage**: In-memory only (no persistence); session-scoped cached frames held in `ConcurrentDictionary`  
**Testing**: xUnit + coverlet (backend); Jest + React Testing Library (frontend)  
**Target Platform**: Windows (System.Drawing + ADB); web UI served via Vite  
**Project Type**: Web service + desktop app + web UI  
**Performance Goals**: <5ms screenshot retrieval (in-memory); capture loop targets ~2 FPS (configurable interval, default 500ms); zero direct ADB calls from consumers while loop is active  
**Constraints**: One capture thread per active session; no overlapping captures; Bitmap and PNG byte[] dual-cache; global capture interval shared across all loops  
**Scale/Scope**: 1–3 concurrent sessions typical; single machine

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Gate | Status | Notes |
|------|--------|-------|
| **I. Code Quality** | PASS | New service class will be <50 LOC per method; public API has docstrings; no new dependencies |
| **II. Testing Standards** | PASS | Unit tests for capture loop lifecycle, caching, metric calculation; integration test for DI wiring; ≥80% line coverage target |
| **III. UX Consistency** | PASS | FPS metric follows existing StatusChip/session-row pattern; error messages are actionable; API additions are backward-compatible (optional `sessionId` param) |
| **IV. Performance** | PASS | Performance goals declared: <5ms retrieval, loop start <1s. Perf note required for hot-path changes (IScreenSource swap) |
| **Quality Gates / DoD** | PASS | No underscores in method names; lint/format pass; changelog entry needed for user-visible FPS metric |
| **Build/Test gate** | PASS | Verified: build succeeded (0 warnings, 0 errors); 454 tests pass (239 unit, 53 contract, 162 integration) |

### Post-Phase-1 Constitution Re-Check

| Gate | Status | Notes |
|------|--------|-------|
| **I. Code Quality** | PASS | Design uses <50 LOC methods; public APIs documented; no new dependencies; no dead code |
| **II. Testing Standards** | PASS | Test plan: unit tests for capture loop (lifecycle, caching, metrics), integration tests for DI wiring; ≥80% coverage target |
| **III. UX Consistency** | PASS | Additive API field (backward-compatible); UI follows existing session row layout; error messages unchanged |
| **IV. Performance** | PASS | Goals declared: <5ms read, loop start <1s. Lock-free read path via volatile reference swap |
| **Build/Test gate** | PASS | Re-verified post-design: 0 warnings, 454 tests green |

## Project Structure

### Documentation (this feature)

```text
specs/034-background-screenshot-service/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-additions.md # New/modified API endpoints
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Sessions/
│       ├── CachedFrame.cs                 # NEW: immutable cached frame record
│       ├── CaptureMetrics.cs              # NEW: capture rate metric model
│       └── RunningSession.cs              # MODIFIED: add CaptureRateFps property
├── GameBot.Emulator/
│   └── Session/
│       ├── BackgroundScreenCaptureService.cs  # NEW: per-session background capture loop
│       └── BackgroundCaptureScreenSource.cs   # NEW: IScreenSource backed by capture service
├── GameBot.Service/
│   ├── Models/
│   │   └── Sessions.cs                    # MODIFIED: add captureRate to RunningSessionDto
│   ├── Services/
│   │   └── SessionService.cs              # MODIFIED: wire capture metrics into RunningSession
│   ├── Endpoints/
│   │   └── EmulatorImageEndpoints.cs      # MODIFIED: serve from background service cache
│   └── Program.cs                         # MODIFIED: DI registration for background service
└── web-ui/
    └── src/
        ├── services/
        │   └── sessionsApi.ts             # MODIFIED: add captureRate field to DTO
        └── pages/
            └── Execution.tsx              # MODIFIED: display FPS metric in session rows

tests/
├── unit/
│   ├── BackgroundScreenCaptureServiceTests.cs  # NEW
│   ├── BackgroundCaptureScreenSourceTests.cs   # NEW
│   ├── CaptureMetricsTests.cs                  # NEW
│   └── SessionService/
│       └── SessionServiceCaptureLifecycleTests.cs  # NEW
└── integration/
    └── EmulatorImageEndpointsCaptureTests.cs   # NEW
```

**Structure Decision**: Follows existing multi-project structure (Domain/Emulator/Service/web-ui). New code lives in existing directories. One new service class in Emulator, one new model in Domain. No new projects.

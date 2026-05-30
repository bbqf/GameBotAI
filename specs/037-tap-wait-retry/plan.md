# Implementation Plan: Tap Wait-and-Retry Before Execution

**Branch**: `036-tap-wait-retry` | **Date**: 2025-04-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/036-tap-wait-retry/spec.md`

## Summary

Extend the primitive tap step in `CommandExecutor` with a configurable wait-and-retry loop before execution. Currently, detection is a single-shot attempt — if the reference image is not visible at the exact moment of execution, the step fails. This feature adds a retry loop that waits for the screenshot capture interval, checks for the image, and retries up to a configurable count with optional progressive backoff. Three configuration parameters are introduced: capture interval (base WAIT_TIME, existing env var `GAMEBOT_CAPTURE_INTERVAL_MS` promoted to `AppConfig`), retry count (COUNT, default 3), and wait progression multiplier (PROGRESSION, default 1).

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: ASP.NET Core Minimal API, OpenCvSharp (TemplateMatcher), SharpAdbClient/ADB integration, Microsoft.Extensions.Logging, existing `GameBot.Domain` and `GameBot.Emulator` libraries  
**Storage**: Existing file-backed JSON repositories under `data/` (no new stores); configuration via `AppConfig` singleton  
**Testing**: xUnit + coverlet for coverage enforcement; existing unit (266), contract (54), integration (169) tests all passing  
**Target Platform**: Windows (System.Drawing, ADB, OpenCvSharp dependencies are Windows-only)  
**Project Type**: Desktop service (ASP.NET Core Minimal API host + web UI)  
**Performance Goals**: Retry loop adds at most `WAIT_TIME × (COUNT + 1)` latency (PROGRESSION=1) or `WAIT_TIME × (1 + (PROGRESSION^COUNT − 1) / (PROGRESSION − 1))` (PROGRESSION > 1), including the initial wait; each detection check < 50ms (existing pipeline); cancellation response < 100ms  
**Constraints**: Must be immediately cancellable via CancellationToken; global config only (no per-step overrides); no new external packages  
**Scale/Scope**: ~3 new config properties on `AppConfig`, ~80-120 LOC retry loop in `CommandExecutor`, ~5-8 new unit tests, ~3-5 new integration tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: Build and test runs are passing (verified: 0 warnings, 0 errors, 489/489 tests green).

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code Quality Discipline | PASS | Feature adds <120 LOC to existing service; no new projects/dependencies; modular retry logic encapsulated in a single method |
| II. Testing Standards | PASS (plan) | New unit tests for retry loop (success on 1st try, success on Nth try, failure after COUNT, cancellation, progression math, edge cases COUNT=0, invalid PROGRESSION). Integration tests for end-to-end retry with mock screenshot source. Coverage targets ≥80% line / ≥70% branch for new code. |
| III. User Experience Consistency | PASS | Config parameters follow existing env-var + AppConfig pattern; log messages follow existing structured logging; step outcome states extend existing PrimitiveTapStepOutcome vocabulary ("executed", "skipped_detection_failed" → + "cancelled") |
| IV. Performance Requirements | PASS | Worst-case latency is bounded and documented in spec (SC-005); no N+1 patterns; each retry reuses cached screenshot (no extra ADB calls); cancellation aborts immediately |
| Quality Gates – DoD | PASS (plan) | No underscores in method names; all public APIs documented; config parameters documented in code and config file; changelog entry planned |

## Project Structure

### Documentation (this feature)

```text
specs/036-tap-wait-retry/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (API endpoint changes)
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Config/
│       └── AppConfig.cs              # + TapRetryCount, TapRetryProgression, CaptureIntervalMs properties
├── GameBot.Emulator/
│   └── Session/
│       └── BackgroundScreenCaptureService.cs  # No changes (captures at existing interval)
└── GameBot.Service/
    ├── Program.cs                     # + Wire new AppConfig properties from env vars
    └── Services/
        └── CommandExecutor.cs         # + Retry loop in PrimitiveTap handling

tests/
├── unit/
│   └── Commands/
│       └── CommandExecutorPrimitiveTapTests.cs  # + Retry loop unit tests
└── integration/
    └── Commands/
        └── PrimitiveTapExecutionIntegrationTests.cs  # + Retry integration tests
```

**Structure Decision**: All changes fit within existing project boundaries. No new projects or directories needed. The retry logic is added to the existing `CommandExecutor.PrimitiveTap` handling path in `GameBot.Service`, with configuration properties added to the existing `AppConfig` class in `GameBot.Domain`.

## Complexity Tracking

No constitution violations. All changes are within existing project structure and follow established patterns.

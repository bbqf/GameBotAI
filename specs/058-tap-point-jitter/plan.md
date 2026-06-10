# Implementation Plan: Tap-Point Jitter

**Branch**: `058-tap-point-jitter` | **Date**: 2026-06-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/058-tap-point-jitter/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Apply a small, automatic random offset (default ±5px, independently per axis) to the X/Y coordinates of every tap and swipe endpoint immediately before it is dispatched to the device. Jitter is injected centrally in `SessionManager.SendInputsAsync` (the single ADB dispatch chokepoint for both `InputAction.Type == "tap"`-shaped and `"swipe"`-shaped actions), so it applies uniformly regardless of whether coordinates were authored directly, computed via image detection, or replayed from a recording. The jitter radius is a new `AppConfig.TapJitterRadiusPx` property (default 5, 0 disables, negative falls back to default), wired through the existing config precedence chain (defaults → `data/config/config.json` → `GAMEBOT_*`/`Service__*` env vars) and surfaced in the generic UI Configuration variables list via `ConfigSnapshotService.BuildDefaultRelevantKeys()` — no new UI controls. Because `InputAction.Args` is a mutable `Dictionary<string,object>` passed by reference, `SessionManager` mutates the dispatched coordinates in place so callers (`CommandExecutor`) can read back the post-jitter values and report both the pre-jitter target and post-jitter executed coordinates in step outcomes and execution logs (additive fields on `PrimitiveTapStepOutcome`).

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core Minimal API, SharpAdbClient/ADB integration (`GameBot.Emulator`), Microsoft.Extensions.Logging, existing `GameBot.Domain` and `GameBot.Service` libraries
**Storage**: Existing file-backed JSON configuration (`data/config/config.json`) via `ConfigSnapshotService`; no new stores
**Testing**: xUnit + coverlet for coverage enforcement; existing unit/contract/integration suites all passing
**Target Platform**: Windows (System.Drawing, ADB, SessionManager dependencies are Windows-only)
**Project Type**: Desktop service (ASP.NET Core Minimal API host + web UI)
**Performance Goals**: Jitter computation (LCG-based RNG, two subtractions/additions/clamps per coordinate) adds negligible (<0.01ms) overhead per tap/swipe dispatch; no additional ADB calls, no additional allocations beyond a few ints
**Constraints**: No new external packages; jitter MUST be applied with no per-step opt-in/opt-out (FR-012); jittered coordinates MUST never be negative (FR-007); radius=0 MUST exactly disable jitter (FR-005)
**Scale/Scope**: ~1 new config property on `AppConfig`, ~1 new shared jitter-offset helper (non-cryptographic LCG, mirroring `SequenceRunner`'s pattern), ~30-50 LOC change in `SessionManager.SendInputsAsync`, additive fields on `PrimitiveTapStepOutcome` + `ExecutionLogService` + DTOs, ~6-10 new unit tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code Quality Discipline | PASS | Feature adds a small jitter helper plus localized changes to `SessionManager.SendInputsAsync`, `AppConfig`, `IConfigApplier`, `ConfigSnapshotService`, `Program.cs`, `CommandExecutor`, `ExecutionLogService`, and DTOs; no new projects/dependencies; all changes additive (new optional record fields, new config property) |
| II. Testing Standards | PASS (plan) | New unit tests for: jitter helper bounds/clamping (radius=0 passthrough, radius=N stays within [-N,N], negative-result clamped to 0), `AppConfig`/`IConfigApplier` validation (default 5, 0 valid, negative→default), `SessionManager` jitter application to tap and swipe args, `CommandExecutor` outcome population of `ExecutedPoint`/`TargetSwipe`/`ExecutedSwipe`. Coverage targets ≥80% line / ≥70% branch for touched areas. Existing `CommandExecutor` unit tests using `RecordingSessionManager` fakes are unaffected since jitter only applies in the real `SessionManager`. |
| III. User Experience Consistency | PASS | New config parameter follows existing env-var + `AppConfig` + `ConfigSnapshotService` pattern (same as `TapRetryCount`/`AdbRetries`); execution log/step outcome vocabulary extended additively (`ExecutedPoint`, `TargetSwipe`, `ExecutedSwipe`) without breaking existing `ResolvedPoint` consumers; no new UI controls per FR-011 |
| IV. Performance Requirements | PASS | Jitter adds O(1) negligible-cost arithmetic per coordinate on the existing dispatch path; no new I/O, no new ADB round-trips, no N+1 patterns |
| Quality Gates – DoD | PASS (plan) | No underscores in method names (CamelCase); new config parameter documented in `ENVIRONMENT.md` and surfaced in UI config list; all touched public members documented |

## Project Structure

### Documentation (this feature)

```text
specs/058-tap-point-jitter/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Config/
│   │   └── AppConfig.cs                  # + TapJitterRadiusPx property (default 5)
│   └── Services/
│       └── CoordinateJitter.cs           # NEW: shared LCG-based jitter offset helper (mirrors SequenceRunner RNG pattern)
├── GameBot.Emulator/
│   └── Session/
│       └── SessionManager.cs             # SendInputsAsync normalization pass (at method entry, before the ADB-mode branch) applies jitter to tap (x,y) and swipe (x1,y1,x2,y2) args in place, clamps to >= 0
└── GameBot.Service/
    ├── Program.cs                         # + Wire GAMEBOT_TAP_JITTER_RADIUS_PX env var into AppConfig
    └── Services/
        ├── IConfigApplier.cs               # + Apply TapJitterRadiusPx with negative->default(5) fallback (file contains the ConfigApplier implementation)
        ├── ConfigSnapshotService.cs        # + GAMEBOT_TAP_JITTER_RADIUS_PX in BuildDefaultRelevantKeys()
        ├── ICommandExecutor.cs             # + PrimitiveSwipePoints record; + ExecutedPoint/TargetSwipe/ExecutedSwipe on PrimitiveTapStepOutcome
        ├── CommandExecutor.cs              # Read back jittered coordinates from InputAction.Args after SendInputsAsync; populate new outcome fields
        ├── Endpoints/CommandsEndpoints.cs  # + map new outcome fields to ResolvedPointDto-style DTOs
        ├── Endpoints/StepsEndpoints.cs     # + include new fields in step result payload
        ├── Models/Commands.cs              # + DTO additions (additive, optional)
        └── Services/ExecutionLog/ExecutionLogService.cs  # + include target vs executed coordinates in detail text

ENVIRONMENT.md                              # + GAMEBOT_TAP_JITTER_RADIUS_PX documentation entry

tests/
└── unit/
    ├── Config/AppConfigValidationTests.cs           # + TapJitterRadiusPx default/zero/negative tests
    ├── Domain/CoordinateJitterTests.cs              # NEW: bounds, clamping, disable-at-zero tests
    ├── Emulator/SessionManagerJitterTests.cs        # NEW: jitter applied to tap/swipe args, clamped, disabled at radius=0
    └── Commands/CommandExecutorPrimitiveTapTests.cs # + ExecutedPoint/TargetSwipe/ExecutedSwipe outcome tests
```

**Structure Decision**: All changes fit within the existing `GameBot.Domain` / `GameBot.Emulator` / `GameBot.Service` / `tests` project layout. The jitter-offset computation lives in `GameBot.Domain` (alongside `AppConfig` and `SequenceRunner`, which already hosts the equivalent non-cryptographic RNG pattern) so it can be referenced from `GameBot.Emulator` (which already depends on `GameBot.Domain` for `AppConfig`). The injection point is `SessionManager.SendInputsAsync`, the existing single dispatch chokepoint for all tap/swipe `InputAction`s, requiring no changes to `ISessionManager`'s public signature.

## Complexity Tracking

No constitution violations. All changes are within existing project structure and follow established configuration and randomization patterns.

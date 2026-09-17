# Research: Report Idle Pause in queue health

## R-001: Why `health.paused` is false during an idle pause

- **Finding**: `QueuesEndpoints.ProjectHealth` sets `Paused = handle.IsPolicyPaused`, `PausedAt = handle.PolicyPausedAt` and `PauseReason = handle.PauseReason`, all from the failure-policy register (feature 087). A code comment there deliberately avoids `IdlePausedUntil` "so a routine idle gap is not reported as a failure-policy park". `QueueMonitorService.BuildCurrent` reports `ScheduleKind.IdlePause` from `handle.IdlePausedUntil`. The two endpoints read different registers, and the `Paused` XML doc ("parked by a tripped pause action") never made it into a clearly worded schema description.
- **Decision**: Widen `paused` to cover both pauses, and add `pauseKind` so the distinction feature 087 cared about stays visible without calling `/monitor`.
- **Rationale**: This is the issue's expected behaviour. `pauseKind` keeps the "don't conflate" intent machine-readable, and `/resume` keeps its policy-only semantics.
- **Alternatives considered**: (a) Documentation-only, saying `paused` is failure-policy-only: the workaround would stay, and the issue calls it the fallback. (b) Separate `idlePaused`/`idlePausedAt` fields: consumers would have to OR two flags to answer "is it paused", which is the exact misreading being fixed.

## R-002: Recording when an idle pause began

- **Finding**: `IdlePauseHoldAsync` calls `handle.EnterIdlePause(resumeAt)` once on entry and again on every poll tick to update the resume time, then `ClearIdlePause()` in its `finally`. The handle keeps only the resume instant.
- **Decision**: Add `_idlePausedAt` under `_idleLock`. `EnterIdlePause(resumeAt, at)` sets it only when the register goes from not-paused to paused, and `ClearIdlePause` nulls it. The run loop passes `_timeProvider.GetLocalNow()`.
- **Rationale**: FR-002 says `pausedAt` must stay stable across resume-time updates. Using the service's `TimeProvider` makes the value deterministic under `FakeTimeProvider` and consistent with the other local-clock `health` timestamps.
- **Alternatives considered**: A separate `BeginIdlePause` method: this adds an API that tick-updates could misuse. Defaulting `at` to `DateTimeOffset.Now`: this breaks fake-clock tests and hides the dependency.

## R-003: Where the combined projection lives

- **Decision**: `QueueRunHandle.SnapshotPause()` returns a `QueuePauseSnapshot` record struct `(bool Paused, DateTimeOffset? PausedAt, string? Reason, string? Kind)`. It reads the policy register under `_policyLock`, and the idle register under `_idleLock` only when no policy pause is in force. Kind constants are `QueuePauseKinds.Idle = "idle"` and `QueuePauseKinds.FailurePolicy = "failurePolicy"`.
- **Rationale**: The handle owns both invariants. A unit-testable method keeps `ProjectHealth` a plain mapping, and the monitor and health derive the idle state from the same `_idlePausedUntil` register (FR-008).
- **Alternatives considered**: Combining in the endpoint: the endpoint cannot unit-test precedence without a web host.

## R-004: Idle pause reason text

- **Decision**: `idle pause: resumes at {resumeAt:HH:mm}` formatted with `CultureInfo.InvariantCulture`, computed at snapshot time from the current resume instant.
- **Rationale**: This matches the clarification, and HH:mm is the monitor's own display granularity. Invariant culture satisfies CA1305 and keeps the text stable across host locales.

## R-005: Publishing the field descriptions

- **Finding**: `AddSwaggerGen` in `GameBotServiceSetup.cs` does not call `IncludeXmlComments`, so XML docs never reach `/swagger/v1/swagger.json`. Feature 094 solved the same problem with `Swagger/SequenceTimeLimitSchemaFilter.cs`, an `ISchemaFilter` that sets property descriptions by type. The `QueueHealthResponse` schema is generated because `SwaggerConfig` attaches `QueueDetailResponse` as the GET-queue 200 schema.
- **Decision**: Add `Swagger/QueueHealthSchemaFilter.cs` that describes `paused`, `pausedAt`, `pauseReason` and `pauseKind` on `QueueHealthResponse` (and sets `enum` values on `pauseKind`), and register it. Extend the GET `/api/queues/{id}` operation description in `SwaggerConfig.cs` with one sentence on pauses. Contract test `tests/contract/Queues/QueueHealthOpenApiTests.cs` follows `SequenceTimeLimitOpenApiTests`.
- **Alternatives considered**: Turning on `IncludeXmlComments` globally: it changes the whole published document and needs `GenerateDocumentationFile`, which is out of scope.

## R-006: Integration-test strategy

- **Finding**: A real idle pause needs a started run with a session. The integration host exposes `IQueueRunRegistry` (internal, with `InternalsVisibleTo` for integration tests) and `IQueueRuntimeStore`, and `TryGetLiveRun` needs `status == Running` plus a registered handle.
- **Decision**: The integration test creates a queue through the API, then resolves the registry and runtime store from `app.Services`, registers a hand-built `QueueRunHandle` in an idle pause, sets the status to Running, and asserts the `health` JSON on both `GET /api/queues/{id}` and `/monitor`. It also covers not-paused, cleared, and policy-precedence cases. It unregisters and sets the status back to Stopped in `finally` so the shared host is not polluted.
- **Rationale**: This exercises the real endpoint, serializer and schema field names without an emulator. Engine behaviour (start instant under a fake clock) is covered by the existing unit harness.

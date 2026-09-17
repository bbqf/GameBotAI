# Implementation Plan: Report Idle Pause in queue health

**Branch**: `096-idle-pause-health-paused` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/096-idle-pause-health-paused/spec.md` (issue #199)

## Summary

`health.paused`, `pausedAt` and `pauseReason` on `GET /api/queues/{id}` are projected only from the
failure-policy pause (`QueueRunHandle.IsPolicyPaused` / `PolicyPausedAt` / `PauseReason`), so an idle
pause, which `/monitor` reports from `QueueRunHandle.IdlePausedUntil`, reads as "not paused". The
fix gives the handle one pause projection, `SnapshotPause()`, that combines both pauses (failure
policy first) and returns `(Paused, PausedAt, Reason, Kind)`. The idle-pause register also records
when the continuous pause began (`IdlePausedAt`), which means `EnterIdlePause` now takes the current
instant. `ProjectHealth` reads the snapshot and fills a new `pauseKind` field. The service does not feed XML
comments to Swagger, so a new `QueueHealthSchemaFilter` publishes the pause-field descriptions (the
same pattern as `SequenceTimeLimitSchemaFilter`) and the GET-queue operation description mentions
pauses. XML docs, `docs/architecture.md`, CHANGELOG and STATUS are updated too.

## Technical Context

**Language/Version**: C# / .NET 9
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, Swashbuckle (descriptions via `ISchemaFilter`; XML comments are not included)
**Storage**: N/A (run state is in-memory on `QueueRunHandle`; nothing persisted)
**Testing**: xUnit + FluentAssertions: unit (`tests/unit`), integration (`tests/integration`), contract/OpenAPI (`tests/contract`)
**Target Platform**: Windows service
**Project Type**: web-service
**Performance Goals**: A health read takes at most two extra uncontended lock acquisitions; the idle-pause poll loop gains nothing but one null check inside the existing lock
**Constraints**: Idle-pause timing, `/monitor` shape, `/resume` semantics and `failurePolicyTripped` unchanged (FR-010); failure-policy pause values byte-identical (FR-005)
**Scale/Scope**: 7 source files (1 new), 6 test files (3 new), docs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Gate | Status | Notes |
|------|--------|-------|
| I. Code quality | PASS | One projection method on the handle rather than duplicated logic in the endpoint; CamelCase; XML docs on new members; invariant-culture formatting (CA1305) |
| II. Testing | PASS | Failing tests first: handle unit tests (precedence, start instant kept on re-entry, clears) and an engine unit test (pause start instant under a fake clock); integration test hitting `GET /api/queues/{id}` with a registered idle-paused handle; existing failure-policy integration test extended with `pauseKind` |
| III. UX consistency | PASS | `pauseKind` uses camelCase wire values in line with `failurePolicy`/`notifyAndStop`; reason text follows the `failure policy: …` prefix style |
| IV. Performance | PASS | Not a hot path; perf note in PR |
| V. Living docs | PASS | `docs/architecture.md` health field list + Last reviewed; `specs/STATUS.md` row; spec 087 Status → iterated by 096; CHANGELOG |

Post-design re-check: PASS. No new projects, no persistence, no violations. The one additive wire field is documented in [contracts/queue-health.md](contracts/queue-health.md).

## Project Structure

### Documentation (this feature)

```text
specs/096-idle-pause-health-paused/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/queue-health.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs          # IdlePausedAt, EnterIdlePause(resumeAt, at), SnapshotPause()
src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs   # pass _timeProvider.GetLocalNow() to EnterIdlePause
src/GameBot.Service/Endpoints/QueuesEndpoints.cs                       # ProjectHealth reads SnapshotPause, sets PauseKind
src/GameBot.Service/Contracts/Queues/QueueHealthResponse.cs            # PauseKind + rewritten pause XML docs
src/GameBot.Service/Swagger/QueueHealthSchemaFilter.cs                 # new: publishes pause-field descriptions
src/GameBot.Service/Swagger/SwaggerConfig.cs                           # GET queue operation description mentions pauses
src/GameBot.Service/GameBotServiceSetup.cs                             # register the schema filter

tests/unit/Queues/QueueRunHandlePauseTests.cs                          # new: snapshot semantics
tests/unit/Queues/QueueExecutionServiceTests.cs                        # idle-pause engine test asserts IdlePausedAt
tests/unit/Queues/QueueMonitorServiceTests.cs                          # EnterIdlePause call sites (new signature)
tests/integration/Queues/QueueHealthIdlePauseTests.cs                  # new: GET /api/queues/{id} while idle-paused
tests/integration/Queues/QueueFailurePolicyRunTests.cs                 # pause test also asserts pauseKind "failurePolicy"
tests/contract/Queues/QueueHealthOpenApiTests.cs                       # new: published descriptions

docs/architecture.md, specs/STATUS.md, specs/087-queue-failure-policy/spec.md, CHANGELOG.md
```

**Structure Decision**: Existing single-service layout; the pause projection lives on `QueueRunHandle` beside both pause registers so the endpoint cannot drift from the handle's invariants.

## Complexity Tracking

No violations.

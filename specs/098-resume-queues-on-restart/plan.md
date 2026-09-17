# Implementation Plan: Resume Queues After a Service Restart

**Branch**: `098-resume-queues-on-restart` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/098-resume-queues-on-restart/spec.md` (issue #203)

## Summary

A queue gains a persisted opt-in flag, `ExecutionQueue.ResumeOnServiceStart` (default `false`). A new
file-backed store, `IQueueRunStateStore` / `FileQueueRunStateStore` (`data/queue-run-state.json`),
durably records the ids of queues with a live run. `QueueExecutionService.StartAsync` records the id once
the run is about to launch, so an abrupt kill still leaves it recorded. `RunAsync`'s `finally` removes the
id unless the host is stopping (`IHostApplicationLifetime.ApplicationStopping` is cancelled), so an operator stop,
completion, run-level failure or failure-policy stop all forget it, and a service shutdown does not. A new
hosted service, `QueueResumeOnStartupService`, waits for `ApplicationStarted`, reads the recorded ids once,
and for each id: starts the queue through `IQueueExecutionService.StartAsync` when it exists and has the flag
on, otherwise discards the record. Each attempt is logged with its outcome. The flag is carried through
create/update/duplicate/get/list and the web UI queue form, and documented in `docs/architecture.md`.

## Technical Context

**Language/Version**: C# / .NET 9 (service), TypeScript + React (web-ui)
**Primary Dependencies**: ASP.NET Core minimal APIs, Microsoft.Extensions.Hosting (`BackgroundService`, `IHostApplicationLifetime`), System.Text.Json, Swashbuckle
**Storage**: JSON files under the service data root. New file `queue-run-state.json` at the data root (not under `queues/`, whose `*.json` files the queue repository enumerates as queues)
**Testing**: xUnit + FluentAssertions (`tests/unit`, `tests/integration`, `tests/contract`); Jest + Testing Library (`src/web-ui`)
**Target Platform**: Windows service
**Project Type**: web-service + web UI
**Performance Goals**: One small file write per queue start and per run end (not a hot path: starts/stops are operator-scale events). Startup resume adds one file read plus one queue lookup per recorded id; resumed queues are Running within 30 s of the service being ready (SC-001)
**Constraints**: Manual start/stop semantics unchanged (FR-013); a store failure must never fail a start or a run teardown (logged instead); queues stored before the feature read `resumeOnServiceStart: false` with no migration (FR-001)
**Scale/Scope**: ~9 source files (3 new), web-ui 4 files, ~6 test files (3 new), docs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Gate | Status | Notes |
|------|--------|-------|
| I. Code quality | PASS | Store behind an interface in Domain beside `IQueueRepository`; resume logic in its own hosted service with an `internal` testable `ResumeAsync`, mirroring `QueueDeviceWatchdogService`; CamelCase; XML docs on new public members; `LoggerMessage` source-generated logs |
| II. Testing | PASS | Tests first: store unit tests (add/remove/list, persistence across instances, corrupt file → list throws / writes heal it, concurrent writes); engine unit tests (start records id; completed/failed/stopped runs clear it; app-stopping keeps it); resume service unit tests (flag on → started; flag off → discarded; missing queue → discarded; device-in-use and throwing start → logged, others still resumed); integration test (host restart over the same data dir resumes an opted-in queue and not a stopped/non-opted one); contract test (field in OpenAPI, create/get round-trip); web-ui form test |
| III. UX consistency | PASS | camelCase `resumeOnServiceStart` in line with `pauseWhenIdle`; checkbox beside the idle-pause option with help text; log messages name the queue and outcome |
| IV. Performance | PASS | Not a hot path; perf note in PR |
| V. Living docs | PASS | `docs/architecture.md` queue section + persistence layout + Last reviewed; `specs/STATUS.md` row; CHANGELOG entry |

Post-design re-check: PASS. No new projects; one new persisted file, documented in [data-model.md](data-model.md) and `docs/architecture.md`.

## Project Structure

### Documentation (this feature)

```text
specs/098-resume-queues-on-restart/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/queue-resume-option.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/Queues/ExecutionQueue.cs                               # ResumeOnServiceStart
src/GameBot.Domain/Queues/IQueueRunStateStore.cs                          # new: MarkRunningAsync / ClearAsync / ListRunningAsync
src/GameBot.Domain/Queues/FileQueueRunStateStore.cs                       # new: data/queue-run-state.json, lock + atomic replace
src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs      # record on start, clear in finally unless app stopping
src/GameBot.Service/Hosted/QueueResumeOnStartupService.cs                 # new: resume pass after ApplicationStarted
src/GameBot.Service/GameBotServiceSetup.cs                                # register store + hosted service
src/GameBot.Service/Contracts/Queues/CreateQueueRequest.cs                # ResumeOnServiceStart
src/GameBot.Service/Contracts/Queues/UpdateQueueRequest.cs                # ResumeOnServiceStart
src/GameBot.Service/Contracts/Queues/QueueResponse.cs                     # ResumeOnServiceStart
src/GameBot.Service/Endpoints/QueuesEndpoints.cs                          # create/update/duplicate/BuildResponse/BuildDetailAsync

src/web-ui/src/services/queues.ts                                         # type fields
src/web-ui/src/components/queues/QueueForm.tsx                            # checkbox
src/web-ui/src/pages/QueuesPage.tsx                                       # emptyForm / edit / create / update
src/web-ui/src/components/queues/__tests__/QueueForm.test.tsx             # checkbox test

tests/unit/Queues/FileQueueRunStateStoreTests.cs                          # new
tests/unit/Queues/QueueExecutionServiceRunStateTests.cs                   # new
tests/unit/Queues/QueueResumeOnStartupServiceTests.cs                     # new
tests/unit/Queues/FileQueueRepositoryTests.cs                             # flag round-trip + legacy JSON reads false
tests/integration/Queues/QueueResumeOnRestartTests.cs                     # new: two hosts over one data dir
tests/contract/Queues/QueuesApiContractTests.cs                           # field in OpenAPI + create/get
tests/integration/Queues/QueuesDuplicateEndpointTests.cs                  # duplicate copies the flag

docs/architecture.md, specs/STATUS.md, CHANGELOG.md
```

**Structure Decision**: Existing single-service layout. The run-state store lives in `GameBot.Domain/Queues` beside the queue repository it complements; the resume pass is a hosted service in `GameBot.Service/Hosted` beside the device watchdog, and resolves the queue-execution graph lazily through `IServiceProvider` for the same reason the watchdog does.

## Complexity Tracking

No violations.

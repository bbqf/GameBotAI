# Implementation Plan: Distinct reporting of sequence time-limit cancellation

**Branch**: `094-sequence-time-limit-reporting` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/094-sequence-time-limit-reporting/spec.md` (GitHub issue #182)

## Summary

A queue firing runs under a watchdog in `QueueExecutionService.RunOneSequenceAsync`. The firing token is one linked
`CancellationTokenSource` combining the queue's stop token and `CancelAfter(bound)`. When it fires, the sequence's
execution-log entry is closed by `SequenceExecutionService.FinalizeAbandonedAsync` as `failure` with prose that cannot
separate "time bound" from "run stopped". Nothing structured records the bound.

**Approach** (see [research.md](research.md)):

1. **Ambient time-limit scope.** A new `SequenceTimeLimitScope` (AsyncLocal, in the same style as the feature-079 device
   context) is pushed by `RunOneSequenceAsync` around each firing. It carries the applied bound in ms, the
   **timer-only** token and the queue's stop token. `HasElapsed` = timer fired AND stop not requested. The watchdog now
   uses a dedicated timer CTS linked with the stop token, so the two causes can be told apart. The linked token passed
   down is unchanged in behaviour.
2. **Stamp at finalize.** Both finalize sites in `SequenceExecutionService` stamp `CancellationReason =
   "sequence_time_limit"` and `TimeLimitMs` on the `ExecutionLogContext` when the final status is not success and
   `SequenceTimeLimitScope.Current?.HasElapsed == true`. The sites are the normal end of `ExecuteCoreAsync`, which covers
   a step that swallowed the cancellation (FR-004a), and `FinalizeAbandonedAsync`. `LogSequenceFinalizeAsync` copies
   both values onto the entry. Only sequence finalizes stamp, so child command entries are untouched (FR-004b). Ad-hoc
   runs have no scope and are never stamped.
3. **Persist and project.** `ExecutionLogEntry` gains nullable `CancellationReason` and `TimeLimitMs`. Old files
   deserialize with nulls (FR-010). The two fields are exposed on `ExecutionLogEntryDto` (list), the detail response and
   `ExecutionTreeNodeDto` (subtree) (FR-005). Status vocabulary unchanged (FR-003).
4. **Effective bound read-out.** A new domain constant holder `SequenceTimeLimits` (Default 240000, Max 1800000,
   `Resolve(int?)`) replaces the private default in `QueueExecutionService` and the private max in
   `FileSequenceRepository`. `GET /api/sequences/{id}` (all three response shapes) adds `effectiveWatchdogTimeoutMs`.
   Writes never read it (FR-007/008).
5. **OpenAPI.** `[Range(1, 1800000)]` on the upsert/patch `WatchdogTimeoutMs` plus schema descriptions (default,
   maximum, meaning). A GET-sequence operation description names `effectiveWatchdogTimeoutMs`. The execution-log entry
   and tree-node schemas are registered in the document filter, with descriptions for `cancellationReason` (enum
   `sequence_time_limit`) and `timeLimitMs` (FR-009).

## Technical Context

**Language/Version**: C# / .NET 9 (GameBot.Domain, GameBot.Service)  
**Primary Dependencies**: none new (Swashbuckle already in use; `System.ComponentModel.DataAnnotations` is in the BCL)  
**Storage**: JSON execution-log files under the data dir. Two new nullable properties, backward compatible  
**Testing**: xUnit + FluentAssertions. Unit tests in `tests/unit/Queues/QueueExecutionServiceTests.cs` (existing `Harness`, `SetWatchdog`). Integration tests modelled on `tests/integration/Sequences/AbandonedSequenceLogIntegrationTests.cs` and `SequenceWatchdogTimeoutApiIntegrationTests.cs`. OpenAPI contract test modelled on `tests/contract/Sequences/SequenceWritesOpenApiTests.cs`  
**Target Platform**: Windows service (GameBot.Service)  
**Project Type**: web-service (backend + API docs; no web-ui change)  
**Performance Goals**: per firing, one extra CTS allocation and one AsyncLocal push/pop. Per finalize, one null check. Negligible next to a sequence firing (seconds to minutes); no hot-path loop affected  
**Constraints**: no change to status values, retry, scheduling, queue continuation, default bound or maximum. No field renames  
**Scale/Scope**: ~8 source files, ~4 test files, architecture doc + STATUS roll-up

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code quality | Small, cohesive additions. New public members get XML docs. No new dependencies. CamelCase method names (no underscores). PASS |
| II. Testing | Unit (queue watchdog vs stop scope), integration (log entry stamping incl. swallowed cancellation, untouched child commands, success/stop negatives, API projection), contract (sequence read-out, OpenAPI). Tests are written before the code they cover. PASS |
| III. UX consistency | camelCase fields matching existing `effectiveTimeoutMs`/`watchdogTimeoutMs` naming. Additive only. Documented in OpenAPI. PASS |
| IV. Performance | Declared above; not a hot path. PASS |
| V. Living docs | `docs/architecture.md` Execution Log + queue watchdog text updated with a refreshed "Last reviewed". Spec Status line set to Implemented at the end, with a `specs/STATUS.md` row. PASS |

Post-design re-check: PASS (no violations; Complexity Tracking empty).

## Project Structure

### Documentation (this feature)

```text
specs/094-sequence-time-limit-reporting/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── api-changes.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Commands/SequenceTimeLimits.cs            # NEW: default/max constants + Resolve
├── Commands/FileSequenceRepository.cs        # use SequenceTimeLimits.MaxWatchdogTimeoutMs
└── Logging/ExecutionLogModels.cs             # ExecutionLogEntry.CancellationReason/TimeLimitMs + ExecutionCancellationReasons

src/GameBot.Service/
├── Services/SequenceExecution/SequenceTimeLimitScope.cs   # NEW: ambient scope
├── Services/SequenceExecution/SequenceExecutionService.cs # stamp at both finalize sites
├── Services/QueueExecution/QueueExecutionService.cs       # timer CTS + push scope; default from SequenceTimeLimits
├── Services/ExecutionLog/ExecutionLogContext.cs           # CancellationReason/TimeLimitMs
├── Services/ExecutionLog/ExecutionLogService.cs           # copy to entry; tree projection
├── Models/ExecutionLogs.cs                                # DTO fields
├── Models/SequenceStepContracts.cs                        # [Range] + docs on WatchdogTimeoutMs
├── Endpoints/ExecutionLogsEndpoints.cs                    # map fields (list, detail, tree)
├── Endpoints/SequencesEndpoints.cs                        # effectiveWatchdogTimeoutMs
└── Swagger/                                               # schema descriptions + GET description

tests/
├── unit/Queues/QueueExecutionServiceTests.cs
├── integration/Sequences/SequenceTimeLimitLogIntegrationTests.cs   # NEW
├── integration/Sequences/SequenceWatchdogTimeoutApiIntegrationTests.cs
└── contract/Sequences/SequenceTimeLimitOpenApiTests.cs             # NEW

docs/architecture.md, specs/STATUS.md, CHANGELOG.md
```

**Structure Decision**: Existing single backend layout (Domain + Service + tests/unit|integration|contract). No new projects.

## Complexity Tracking

No violations.

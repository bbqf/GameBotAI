# Implementation Plan: Execution Log Retention Default & Long-Run Rotation

**Branch**: `084-log-retention-rotation` | **Date**: 2026-09-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/084-log-retention-rotation/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Two changes to keep the execution log clean: (1) change the compiled-in default of
`ExecutionLogRetentionPolicy.RetentionDays` from 60 to 7, affecting only deployments that have never
saved an explicit policy; (2) rotate a queue run's execution log once it has been continuously open
for more than 24 hours. Because each sequence firing already produces its own `ExecutionLogEntry`
linked only by a shared queue-root `RootExecutionId` (no single entry grows unbounded today),
rotation is implemented as: at the existing per-firing boundary inside `QueueExecutionService`,
check whether the active segment's queue-root entry is >24h old; if so, close it out (recording a
`RotatedToExecutionId` link and a "rotated, continuation available" summary) and open a new
queue-root entry (recording a `RotatedFromExecutionId` back-link and a "continuation of a prior run"
summary) before the next firing proceeds, updating the run's in-memory active root id so every
subsequent firing attaches to the new segment. Firings within one queue run are already strictly
sequential (verified in code), so this requires no new locking. Both link fields are additive on
`ExecutionLogEntry`/its DTO and surfaced via the existing `RelatedObjects` link mechanism on the
detail endpoint, with a minimal UI hook to render them.

## Technical Context

**Language/Version**: C# / .NET (ASP.NET Core minimal APIs) for the backend; TypeScript + React 18 (Vite) for the web UI — matching the rest of the repo.
**Primary Dependencies**: Existing `GameBot.Domain.Logging` (`ExecutionLogRetentionPolicy`, `ExecutionLogEntry`, `IExecutionLogRepository`), existing `GameBot.Service.Services.QueueExecution` (`QueueExecutionService`, `QueueRunHandle`), existing `TimeProvider` abstraction already used for testable time in `QueueExecutionService`. No new packages.
**Storage**: File-based JSON execution log entries under `data/execution-logs/{id}.json` via `FileExecutionLogRepository` (existing, unchanged file layout — rotation adds fields to entries, not new files/folders); retention policy file `data/config/execution-log-policy.json` via `ExecutionLogRetentionPolicyRepository` (existing, only the in-code default changes).
**Testing**: xUnit unit/integration tests (existing `tests/unit/ExecutionLogs`, `tests/unit/Queues`, `tests/integration/ExecutionLogs` conventions), using the existing injectable `TimeProvider` to simulate elapsed time across a 24h+ span without real waits. Jest + React Testing Library for the small web-ui link-rendering addition.
**Target Platform**: Windows desktop host running the GameBot service + emulator automation; browser-based Web UI.
**Project Type**: Web application (ASP.NET Core backend + React SPA), matching the existing repo layout — no new project.
**Performance Goals**: Rotation check is one timestamp comparison per sequence firing (already-loaded root entry) — negligible overhead added to the existing per-firing path. The (rare, at most once per 24h per active queue) rotation itself is two additional log-entry writes (finalize old root + create new root), the same cost class as the existing `LogQueueFinalizeAsync`/`LogQueueStartAsync` calls it reuses.
**Constraints**: Must not change the on-disk file layout or existing `ExecutionLogEntry`/DTO fields (only additive optional fields); must not rotate mid-sequence (FR-007); must not require any new synchronization primitive given firings within one queue run are already sequential (research R3); must leave historical (pre-feature) entries fully functional with both new fields `null` (FR-014).
**Scale/Scope**: Two domain fields, two DTO fields, one default value change, one new rotation-decision code path in `QueueExecutionService` (reusing `LogQueueStartAsync`/`LogQueueFinalizeAsync`), one small detail-projection addition (`ExecutionLogService.BuildDetailProjection`) reusing the existing `RelatedObjects` mechanism, one small web-ui rendering addition. No new endpoints, no data migration, no changes to `FileExecutionLogRepository`'s storage/query logic.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality Discipline | Additive fields and one new focused rotation-decision method (kept under ~50 LOC, reusing existing `LogQueueStartAsync`/`LogQueueFinalizeAsync` rather than duplicating entry-creation logic); no new dependencies; no new synchronization complexity because firings are already sequential (research R3) — avoids the unjustified-complexity trap of adding a lock "just in case". PASS. |
| II. Testing Standards | New xUnit tests: retention-default test (fresh policy → 7 days), rotation unit tests on `QueueExecutionService`/`ExecutionLogService` using the existing fake `TimeProvider` (no-rotation-under-24h, rotation-at-boundary-only-between-firings, multi-rotation chain, stop-before-24h-no-rotation, retention-independent-expiry-of-segments), integration test extending `tests/integration/ExecutionLogs/ExecutionLogRetentionIntegrationTests.cs` patterns. Jest test for the new related-object link rendering. PASS (planned in tasks). |
| III. UX Consistency | Reuses the existing `RelatedObjectLinkDto`/`RelatedObjects` pattern already used for other cross-references in the execution log detail view, so the new rotation links look and behave exactly like existing related-object links (including the existing `isAvailable`/`unavailableReason` handling for a since-deleted target). No new error envelope shape. PASS. |
| IV. Performance Requirements | Declared above: one timestamp comparison added to the existing per-firing path; rotation itself is same cost class as existing queue start/finalize writes and happens at most once per 24h per active queue. No dedicated benchmark needed, consistent with how sibling per-entry retention logic was treated. PASS. |
| V. Living Documentation | `docs/architecture.md` will get an updated description of the execution log domain model (retention default, rotation/segment chaining) with "Last reviewed" bumped, in the same PR as the code change. This spec's `Status` line and `specs/STATUS.md` will be updated once implemented. PASS (tracked in tasks). |

No violations requiring the Complexity Tracking table.

## Project Structure

### Documentation (this feature)

```text
specs/084-log-retention-rotation/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md         # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── execution-log-rotation.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Logging/
├── ExecutionLogRetentionPolicy.cs   # RetentionDays default 60 → 7
└── ExecutionLogModels.cs            # ExecutionLogEntry + RotatedToExecutionId / RotatedFromExecutionId

src/GameBot.Service/
├── Services/ExecutionLog/ExecutionLogService.cs
│   # + LogQueueRotateAsync(oldRootId, queueId, queueName) helper (closes old root, opens new root,
│   #   links both) reusing LogQueueFinalizeAsync/LogQueueStartAsync internals
│   # + BuildDetailProjection: add rotation RelatedObjectLinkDto entries when fields are set
├── Services/QueueExecution/
│   ├── QueueExecutionService.cs     # rotation-check call inline before each RunOneSequenceAsync firing;
│   │                                 # rootId becomes mutable / sourced from handle.RootExecutionId
│   └── QueueRunHandle.cs            # RootExecutionId already `{ get; set; }` — reassigned on rotation, no schema change
├── Models/ExecutionLogs.cs          # ExecutionLogEntryDto + RotatedToExecutionId / RotatedFromExecutionId
└── Endpoints/ExecutionLogsEndpoints.cs  # no route changes; response mapping picks up new DTO fields

tests/unit/ExecutionLogs/
└── ExecutionLogRotationTests.cs     # new: rotation decision + link creation

tests/unit/Queues/
└── QueueExecutionServiceTests.cs    # + rotation-in-run-loop cases (existing file)

tests/integration/ExecutionLogs/
└── ExecutionLogRetentionIntegrationTests.cs  # + default-is-7-days case (existing file), or a
                                               # sibling ExecutionLogRotationIntegrationTests.cs

src/web-ui/src/
├── services/executionLogsApi.ts     # + rotatedToExecutionId/rotatedFromExecutionId on the entry type
├── pages/ExecutionLogs.tsx          # + render the rotation related-object link in the detail view
└── pages/__tests__/                 # + Jest coverage for the new link rendering

docs/architecture.md                 # updated per constitution (Living Documentation)
```

**Structure Decision**: No new projects or directories. This is a same-shape, additive change to the
existing single ASP.NET Core backend + single React SPA web application: two new optional fields on
an existing domain/DTO pair, one new default value, one new focused method reusing existing
log-writing helpers, one inline check in the existing queue-run loop, and a small additive UI
rendering hook reusing the existing related-object-link pattern. Mirrors how the existing
per-entry retention mechanism itself was built — no structural changes, only additive fields/handlers
in the same files.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table omitted.

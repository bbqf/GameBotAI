# Implementation Plan: Show the newest runs of a running queue in the execution log (B-044)

**Branch**: `198-fix-queue-log-children` | **Date**: 2026-11-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/198-fix-queue-log-children/spec.md` (GitHub issue #408)

## Summary

`FileExecutionLogRepository` keeps all entries in an in-memory list (`_entries`). The list endpoint and the subtree endpoint read only this list. `DeleteExpiredAsync` reads the list, deletes the expired files, and then sets `_entries` to the entries that it kept from its first read. It does this without `_mutex`, the lock of the writes. Each `AddAsync` or `UpsertAsync` between the first read and the set is lost from the list (the file stays on disk). At service start, the first cleanup loads about 64,000 files under the lock, and the queues that resume wait for the lock with their new root entries. When the load ends, the cleanup takes its copy, the root writes go in, and the cleanup then overwrites the list. Thus the new roots of Exo and Farm-2 were not in the list from 15:05 UTC until the stop wrote them again (research R-001).

The fix:

1. `DeleteExpiredAsync` holds `_mutex` for its whole remove step. It removes the expired entries from the current `_entries` and deletes their files. It does not replace the list with an earlier copy (FR-001 to FR-003).
2. A new repository method `CloseAbandonedRunningAsync` closes, under `_mutex`, each entry with the status `running` and a timestamp before the start of the current service process. The new status is `failure`, and the summary says that the run ended when the service stopped (FR-006, FR-007).
3. `ExecutionLogRetentionCleanupService` calls the start sweep one time, before its first cleanup, and writes one log line with the number of closed entries (FR-006a).
4. The OpenAPI description of `GET /api/execution-logs` and `GET /api/execution-logs/{id}/subtree` states the queue log rules (FR-008).

## Technical Context

**Language/Version**: C# (`LangVersion` preview) / .NET 9 (domain and service); no web UI change
**Primary Dependencies**: ASP.NET Core minimal APIs and Swashbuckle (description text only), System.Text.Json (no change)
**Storage**: Files, one JSON file for each entry in `<data root>/execution-logs/` (no format change). The in-memory list is a cache of these files.
**Testing**: xUnit + FluentAssertions (`tests/unit`, `tests/integration`, `tests/contract`)
**Target Platform**: Windows service host
**Project Type**: web service (backend) with a web UI (not changed)
**Performance Goals**: A write waits for the lock at most for the remove step of one cleanup. In production this is about 200 file deletes every 30 minutes (less than 1 s). The remove step is O(n) over the list, the same as now. The start sweep runs one time and writes only the `running` entries of an earlier process (in production, fewer than 50). No change for the list and subtree reads.
**Constraints**: No change of the response shape of the execution log endpoints, the queue engine, the rotation rule, the delete rule, or the file format. No change of `IExecutionLogService` (four test fakes implement it). One new member on `IExecutionLogRepository` (only `FileExecutionLogRepository` implements it).
**Scale/Scope**: About 64,000 stored entries, 4 production queues, about 9,000 new entries each day.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. Phone support level: not applicable.

| Gate | Status | Note |
|---|---|---|
| I. Code quality | PASS | Small changes in one repository class, one hosted service, and one Swagger method. One new repository method with a doc comment. No new dependency. Method names in CamelCase. |
| II. Testing | PASS | Bug fix: a unit test that fails before the fix (an `AddAsync` during the remove step of `DeleteExpiredAsync` is lost from the list). The test uses an internal test hook that runs inside the remove step, so the test is deterministic. Unit tests for the start sweep, an integration test for the start sweep at host start, and a unit test for a subtree with 500 children. |
| III. UX consistency | PASS | The new summary text follows the current summary style (`Queue '<name>' ...`). The new log line follows the cleanup log line. The API description gets the queue log rules. |
| IV. Performance | PASS | Declared in Technical Context (research R-004). The perf note goes into the PR and the CHANGELOG. |
| V. Living documentation | PASS | Update `docs/architecture.md` (the Execution Log bullet and "Last reviewed"), `CHANGELOG.md`, `specs/STATUS.md` (row 198). No earlier spec is superseded: the fix restores the intended cache rule. |
| VI. STE | PASS | All artifacts are in STE. Code comments, the summary text, and the log line are in STE. |
| Version bump | PASS (phase 2) | Bump `patch` in `installer/versioning/version.override.json` before the push (pipeline step 10). |

Post-design re-check: PASS. The design adds no project, no stored field, and no public API shape.

## Project Structure

### Documentation (this feature)

```text
specs/198-fix-queue-log-children/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── execution-log-store.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Logging/
├── IExecutionLogRepository.cs        # + CloseAbandonedRunningAsync
├── FileExecutionLogRepository.cs     # DeleteExpiredAsync under _mutex; CloseAbandonedRunningAsync; internal test hook
└── ExecutionLogAbandonedRun.cs       # NEW: the close rule (status, summary) of an abandoned running entry

src/GameBot.Service/
├── Hosted/ExecutionLogRetentionCleanupService.cs   # start sweep before the first cleanup; log line 7102
└── Swagger/SwaggerConfig.cs                        # queue log rules in the two operation descriptions

tests/unit/ExecutionLogs/
├── ExecutionLogRepositoryCleanupRaceTests.cs       # NEW: FR-001..FR-003 (fails before the fix)
├── ExecutionLogAbandonedRunTests.cs                # NEW: FR-006, FR-007
└── ExecutionLogQueueSubtreeSizeTests.cs            # NEW: FR-005 (500 children)

tests/integration/ExecutionLogs/
└── ExecutionLogStartSweepIntegrationTests.cs       # NEW: the sweep at host start

tests/contract/ExecutionLogs/
└── ExecutionLogsApiDescriptionTests.cs             # NEW: FR-008 (OpenAPI text)

docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: The fix stays in the existing projects. The cache rule belongs to `FileExecutionLogRepository` (Domain). The close rule is a small static class in the same folder, so that the repository and the tests use one rule. The start trigger belongs to the existing hosted cleanup service (Service).

## Complexity Tracking

No constitution violations.

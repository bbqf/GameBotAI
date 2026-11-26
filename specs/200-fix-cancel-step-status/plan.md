# Implementation Plan: Give a reschedule-self Cancel step the status success when the cancel runs (B-046)

**Branch**: `200-fix-cancel-step-status` | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/200-fix-cancel-step-status/spec.md`

## Summary

The execution log calculates the status of a step node from the action outcome when it reads the log (`ExecutionLogService.MapStepStatus`). The calculation does not know the outcome `cancelled`, so a `Cancel` step that removes bookings gets the default status `failure`. A `Cancel` step that finds no booking has the outcome `noop` and gets `skipped`.

The fix has three parts:

1. A new helper `ResolveStepStatus` in `ExecutionLogService` gives `success` to a `reschedule-self` step node with the outcome `cancelled`. `MapStepStatus` does not change, because a primitive or WaitForImage step that a stop cut off also has the outcome `cancelled` and must keep `failure`.
2. `ResolveStepStatus` also gives `success` to a `reschedule-self` step node with the outcome `noop` when the stored step detail of the same `stepOrder` has the option `Cancel`. The other `noop` nodes keep `skipped`. `BuildStepNode` calls `ResolveStepStatus` in place of `MapStepStatus`.
3. `SequenceExecutionService.DispatchSelfReschedule` catches an error of `ISelfRescheduleCoordinator.CancelSelf` and returns the outcome `failed`, the message "cancel not performed: <error text>", and `removed` `false`. The step continues to be a recorded service-level step, so the sequence does not stop. `MapStepStatus("failed")` gives `failure`. This changes the error case only: today the error ends the run with `failure` (`FinalizeAbandonedAsync` and a rethrow).

All status logic is at read time, so the logs that the service stored before the fix also show the correct status.

## Technical Context

**Language/Version**: C# (`LangVersion` preview) on .NET 9 (`net9.0`, service and domain)  
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, xUnit, FluentAssertions  
**Storage**: Execution log JSON files (file store, read into memory). No change to the file format.  
**Testing**: xUnit unit tests (`tests/unit`), integration tests with `WebApplicationFactory<Program>` (`tests/integration`)  
**Target Platform**: Windows service (GameBot.Service)  
**Project Type**: Web service with a web UI. The web UI does not change.  
**Performance Goals**: No measurable change. The option look-up runs only for a `reschedule-self` node with the outcome `noop`, and it reads the detail items of one entry (the same pattern as `ReadStepParameters`).  
**Constraints**: The message text, the stored action outcome, `actionOutcome`, `reasonCode`, the booking removal, and the sequence status do not change (FR-006, FR-007, FR-011).  
**Scale/Scope**: 2 production files (`ExecutionLogService.cs`, `SequenceExecutionService.cs`), tests, `docs/architecture.md`, `CHANGELOG.md`, spec status files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. The web UI shows the `status` that the service gives. No phone support statement is necessary.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | PASS | Small changes in two methods. A new private helper stays under 50 lines. Method names use CamelCase. |
| II. Testing | PASS | Failing tests first (bug fix rule): subtree tests for the `cancelled` and the `noop` Cancel nodes, an integration test that reads the stored subtree after a real run, and an integration test for the error case. |
| III. UX consistency | PASS | The status names stay the same. The error message tells the cause ("cancel not performed: <error text>"). |
| IV. Performance | PASS | No hot path change. Perf note: the look-up runs only for `reschedule-self` `noop` nodes. |
| V. Living documentation | PASS | Update `docs/architecture.md` (Self-reschedule Cancel paragraph and "Last reviewed"), spec 123 status `Implemented (iterated by 200)`, `specs/STATUS.md`. |
| VI. STE | PASS | All new text in STE. |
| Version bump | PASS | Phase 2 raises `patch` before the push (constitution "Version bump"). |

## Project Structure

### Documentation (this feature)

```text
specs/200-fix-cancel-step-status/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── execution-log-step-status.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs        # BuildStepNode, new ResolveStepStatus and ReadRescheduleOption helpers (MapStepStatus unchanged)
src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs  # DispatchSelfReschedule: catch an error of CancelSelf; new LoggerMessage in SequenceExecutionLog
tests/unit/ExecutionLogs/ExecutionLogServiceMapStepStatusTests.cs       # cancelled stays failure, failed -> failure, noop -> skipped
tests/unit/ExecutionLogs/ExecutionSubtreeProjectionTests.cs             # reschedule-self cancelled -> success; noop + option Cancel -> success; other -> unchanged
tests/integration/Queues/SelfRescheduleCancelDispatchIntegrationTests.cs # error of CancelSelf -> outcome failed, run continues; stored subtree status end to end
docs/architecture.md                                                    # Self-reschedule Cancel paragraph, Last reviewed
specs/123-reschedule-self-cancel/spec.md, specs/STATUS.md               # status lines
CHANGELOG.md, installer/versioning/version.override.json               # changelog entry, version bump
```

**Structure Decision**: The existing single service project and test projects. No new project or file in `src`.

## Phase 0 and Phase 1 outputs

- [research.md](research.md): R-001 to R-005 (cause, read-time fix, option look-up, error handling, stored logs).
- [data-model.md](data-model.md): the step node status rules.
- [contracts/execution-log-step-status.md](contracts/execution-log-step-status.md): the `status` values of a `reschedule-self` step node in `GET /api/execution-logs/{id}/subtree`.
- [quickstart.md](quickstart.md): how to check the fix.

## Constitution Check (after design)

All gates stay PASS. The design changes no API shape, no stored format, and no UI.

## Complexity Tracking

No violations.

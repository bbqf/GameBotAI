# Implementation Plan: Truthful tap outcome and an execution log for a single step

**Branch**: `claude/resolve-github-issue-whu0sd` (spec number 112) | **Date**: 2026-09-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/112-truthful-tap-outcome-log/spec.md`

## Summary

A `PrimitiveTap` step can report a "not executed" outcome with `accepted: 0` after the service sent the tap. This occurs when an error or a cancellation occurs in the read-back code after `SendInputsAsync` (research R-001). The plan adds a dispatch state that `TryDetectAndTap` fills. The `catch` blocks of `ExecuteOneStepAsync` read it. After a completed dispatch they return `executed` with `executed_then_error` or `executed_then_cancelled` and the real `accepted` count. During the dispatch they return `dispatch_unknown`. Before the dispatch they return the current outcomes.

`POST /api/steps/execute` writes no execution-log entry today. The plan moves the 10-second limit into a new `ForceExecuteStepAsync` overload. The executor writes exactly one `step` entry for each call that passes the session check, also for a timeout, a cancellation and an error (R-003, R-004). A new method `IExecutionLogService.LogStepExecutionAsync` writes the entry. A failure of the write gives a warning only (R-005). The Swagger text, `docs/architecture.md`, `CHANGELOG.md` and `specs/STATUS.md` tell the contract (R-007).

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)  
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle, xUnit, FluentAssertions  
**Storage**: File-backed execution-log repository (JSON). New entries of the execution type `step`. The stored format does not change.  
**Testing**: xUnit unit tests (`tests/unit`), integration tests with `WebApplicationFactory` (`tests/integration`)  
**Target Platform**: Windows service (CI on `windows-latest`). The `PrimitiveTap` detection path runs only on Windows.  
**Project Type**: Web service with a web UI (the web UI does not change)  
**Performance Goals**: One more file write for each single step call. No change for commands, sequences and queues.  
**Constraints**: No change to thresholds, retry count, retry progression, jitter or hold duration (FR-011). No change to the response shape, except new status and reason values (FR-012). No change to the log of commands, sequences and queues.  
**Scale/Scope**: About 5 service files, 2 new test files, 1 changed test file, docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| Code quality (analyzers, `-warnaserror`) | Pass | Small change in the existing style. New log messages use `LoggerMessage`. Method names in CamelCase, no underscores. |
| Tests first, regression test for the bug | Pass | The post-dispatch error test and the post-dispatch cancellation test fail before the change (they get `accepted: 0`). The log tests fail before the change (no entry). |
| UX / API consistency | Pass | The response shape does not change. The new status values follow the snake-case form of the current values. |
| Performance | Pass | One file write for each single step call. No change on the hot path of queues. |
| Backward compatibility | Pass | A tap without an error gives the same outcome as before. The endpoint errors do not change. |
| Living docs | Pass | `docs/architecture.md` (with a new "Last reviewed" date), `CHANGELOG.md`, `specs/STATUS.md` and the spec `Status` line change. |
| Language (STE) | Pass | All new text in STE. |

Post-design re-check: Pass. No violations.

## Project Structure

### Documentation (this feature)

```text
specs/112-truthful-tap-outcome-log/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── steps-execute.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/CommandExecutor.cs
    # TapDispatchState; TryDetectAndTap fills it; catch blocks read it;
    # ForceExecuteStepAsync overload with timeout; one log write per call; LoggerMessage for a write failure
src/GameBot.Service/Services/ICommandExecutor.cs
    # new overload ForceExecuteStepAsync(string?, CommandStep, TimeSpan?, CancellationToken)
src/GameBot.Service/Services/ExecutionLog/StepExecutionLogRecord.cs
    # new record
src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
    # IExecutionLogService.LogStepExecutionAsync and its implementation
src/GameBot.Service/Endpoints/StepsEndpoints.cs
    # pass the 10-second limit; catch TimeoutException for the timeout body
src/GameBot.Service/Swagger/SwaggerConfig.cs
    # Description of POST /api/steps/execute

tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs
    # post-dispatch error, post-dispatch cancellation, dispatch error, no dispatch when detection fails
tests/unit/Commands/CommandExecutorStepLogTests.cs
    # new: one entry per call (outcome, timeout, cancellation, error, log write failure, no entry for a session error)
tests/integration/Commands/StepExecutionLogIntegrationTests.cs
    # new: entry visible through GET /api/execution-logs with the time filter; timeout entry
tests/contract/StepsExecuteOpenApiTests.cs
    # new: the Swagger description states the contract
tests/unit/Queues/RecordingExecutionLog.cs, tests/unit/Queues/QueueMonitorServiceTests.cs
    # test doubles of IExecutionLogService get the new method

docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: The change stays in the existing service project. No new project. Test doubles for the log service implement `IExecutionLogService`; the new interface method needs an implementation in each test double that implements the interface.

## Complexity Tracking

No violations.

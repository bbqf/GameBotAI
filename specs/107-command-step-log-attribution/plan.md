# Implementation Plan: Log each command step under its own step ID (B-020)

**Branch**: `claude/resolve-github-issue-6ldqb3` (spec number 107) | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/107-command-step-log-attribution/spec.md`

## Summary

The execution log names each command node by the first sequence step that uses the same command. The cause is in `SequenceExecutionService.ExecuteAsync`: it finds the sequence step for a run result through a dictionary keyed by command ID, and this dictionary keeps only the first step for each command.

The fix has two parts:

1. The sequence runner records the `stepId` of the step that ran in each command step result (`SequenceExecutionResult.StepResult.StepId`).
2. The service finds the sequence step by this `stepId` first. It uses the lookup by command ID only when the `stepId` is not set or is not found. The flatten helper also includes `elseBody` steps.

No API shape changes. The new property does not go into the JSON of the run result.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)  
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, xUnit, FluentAssertions  
**Storage**: File-based execution log (`FileExecutionLogRepository`). No storage change.  
**Testing**: xUnit unit tests (`tests/unit`), integration tests with `WebApplicationFactory<Program>` (`tests/integration`)  
**Target Platform**: Windows service (CI on `windows-latest`)  
**Project Type**: Web service with a web UI. This fix touches only the service and the domain.  
**Performance Goals**: No change. The fix adds one dictionary lookup for each step result.  
**Constraints**: No change to API shape, message format, or run semantics (spec FR-007 to FR-009).  
**Scale/Scope**: 3 source files, 2 test files, `CHANGELOG.md`, `specs/STATUS.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| Code quality (analyzers, `-warnaserror`) | Pass | Small change in existing style. Method names in CamelCase, no underscores. |
| Tests first, regression test for the bug | Pass | New unit test and new integration test (tasks T002, T003) before the fix. |
| UX / API consistency | Pass | No API shape change. Message format stays the same. |
| Performance | Pass | One more dictionary lookup for each step result. |
| Living docs | Pass | `docs/architecture.md`: no change, because the domain model, capabilities, API surface and persistence do not change. `CHANGELOG.md` gets a "Fixed" entry. `specs/STATUS.md` gets row 107. |
| Language (STE) | Pass | All new text in STE. |

Post-design re-check: Pass. No violations.

## Project Structure

### Documentation (this feature)

```text
specs/107-command-step-log-attribution/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output
```

No `contracts/` directory: the fix does not change an API contract.

### Source Code (repository root)

```text
src/GameBot.Domain/Services/SequenceRunner.cs
    # StepResult.StepId (new, [JsonIgnore]); AddStep(..., stepId) parameter;
    # ExecuteSingleStepAsync passes step.StepId for the command-path results
src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs
    # find the sequence step by StepId first; FlattenSequenceSteps includes ElseBody

tests/unit/Sequences/SequenceRunnerStepIdTests.cs                          # new
tests/integration/ExecutionLogs/SharedCommandStepAttributionIntegrationTests.cs  # new

CHANGELOG.md
specs/STATUS.md
```

**Structure Decision**: Use the existing projects. The runner change is in `GameBot.Domain`. The log change is in `GameBot.Service`. Tests go in the existing unit and integration test projects.

## Complexity Tracking

No violations.

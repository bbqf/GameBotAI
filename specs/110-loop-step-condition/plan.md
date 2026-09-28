# Implementation Plan: A condition on a Loop step is a guard that the runtime obeys

**Branch**: `claude/resolve-github-issue-wp4ek5` (spec number 110) | **Date**: 2026-09-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/110-loop-step-condition/spec.md`

## Summary

`POST /api/sequences` accepts a `condition` on a top-level `Loop` step, but the save mapping drops the field, and the runner does not look at it. Thus the loop runs when its condition is false, and the execution log shows no condition result (research R-001).

The fix selects result 1 of the issue: the condition on a `Loop` step is a guard, with the same rules as on an `Action` step (research R-002). The save mapping keeps the field (R-007). Save-time validation checks it (R-006). The runner evaluates it one time before the loop starts, with the guard evaluator that it uses for `Action` steps. A false guard gives a `Skipped` `Loop` entry with zero iterations. An error fails the sequence (R-003, R-004). The `Loop` entry of the execution log gets `conditionType` and `conditionResult` (R-005). The OpenAPI step schema, `docs/architecture.md`, `CHANGELOG.md` and `specs/STATUS.md` tell the rule (R-008).

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)  
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle, xUnit, FluentAssertions  
**Storage**: File-backed sequence repository. No format change: `SequenceStep.Condition` is already stored for each step type.  
**Testing**: xUnit unit tests (`tests/unit`), contract tests with `WebApplicationFactory` (`tests/contract`)  
**Target Platform**: Windows service (CI on `windows-latest`)  
**Project Type**: Web service with a web UI. This fix touches only the service.  
**Performance Goals**: No change. One condition evaluation for each guarded `Loop` step in a run.  
**Constraints**: No change to `If` steps, conditioned `Action` steps, `Break` steps, and the loop configurations `count`, `while` and `repeatUntil` (spec FR-009). No new condition type. No new field in the request contract.  
**Scale/Scope**: 5 source files, 1 swagger filter, 4 to 5 test files, `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`, `CLAUDE.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| Code quality (analyzers, `-warnaserror`) | Pass | Small change in existing style. The guard block moves into one private method, so the runner has no copy. Test method names in CamelCase, no underscores. |
| Tests first, regression test for the bug | Pass | The runner test "false guard skips the body" and the contract test "guard survives a save" fail before the fix (tasks in phase 3). |
| UX / API consistency | Pass | Same guard rule and same log fields as for an `Action` step. No new request field. |
| Performance | Pass | One evaluation for each guarded `Loop` step. |
| Living docs | Pass | `docs/architecture.md` gets the rule and a new "Last reviewed" date. `CHANGELOG.md` and `specs/STATUS.md` change too. |
| Language (STE) | Pass | All new text in STE. |

Post-design re-check: Pass. No violations.

## Project Structure

### Documentation (this feature)

```text
specs/110-loop-step-condition/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── loop-step-guard.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

```text
src/GameBot.Service/Endpoints/SequencesEndpoints.cs
    # MapToLinearSteps: keep Condition on a Loop step; minSimilarity check also for a Loop guard
src/GameBot.Domain/Services/SequenceStepValidationService.cs
    # ValidateLoopStep: check the Loop step's own condition with ValidateStepCondition
src/GameBot.Domain/Services/SequenceRunner.cs
    # EvaluateStepGuardAsync (moved from ExecuteSingleStepAsync); Loop path calls it first;
    # AddLoopStep(conditionType, conditionResult); SetConditionForLatestLoopStep
src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs
    # Loop log entry: conditionType and conditionResult attributes
src/GameBot.Service/Swagger/SequenceNestingRulesSchemaFilter.cs
    # description of the step condition property

tests/unit/Sequences/SequenceRunnerLoopGuardTests.cs              # new: runner guard behavior
tests/unit/Sequences/LoopValidationTests.cs                       # guard validation
tests/contract/Sequences/SequenceLoopGuardContractTests.cs        # new: save, read, dryRun 400, OpenAPI text
tests/contract/ExecutionLogs/ExecutionLogsLoopGuardContractTests.cs  # new: log entry fields

docs/architecture.md
CHANGELOG.md
specs/STATUS.md
CLAUDE.md
```

**Structure Decision**: Use the existing projects. Code changes are in `GameBot.Domain` (runner, validator) and `GameBot.Service` (endpoint mapping, execution log, OpenAPI). Tests go in the existing unit and contract test projects.

## Complexity Tracking

No violations.

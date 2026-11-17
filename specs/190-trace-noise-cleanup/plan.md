# Implementation Plan: Trace Noise Cleanup

**Branch**: `190-trace-noise-cleanup` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/190-trace-noise-cleanup/spec.md`

## Summary

Remove trace messages that come in healthy runs and tell the operator nothing. The change has three parts:

1. Remove the "Slow image match" Warning from `ImageMatchEvaluator`. Also remove the `Stopwatch` that measures the duration only for this message.
2. Change the level of `TapRetryExhausted` (event 6012) in `CommandExecutor` from Warning to Debug. The text and the data stay the same.
3. Remove three log definitions in `CommandExecutor` that no code calls: `TriggerBypassed` (6002), `SessionCacheHit` (6006), and `SessionCacheMiss` (6007).

Unit tests use a test logger that records each entry. The tests make sure that a slow image match writes no line and that event 6012 is at Debug level.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (service and domain projects)  
**Primary Dependencies**: Microsoft.Extensions.Logging with source-generated `[LoggerMessage]` methods; OpenCvSharp (not changed)  
**Storage**: N/A  
**Testing**: xUnit with FluentAssertions, in `tests/unit`  
**Target Platform**: Windows service (console host)  
**Project Type**: Web service with a web UI. This feature changes the service only.  
**Performance Goals**: No regression. The image-match path does one `Stopwatch` start and stop less for each evaluation, and it writes no console line. Thus it gets a small gain.  
**Constraints**: The evaluation result and the execution log entries must not change (FR-002, FR-005).  
**Scale/Scope**: 2 source files, 2 test files, 1 CHANGELOG entry, 1 version bump, 1 spec status update.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. No phone support level applies.

| Gate | Result | Note |
|------|--------|------|
| I. Code quality | Pass | The change removes dead code (three unused log methods, one unused measurement). Remove `using System.Diagnostics;` from `ImageMatchEvaluator.cs` if no other code in the file uses it, so the build has no unused-using warning. |
| II. Testing | Pass | New unit tests for FR-001 and FR-003. The current tests for the evaluation result, the tap step outcome, and the execution log entry of a step cover FR-002 and FR-005. |
| III. UX consistency | Pass | Log text does not change. Only one level changes and one message goes away. |
| IV. Performance | Pass | Hot path gets less work. Perf note in the CHANGELOG entry. No benchmark is necessary for a removal. |
| V. Living docs | Pass | `docs/architecture.md` does not list these messages, so it does not change. Spec 037 chose Warning for event 6012, so its `Status` changes to "Implemented (iterated by 190)" in `spec.md` and in `specs/STATUS.md`. Spec 190 gets the status "Implemented" when the work is done. |
| VI. STE | Pass | All new text in this plan and its artifacts uses STE. |
| Version bump | Planned | Bump `patch` in `installer/versioning/version.override.json` before the push (CLAUDE.md rule). |

## Project Structure

### Documentation (this feature)

```text
specs/190-trace-noise-cleanup/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (the log events that change)
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── trace-messages.md  # Phase 1 output (the log message contract)
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs   # remove SlowMatch and the Stopwatch
└── GameBot.Service/Services/CommandExecutor.cs                  # 6012 to Debug; remove 6002, 6006, 6007

tests/unit/
├── Triggers/ImageMatchEvaluatorAlternatesTests.cs or a new
│   Triggers/ImageMatchEvaluatorLoggingTests.cs                  # no line for a slow evaluation
└── Commands/CommandExecutorPrimitiveTapTests.cs                 # event 6012 is at Debug level

specs/037-tap-wait-retry/spec.md                                 # Status line
specs/STATUS.md                                                  # 037 and 190 rows
CHANGELOG.md                                                     # Changed entry
installer/versioning/version.override.json                       # patch bump
```

**Structure Decision**: The feature changes two current source files. It adds tests in the current unit test project. It adds no new project or folder.

## Complexity Tracking

No constitution violation. This section is empty.

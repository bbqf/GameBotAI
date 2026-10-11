# Implementation Plan: Reduce Build Time

**Branch**: `133-reduce-build-time` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/133-reduce-build-time/spec.md`

## Summary

The default build `dotnet build C:\src\GameBot\GameBot.sln` (Debug) must end in less than 4 minutes on a clean state. The target is 3 minutes or less. The plan has two parts: measure first, then fix by measured share.

1. Phase A: test baseline, then three clean timed runs, then one binlog run with `ReportAnalyzer`. This gives a ranked cause list. The record also states if the default build starts a web UI step.
2. Phase B: fix the causes in ranked order. Remove duplicate builds and steps with no effect. Split only the methods and files named in the ranked list, each with a target set from the measurement. Test the named build settings (`UseSharedCompilation`, `-maxcpucount`, `BuildInParallel`, `Deterministic`) and the candidate package change.
3. Phase C: check incremental builds (no change, and a change in `GameBot.Domain` and in `GameBot.Service`), write the maintainer guide and the measure script, and record baseline and final values side by side.
4. Phase D (Polish and proof): see "Ordering rules" below.

### Ordering rules

- Split tasks: add ONE split task per ranked method or file, after the ranked cause list is done. There is no fixed count of split tasks. A split task names its method or file and its target from the measurement (FR-004).
- Project split: only as a last resort (FR-013). The trigger is: the ranked cause list names a project-wide cause, and the clean build is still 4 minutes or more after the method and file splits. After a project split, run the FR-007 incremental checks again. If a project split happens, change `C:\src\GameBot\docs\architecture.md` in the same PR and set a new "Last reviewed" date.
- Settings and packages: the final measurement (three clean runs) runs after all splits. It measures the kept settings and packages again. A decision that the final measurement does not confirm is reverted (FR-014).
- CI proof order: the CI pre-check and the test re-run come first. The push and the wait for CI (SC-006) depend on BOTH the CI pre-check and the test re-run.
- Polish before CI: the result record, the binlog cleanup, the doc links, and the status update (spec `Status` line set to `Implemented` in `C:\src\GameBot\specs\133-reduce-build-time\spec.md`, and the entry for feature 133 in `C:\src\GameBot\specs\STATUS.md`) come BEFORE the CI proof.
- The CI proof on the pushed branch is the LAST step. Any later commit needs a new CI run (SC-006).

No analysis rule, analysis level, warning-as-error setting, or pinned analyzer version is relaxed in any configuration (FR-008). If 4 minutes cannot be reached with all rules kept, the team records the measured floor and its cause, and reaches the lowest time that keeps all rules. SC-003 then is not a failure. CI is proven by the real workflow runs on the pushed branch (SC-006).

## Technical Context

**Language/Version**: C# on .NET 9 (`net9.0`, `LangVersion preview`); PowerShell 5.1 for the measure script
**Primary Dependencies**: MSBuild, `Microsoft.CodeAnalysis.NetAnalyzers` 10.0.301 (pinned), `Microsoft.CodeAnalysis.Metrics` 3.3.4 (candidate for removal test), MSBuild Structured Log Viewer (read-only tool)
**Storage**: N/A (measurement records are Markdown files in the feature folder)
**Testing**: xUnit test projects under `tests/unit`, `tests/contract`, `tests/integration`; the full suite is run once as the baseline and once at the end
**Target Platform**: Windows developer PC; CI runners run the same build in Release
**Project Type**: Multi-project .NET solution (`GameBot.Domain`, `GameBot.Emulator`, `GameBot.Service`, `GameBot.Updater`) plus a web UI in `src/web-ui`
**Performance Goals**: Clean build under 4 minutes (worst of 3), median 3 minutes or less; incremental build with no change under 30 seconds
**Constraints**: All analysis rules stay active; no product behavior change; CI, installer, and release flow keep working
**Scale/Scope**: 4 source projects, 3 test project groups; changes limited to build configuration, project graph, and code shape of the slowest methods and files

No item needs clarification. See [research.md](research.md).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Note |
|-----------|--------|------|
| I. Code Quality Discipline | Pass | FR-008 keeps all rules. Splits keep behavior and add no suppression. |
| II. Testing Standards | Pass | The test baseline runs before the change. The full suite must give the same result at the end (SC-005). A split of a method keeps existing tests as the check. `scripts/measure-build.ps1` has no automated test. One manual run of the script is the accepted check, because it is a measurement helper with no product risk. |
| III. User Experience Consistency | Pass | No user-facing change. |
| IV. Performance Requirements | Pass | This feature is a build-time performance goal with measured limits. |
| V. Living Documentation | Pass | `docs/build-time.md` is the new maintainer guide. `build-baseline.md` records the numbers. In the Polish step, the spec `Status` line is set to `Implemented` and `specs/STATUS.md` is made consistent. If a project split happens, `docs/architecture.md` changes in the same PR with a new "Last reviewed" date. |
| VI. Simplified Technical English | Pass | All new text uses STE. |

Post-design re-check: Pass. The design adds no new project and no new runtime dependency. The measure script and the guide are the only new files outside the feature folder.

## Project Structure

### Documentation (this feature)

```text
specs/133-reduce-build-time/
├── plan.md              # This file
├── research.md          # Phase 0 output: method decisions
├── data-model.md        # Phase 1 output: measurement records
├── quickstart.md        # Phase 1 output: how to measure
├── build-baseline.md    # Created in Phase A: baseline, ranked causes, final result
├── checklists/
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

No `contracts/` folder: the feature has no external interface.

### Source Code (repository root)

```text
C:\src\GameBot
├── Directory.Build.props        # shared build settings and analyzer packages (candidate change)
├── GameBot.sln                  # solution built by the measured command
├── src/
│   ├── GameBot.Domain/          # lowest-level project (incremental check 1)
│   ├── GameBot.Emulator/        # floating "*" package versions to pin
│   ├── GameBot.Service/         # top-level service project (incremental check 2); likely large methods
│   ├── GameBot.Updater/
│   └── web-ui/                  # check if the default build starts a web UI step
├── tests/                       # unit, contract, integration (tests are not timed, but must pass)
├── scripts/measure-build.ps1    # NEW: repeatable measure against the limit
├── docs/build-time.md           # NEW: maintainer guide (causes, rules, method). Include one line: limits apply to the developer PC, and the PC description is recorded with each result.
├── docs/architecture.md         # Change only if a project split happens (new "Last reviewed" date)
├── specs/STATUS.md              # Update in Polish: status of feature 133
└── .github/workflows/           # CI: must stay green
```

**Structure Decision**: Keep the existing project layout. Change code shape inside existing projects only. Split `GameBot.Service` into several projects only as a last resort, under the trigger in FR-013 (project-wide cause in the ranked list, and the clean build still 4 minutes or more after the method and file splits), because it raises change size and risk. After such a split, run the FR-007 checks again and change `docs/architecture.md` in the same PR.

## Complexity Tracking

No constitution violation. No entry needed.

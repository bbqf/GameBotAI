# Research: Reduce Build Time

No item in the Technical Context needs clarification. This file records the decisions about method.

## Decision 1: Measure first, fix by measured share

- Decision: Phase A (baseline, binlog, analyzer report) comes before any change. The fix order follows the measured seconds.
- Rationale: The build time rose again after an earlier fix. A guess has no value. The earlier note shows the cost is in a few places.
- Alternatives considered: Apply the known fix (split big methods) at once. Rejected: it can miss a cheaper cause such as a duplicate build.

## Decision 2: Tools for measurement

- Decision: Use `dotnet build /bl` with `/p:ReportAnalyzer=true`, then read the binlog (per project, per target, analyzer table). Use wall-clock time of three clean runs as the official number.
- Rationale: These tools were used in the earlier fix and give per-analyzer data. `ReportAnalyzer` adds overhead, so the official wall-clock runs do not use it.
- Alternatives considered: `dotnet-trace`, `-clp:PerformanceSummary`. Kept as a backup: `-clp:PerformanceSummary` is cheap and gives task time.

## Decision 3: Keep all analysis rules

- Decision: No rule, level, or warning-as-error setting changes. Work only on code shape, the project graph, package set, and redundant steps.
- Rationale: Spec clarification and FR-008. Local-only relaxation hides defects until CI.
- Alternatives considered: A `Fast` configuration that disables analyzers. Rejected by the spec.

## Decision 4: Code structure is the main lever

- Decision: Split large methods and files that the analyzer table marks as slow. Extract lambdas into named private static methods.
- Rationale: Analyzer cost is per method body. A body is one unit for the concurrent executor. Prior art: build fell from about 6 min to about 45 s by this method.
- Alternatives considered: Split `GameBot.Service` into several projects. Held as last resort: it raises the change size and risk, and the cost per analyzer is whole-compilation.

## Decision 5: Candidate package changes

- Decision: Test removal of `Microsoft.CodeAnalysis.Metrics`; pin the `*` versions in `GameBot.Emulator.csproj`. Keep `Microsoft.CodeAnalysis.NetAnalyzers` pinned at 10.0.301.
- Rationale: Metrics is an extra analyzer in every project and is not part of the rule policy. Floating versions slow restore and defeat the CI cache key.
- Alternatives considered: Apply the Metrics package only to `src` projects. Possible if the measurement shows the cost is mostly in test projects.

## Decision 5b: Concrete compiler and parallel settings to test

- Decision: Test exactly these settings: `UseSharedCompilation` (true, false), `-maxcpucount`, `BuildInParallel`, and `Deterministic` (true, false). Each test is one clean build, compared to the baseline. Link the result to the per-project timing: a setting helps if the critical-path project time or the sum of project times falls. Keep a setting only if the gain is measured and CI and the installer accept it.
- Rationale: A named list can be tested and recorded. The vague term "shared settings" cannot.
- Alternatives considered: Test all MSBuild properties. Rejected: no link to a measured cause.

## Decision 5c: Splits follow the ranked list

- Decision: The team splits only the methods and files named in the measured, ranked cause list, each with a target set from the measurement (for example, an analyzer time that falls below a figure). Split tasks depend on the list.
- Rationale: An open-ended split has no end and can break behavior without a gain.
- Task rule: ONE split task per ranked method or file, added after the ranked cause list is done. There is no fixed count.

## Decision 5d: Project split, re-measure, and proof order

- Decision: A project split is a last resort (FR-013). Trigger: the ranked list names a project-wide cause, and the clean build is 4 minutes or more after the method and file splits. After it, run the FR-007 checks again and change `docs/architecture.md` with a new "Last reviewed" date. The final measurement runs after all splits and measures the kept settings and packages again; an unconfirmed decision is reverted (FR-014). The CI proof (SC-006) is the last step, after the result record, the binlog cleanup, the doc links, and the status update.
- Rationale: A split changes the dependency graph and the code shape. A CI run on an earlier commit does not prove the final commit. Constitution Principle V needs the status and architecture updates in the same PR.

## Decision 6: Maintainer guard

- Decision: A guide (`docs/build-time.md`) and a script (`scripts/measure-build.ps1`) give a repeatable check against the stated limit. No CI failure gate on time.
- Rationale: CI runner time is noisy. A hard gate gives false alarms. The guide plus the script are enough for US3.
- Alternatives considered: A CI time gate. Rejected for noise.

## Decision 7: Scope of "build"

- Decision: The default `dotnet build GameBot.sln` in the default configuration. CI Release build is checked for no new warnings only.
- Rationale: Spec clarification.

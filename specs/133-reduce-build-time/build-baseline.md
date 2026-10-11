# Build Baseline: Reduce Build Time

All numbers in this file come from real runs on the PC below. Time unit is seconds unless the text says other.

## Build baseline

- pc: AMD Ryzen 5 3600 (6 cores, 12 logical), 47.9 GB RAM, Samsung SSD 980 PRO 2 TB (NVMe SSD, drive C:)
- load note: on this PC, 4 LDPlayer emulator processes and the live GameBot service run all the time. The CPU use was 70 to 80 percent with no build running. Timings are therefore noisy: the same code gave 131 to 199 seconds in three clean runs (see below). Read every single-run difference below about 35 seconds as noise.
- sdk: 9.0.314
- state: "clean" means no `bin` or `obj` folder under `src` and `tests`, packages restored, build servers shut down before the run.
- command: `dotnet build C:\src\GameBot\GameBot.sln` (Debug, no extra option). The tool script is outside the repo; the timing is the wall time of this command.
- result: 0 warnings, 0 errors in all runs.

### Test baseline (T002)

Command: `dotnet test C:\src\GameBot\GameBot.sln`, unchanged code, contract test data folders `execution-logs`, `queues`, `queue-templates` cleared first (see the test harness note: the contract tests share a data folder).

| Test project | Passed | Failed | Skipped | Total | Duration |
|--------------|--------|--------|---------|-------|----------|
| GameBot.UnitTests | 2936 | 0 | 0 | 2936 | 2 min 30 s |
| GameBot.IntegrationTests | 656 | 0 | 1 | 657 | 5 min 41 s |
| GameBot.ContractTests | 726 | 0 | 0 | 726 | 7 min 21 s |

Total: 4318 passed, 0 failed, 1 skipped. The skipped test is `ConcurrentQueueRunTests.ARunBlockedByTheSessionLimitRecordsAnActionableReason` (skipped in the code). There is no failing test in the baseline.

### Clean build runs (T004 to T006)

| Run | Seconds |
|-----|---------|
| 1 | 131.6 |
| 2 | 178.8 |
| 3 | 199.4 |

Worst: 199.4 s (3 min 19 s). Median: 178.8 s (2 min 59 s). The limit of the spec is: worst under 240 s, median 180 s or less. The baseline on this PC is already inside both limits, so SC-003 (a 30 percent fall) does not apply (it applies only if the baseline is above 4 minutes).

### Web UI and duplicate build check (T008)

- No web UI step is in the default build. The solution has 8 projects (Domain, Emulator, Service, Updater, UnitTests, IntegrationTests, ContractTests, TesseractStub). No project has an `Exec` task, a custom target, or a reference to `src\web-ui`. There is no `Directory.Build.targets`.
- Each project builds one time. The `Csc` task ran 8 times for 8 projects (binlog, `CoreCompile` 8 calls).
- No build step has an effect that is not needed (FR-003). No duplicate build was found (FR-005).

## Project timing

Source: one clean build with `/bl /p:ReportAnalyzer=true -clp:PerformanceSummary` (164.7 s wall). The project times are inclusive of the project references.

| Project | Inclusive time (s) | Own time (s, estimate) | On critical path |
|---------|--------------------|------------------------|------------------|
| TesseractStub | 7.0 | 7.0 | no |
| GameBot.Updater | 7.3 | 7.3 | no |
| GameBot.Domain | 36.3 | 36.3 | yes |
| GameBot.Emulator | 38.1 | 2 | yes |
| GameBot.Service | 143.5 | about 105 | yes |
| GameBot.ContractTests | 156.9 | about 13 | yes (parallel with 2 other test projects) |
| GameBot.IntegrationTests | 158.4 | about 15 | yes (parallel) |
| GameBot.UnitTests | 162.1 | about 19 | yes (last) |

The critical path is Domain, then Emulator, then Service, then the test projects (parallel). `Csc` (the C# compiler task, with the analyzers) took 194.7 s of summed time over 8 projects, and the target `CoreCompile` took 194.7 s. Restore, copy, and the other targets took under 2 s each. Nearly all the build time is `Csc`.

## Analyzer cost

Source: the `ReportAnalyzer` table in the binlog, in CPU seconds (the analyzers run in parallel, so CPU seconds are more than wall seconds).

| Project | Total analyzer CPU (s) | Main part |
|---------|------------------------|-----------|
| GameBot.Service | 513.9 | NetAnalyzers 486.0 of it: CA2000 126.8; taint rules CA3006 62.9, CA3011 41.9, CA3012 40.3, CA3003 39.2, CA3007 38.8, CA3001 34.3, CA3010 32.3, CA3009 31.4, CA3008 28.5 |
| GameBot.Domain | 243.8 | NetAnalyzers 234.3 of it: CA2000 213.0; CA1062 12.0 |
| GameBot.UnitTests | 54.9 | CA2000 16.0, CA1307/CA1310 9.2 |
| GameBot.IntegrationTests | 33.2 | CA2000 14.7 |
| GameBot.ContractTests | 28.0 | CA2000 13.2 |
| GameBot.Emulator | 3.5 | small |
| TesseractStub, Updater | 3.9, 9.2 | small |

`Microsoft.CodeAnalysis.Metrics` (3.3.4): no analyzer of this package appears in the `ReportAnalyzer` table with measurable time (the only metrics rule, `CodeMetricsAnalyzer`, is part of NetAnalyzers and costs under 0.001 s). The measured share of the package is 0.

Floating `*` package versions (3 packages in `GameBot.Emulator`): the restore step took about 1.4 s for the whole solution in the binlog run. The share of the clean build is under 1 percent. Pinning them is still done for a stable CI cache key (FR-014 check at the end).

## Cause

### Ranked cause list (T009)

Share is the share of the 164.7 s clean build in the binlog run (wall time on the critical path).

| Id | Kind | Measured cost | Share | Planned action | Target |
|----|------|---------------|-------|----------------|--------|
| C1 | code structure / analyzers in `GameBot.Service` | about 105 s own time; analyzer CPU 514 s (CA2000 127 s, nine taint rules 28 to 63 s each) | about 64 % | Test the large methods of the project: `QueueExecutionService.RunAsync` (580 lines), `SequenceExecutionService.ExecuteCoreAsync` (349 lines), `MapQueueEndpoints` (340 lines), `MapCommandEndpoints` (245 lines) | Service project build under 85 s |
| C2 | code structure / analyzers in `GameBot.Domain` | 36 s inclusive (25 to 33 s alone); analyzer CPU 244 s, of which CA2000 213 s | about 22 % | Split `SequenceRunner.ExecuteSingleStepAsync` (345 lines) into named methods | Domain alone under 23 s |
| C3 | graph (serial chain) | Domain, Emulator, Service, tests are a chain; the three test projects add about 19 s after Service | about 12 % | None (a project split is the last resort, FR-013) | none |
| C4 | package: `Microsoft.CodeAnalysis.Metrics` | 0 s measured analyzer time | 0 % | Remove and measure (T012) | no change expected |
| C5 | package: three floating `*` versions | restore 1.4 s for the whole solution | under 1 % | Pin to the resolved versions (T011) | none |
| C6 | config: compiler and parallel settings | not measured before | not known | Test the settings (T013 to T016) | none |

The list names no project-wide cause: the analyzer cost is spread over the code of the two big projects. It names the `GameBot.Service` size as a cause, so the FR-013 trigger test (T020) was run (see below).

### What was done for each item

C1, large methods of `GameBot.Service`. Measured with a throwaway copy of the source tree (outside the repo), because the analyzer report has no per-method data. A method was replaced by an empty body in the copy, and `GameBot.Service` was built alone (clean `obj`), 4 runs of each variant, interleaved:

| Variant | Run seconds | Mean |
|---------|-------------|------|
| original | 104.6, 102.9, 100.5, 101.9 | 102.5 |
| `RunAsync` and `ExecuteCoreAsync` removed | 89.5, 88.6, 89.0, 97.7 | 91.2 |

So the largest possible gain from both methods together is about 11 s (11 %) of the Service build, if the code cost nothing. Real splits give only a part of that:

- `QueueExecutionService.RunAsync` was split into about 20 named methods with a state object (all behavior kept). One build of `GameBot.Service`: 97.4 s, then 99.5 s after a funnel for the sequence start; the unsplit code gave 100.4 s. The analyzer CPU did not fall (448 s and 424 s, against 441 s). No gain was measured. The change was reverted.
- The route registration of `QueuesEndpoints` and `CommandsEndpoints` was split into 7 and 3 `Register*` methods. One build: 107.6 s, against 100.4 s. No gain. Reverted.
- The detail-building loops of `SequenceExecutionService.ExecuteCoreAsync` were moved into a static method. One build: 101.8 s. No gain measured. Reverted.

Result for C1: no split gave a gain that is larger than the noise of this PC, so no change was kept in `GameBot.Service`.

C2, `SequenceRunner.ExecuteSingleStepAsync`. Same copy method, `GameBot.Domain` built alone, 5 runs of each variant, interleaved.

| Variant | Run seconds | Mean |
|---------|-------------|------|
| original | 26.3, 25.9, 30.0, 28.5, 32.8 | 28.7 |
| method body removed (upper bound) | 20.7, 19.9, 18.7, 21.0, 19.4 | 19.9 |

The method was then split in the repo into 7 named methods (gate wait, dry-run skip, wait-for-image step, service-level action, primitive action, command step, undispatched command). Domain alone, 5 runs of each variant, interleaved:

| Variant | Run seconds | Mean |
|---------|-------------|------|
| original | 25.6, 24.8, 25.7, 23.7, 25.6 | 25.1 |
| split | 23.7, 22.8, 21.1, 25.5, 21.4 | 22.9 |

The gain is 2.2 s (9 %) for Domain, and the split was faster in 4 of 5 pairs. Analyzer CPU of Domain in one run with `ReportAnalyzer`: 107 s, against 155 to 221 s in 3 unsplit runs. The change is kept. Files: `C:\src\GameBot\src\GameBot.Domain\Services\SequenceRunner.cs`. No analysis rule was changed and no suppression was added.

C4, `Microsoft.CodeAnalysis.Metrics`: removed from `Directory.Build.props`, 2 clean builds: 137.5 s and 132.8 s. The control builds of the same series (same pinned packages, package kept): 159.9, 132.7, 136.0 s. No measured gain. The change was reverted (the package stays).

C5, floating versions: pinned to the versions that restore resolved before the change: `SharpAdbClient` 2.3.23, `Microsoft.Extensions.Options` 10.0.12, `Microsoft.Extensions.Logging.Abstractions` 10.0.12. The restore after the change resolved the same versions. Kept for a stable CI cache key and a stable result of restore. File: `C:\src\GameBot\src\GameBot.Emulator\GameBot.Emulator.csproj`.

C6, compiler and parallel settings (T013 to T016). Clean builds, interleaved with the control builds (control = no option):

| Setting | Run seconds | Control seconds (same series) | Decision |
|---------|-------------|--------------------------------|----------|
| `UseSharedCompilation=false` | 193.7, 225.6 | 159.9, 132.7, 136.0 | slower; default (true) kept |
| `Deterministic=false` | 140.0, 162.8 | same | no gain; default (true) kept |
| `-maxcpucount:6` (the default is all 12 logical cores) | 134.0, 147.5 | same | no gain; not used |
| `BuildInParallel` | not changed | | The default is true. The binlog shows the three test projects build in parallel after `GameBot.Service`. No build step was found that needs it. |

No setting was kept.

Redundant steps (T010): none found. The default build has no web UI step, no `Exec`, no duplicate project build, and no step without effect (see the check above).

### FR-013 trigger (T020)

The clean build is below 4 minutes without a project split (worst 152.8 s in the final series). The trigger is false. T021 and T022 were skipped. `docs/architecture.md` was not changed.

One finding for later: a change in one file of `GameBot.Service` compiles the whole project again and takes about 89 s (comment-only change, measured), because `GameBot.Service` is one project. Only a project split can reduce this. It is outside the limits of this feature (no limit is exceeded).

## Incremental checks (T023 to T025)

| Check | Result |
|-------|--------|
| Build with no source change (T023) | 2.8 to 3.4 s (limit 30 s) |
| Change one file in `GameBot.Domain` (comment line, then undone) | 14.2 s. Only `GameBot.Domain` compiled again. The comment does not change the reference assembly, so the dependent projects were up to date. |
| Change one file in `GameBot.Service` (comment line, then undone) | 88.9 s. Only `GameBot.Service` compiled again. |

## Final clean build (T026 to T029)

State: pinned packages (C5) and the split of `ExecuteSingleStepAsync` (C2). Settings and packages decisions of C4 and C6 were not kept, so nothing needed a revert (T029).

| Run | Seconds |
|-----|---------|
| 1 | 130.7 |
| 2 | 152.8 |
| 3 | 129.1 |

Worst: 152.8 s (2 min 33 s). Median: 130.7 s (2 min 11 s). SC-001 (worst under 4 minutes): met. SC-002 (median 3 minutes or less): met. SC-003 does not apply (the baseline is not above 4 minutes). A one-time run of `scripts/measure-build.ps1` (T031) gave 129.0 s clean and 3.2 s incremental, which agrees with these runs.

## CI pre-check (T034)

- `dotnet build C:\src\GameBot\GameBot.sln -c Release -warnaserror` (the command of `.github\workflows\dotnet.yml`): 0 warnings, 0 errors, 113 s.
- The installer scripts (`scripts\build-installer.ps1`, `scripts\package-installer-payload.ps1`) and the workflow `release-installer.yml` build their own projects with `dotnet build`. The change touches no installer file and no workflow file. The pinned package versions are the versions that were resolved before. The installer build itself was not run.
- The CI proof (T040: push the branch and wait for the CI runs) is not done here. The orchestrator does it.

## Test re-run (T035)

Command: `dotnet test C:\src\GameBot\GameBot.sln` on the final code, contract data folders cleared first.

| Test project | Passed | Failed | Skipped | Baseline |
|--------------|--------|--------|---------|----------|
| GameBot.UnitTests | 2936 | 0 | 0 | 2936 / 0 / 0 |
| GameBot.IntegrationTests | 656 | 0 | 1 | 656 / 0 / 1 |
| GameBot.ContractTests | 726 | 0 | 0 | 726 / 0 / 0 |

The result is the same as the baseline. No test that passed before fails now.

## Guide check (T033)

The guide `docs/build-time.md` was followed from the start: one command (`scripts\measure-build.ps1`) gave the result against the limits. The effort besides the build run was a few minutes (an estimate, not a stop-watch value), under the 10-minute limit of SC-007.

## Result

| Value | Baseline | Final |
|-------|----------|-------|
| Clean build worst of 3 | 199.4 s | 152.8 s |
| Clean build median of 3 | 178.8 s | 130.7 s |
| Fall of the median | | 27 % |
| Incremental build, no change | not measured before the change; 2.8 to 3.4 s after | 2.8 to 3.4 s |
| Tests | 2936 + 656 (1 skipped) + 726 passed, 0 failed | same |

Read this table with the noise note at the top. The three baseline runs ranged from 131.6 to 199.4 s on unchanged code. The control series that ran in the same session as the experiments (pinned packages, no split) gave 159.9, 132.7, and 136.0 s (median 136.0 s). Against that control the final median is 4 % lower, which is inside the noise. The only effect that the repeated, interleaved measurements show is the 9 % gain of `GameBot.Domain` alone (C2).

Both limits were met before the change on this PC (the baseline was already below 4 minutes), so the main finding is: the build is inside the limits, the cost is the analyzers (about 64 % in `GameBot.Service`, about 22 % in `GameBot.Domain`), and it is spread over much code. No single method is the cause. The largest single method in `GameBot.Service` that was removed in a test copy saved at most 11 s (11 %) of the Service build. All analysis rules stay on, so the floor with all rules kept on this PC is about 130 s for a clean build.

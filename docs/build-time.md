# Build Time Guide

This guide is for maintainers. It tells you how long the build must take, what makes it slow, and how to measure it.

## Limits

These limits apply to the developer PC, for the command `dotnet build GameBot.sln` (Debug, no extra option, no tests):

| Check | Limit |
|-------|-------|
| Clean build, worst of 3 runs | under 4 minutes |
| Clean build, median of 3 runs | 3 minutes or less |
| Incremental build, no source change | under 30 seconds |

A "clean build" is a build with no `bin` and no `obj` folder under `src` and `tests`, and restored packages.

Always write down the PC description (CPU, cores, RAM, disk type) with each result. A build on a slower PC, or on a PC that runs other heavy programs, gives larger times.

## What makes the build slow

The compiler with the code analyzers is nearly all of the build time. In the baseline measurement, the C# compiler task (`Csc`) used about 195 seconds of summed time over 8 projects. Restore, copy, and the other steps used under 2 seconds each. The build has no web UI step.

The analyzers run with all rules on: `AnalysisLevel` is `latest-all`, warnings are errors, and `Microsoft.CodeAnalysis.NetAnalyzers` is pinned to one version. The most expensive rules are:

- `CA2000` (dispose objects before losing scope).
- The nine taint-analysis rules `CA3001` and `CA3003` to `CA3012`.

The cost of these rules grows fast with the size of one method body. A lambda or a local function counts for the method that contains it. For this reason a very large method makes the build much slower than the same code in several small methods.

The project graph is a chain: `GameBot.Domain`, then `GameBot.Emulator`, then `GameBot.Service`, then the three test projects. `GameBot.Service` is the largest part. A change in a `GameBot.Service` file compiles the whole project again (about 90 seconds on the measured PC).

The measured details (per-project times, analyzer table, ranked causes, and what was tried) are in `specs/133-reduce-build-time/build-baseline.md`.

## Rules to keep the build time low

1. Do not relax the analysis. Do not lower `AnalysisLevel`, remove `TreatWarningsAsErrors`, add a broad `NoWarn`, or float the `Microsoft.CodeAnalysis.NetAnalyzers` version. The rules must stay on in all configurations.
2. Keep methods small. Do not let one method grow to hundreds of lines. Put new endpoint groups in `Endpoints/*.cs` classes. Put new service registrations in a topical `Register*` method of `GameBotServiceSetup`. Keep `Program.cs` thin.
3. Move lambdas with large bodies into named private static methods.
4. Do not add a build step (`Exec`, custom target, code generation) to the default build unless the step is needed. Measure the build before and after.
5. Pin every package version. Do not use `*` or a floating range. A floating version changes the result of restore and breaks the CI cache key.
6. Do not add a second build of the same project (for example, a project reference with a different property).
7. Measure the build time after a change that adds many lines to one file or adds a package. Compare the result with the limits above.
8. A setting is kept only if a repeated measurement shows a gain that is larger than the noise. In the measured series, `UseSharedCompilation=false` made the build slower, and `Deterministic=false`, `-maxcpucount`, and removal of `Microsoft.CodeAnalysis.Metrics` gave no gain.

## How to measure

Do not run other heavy programs during a measurement (emulators, test runs, another build).

Use the script:

```powershell
& "C:\src\GameBot\scripts\measure-build.ps1" -Runs 3
```

The script restores packages, does the clean step, times the clean builds, times one incremental build, and prints the result against the limits. Use `-Runs 3` for an official result. Without `-Runs` it does one clean build.

Manual steps (the same result):

1. Run `dotnet restore C:\src\GameBot\GameBot.sln`.
2. Delete the `bin` and `obj` folders under `C:\src\GameBot\src` and `C:\src\GameBot\tests`.
3. Run `Measure-Command { dotnet build C:\src\GameBot\GameBot.sln }`.
4. Do steps 2 and 3 three times. Write down the worst and the median.
5. Run the command of step 3 again with no change. This is the incremental time.

To find the cause of a slow build, do this on a clean state:

```powershell
dotnet build C:\src\GameBot\GameBot.sln /bl:C:\src\GameBot\baseline.binlog /p:ReportAnalyzer=true -clp:PerformanceSummary
```

Open the binlog in MSBuild Structured Log Viewer. Read the time of each project, the time of each target, and the analyzer table (the "Total analyzer execution time" table of each `Csc` call). Do not commit binlog files.

Timings on a PC with other load are noisy. The same code gave 131 to 199 seconds in three clean runs on the measured PC. Use the median of 3 runs, and repeat a comparison before you keep a change.

# Research: Reduce CI Run Duration

Each item has a decision, a reason, and the alternatives. Items marked **Measure** need a number from a real run. The team keeps the step only if the gain is larger than the noise (FR-003, FR-006, FR-009).

## R1: How do pushes that need no full run still give a pass signal?

- **Decision**: Keep the `push` trigger with no `paths` filter. Add a `changes` job on `ubuntu-latest`. The job sorts the changed files into groups: `docs`, `webui`, `backend`, `shared`, and unknown. It sets two outputs, `dotnet` and `webui`. Later jobs use these outputs in `if`. A final `ci-gate` job runs with `if: always()`. It passes when every needed job passed or was skipped by a path rule. It fails when a needed job failed or was cancelled.
- **Reason**: A trigger-level `paths-ignore` starts no run. The speckit pipeline polls `gh run list` until a run appears. It would wait for ever. A skipped job counts as a pass, and the run always exists. The `changes` job takes about 10 s.
- **Rule for the default**: A file in no known group sets both outputs to true. These paths are `shared` and set both outputs to true:
  - `.github/workflows/**`
  - `scripts/**`
  - `Directory.Build.props`
  - `GameBot.sln`
  - `global.json`
  - `*.csproj`
  - lock files
- **Alternatives**:
  - `paths-ignore` on the trigger. No run starts, and the pipeline waits.
  - A third-party path-filter action. It adds supply-chain risk. A script is easier to test.
  - Skip by commit message. It is not reliable.

## R2: Can the test path use a faster build? **Measure**

- **Decision**: Build the shared test output with `-p:RunAnalyzers=false`. Run the full build (`-warnaserror`, all analyzers) in a separate parallel job named `analyze`. Keep the split only if the compile job is at least 30 s faster than the current build step.
- **Reason**: The summed `Csc` time is about 195 s. The analyzer rules `CA2000`, `CA3001`, and `CA3003` to `CA3012` are the main cost (`docs/build-time.md`). The `analyze` job keeps the rules at full strictness. A failure of that job fails the run (FR-001, FR-006).
- **Failure rule**: A compile failure stops the test jobs. An `analyze` failure fails `ci-gate` and the run. It does not stop the test jobs. The test jobs do not wait for `analyze`. A wait would put the analyzer time back on the critical path (owner decision).
- **Fallback**: If the team drops the split, one build job holds all analyzers. An analysis warning then fails that job, and the test jobs do not start.
- **Risk**: The `analyze` job and the `compile` job both compile the code. The runner minutes rise. The repository is public, so the owner pays nothing.
- **Alternatives**:
  - One build with analyzers. It is simple but slower.
  - A build cache for `obj`. The compiler output depends on all sources, so the hit rate is low.
  - A split of `GameBot.Service` into projects. It is a large change. `docs/build-time.md` calls it the last resort.

## R3: How do the test jobs get the build output? **Measure**

- **Decision**: The `compile` job uploads one artifact for each test project. The artifact is the `bin/Release/net9.0` folder of the test project. The folder holds the referenced assemblies, the test host files, and the content files, such as the contract snapshots. The integration artifact also holds the `TesseractStub` output. A test job downloads only its artifact. It runs `dotnet test <project>.dll`.
- **Reason**: Each test job starts a new runner, so the output must travel. The test project folder holds what the test host needs. This avoids a second compile and a restore in the test jobs.
- **Measure**: Measure upload plus download time. Suppose the two together are more than 45 s. Then test this alternative: one runner, three test processes at the same time, and the integration tests run from copies of the folder (see R5).
- **Alternatives**:
  - A cache action keyed by commit. It is slower to restore than an artifact.
  - A tar file of the full repository. It is larger.

## R4: Where is coverage needed?

- **Decision**: Collect coverage in the unit job and in the pinned integration shard only. The contract job and the other shards collect none.
- **Reason**: `scripts/check-coverage-thresholds.ps1` reads `tests\unit\coverage.json` for four gates. It reads `tests\integration\TestResults\coverage\coverage.json` for one gate. No gate reads a contract file. Today the workflow collects coverage in all three projects with one `Include` filter.
- **Collector**: The test jobs run `dotnet test <dll>`. This command cannot use `coverlet.msbuild`. The jobs use `coverlet.collector` with a run settings file.
- **Pinned shard**: All tests that cover `EmulatorImageEndpoints` are in one integration shard. The gate then reads full data. The performance gate class `ExecutionLogsPerformanceIntegrationTests` is in the same shard. It runs there as a second test step with `GAMEBOT_PERF_PROFILE: ci`.
- **Where the thresholds run**: The thresholds run in the Linux `ci-gate` job. A separate Windows job would add about 40 s of start time to the critical path. The script takes about 1 s. It must use no backslash paths.
- **Change**: The gate script gets one parameter for the folder of each file, because the files come from two jobs. It reads the collector format. A missing file or a class with no data still fails the gate (FR-012). The script already throws in these cases.
- **Alternatives**: Merge the coverage files in a separate job. It moves more files and gives no gain.

## R5: How to split the integration tests? **Measure**

- **Decision**: Run one per-test timing measurement (R8). Build named shards from folder and namespace prefixes, so each shard has a similar time. The last shard is the **complement**. Its filter is "not in any named shard". A new test class always lands in a shard and is never lost. A script test proves that every test class is in exactly one shard.
- **Reason**: The integration tests run 4 min 1 s serial. One shard on its own runner needs only a share of that time. In-process parallelism is unsafe here (R6). Separate runners have separate environment variables and separate data folders.
- **Number of shards**: Start with 3 for the integration tests. Keep a shard count only if the slowest shard plus its fixed cost is below the next-longest job.
- **Alternatives**:
  - Hash the class name. It balances badly, because the shard times are not known.
  - A third-party sharding tool. It adds a dependency.

## R6: Is in-process parallel test execution safe? **Measure**

- **Decision**: Do not turn it on for the integration or contract tests. Try it for the unit tests only, in one experiment. First, the shared-state scan (FR-004) lists the unit tests that use environment variables, ports, static values, or shared folders. Keep the change only if 3 runs in a row pass and the gain is larger than 20 s.
- **Reason**: `GAMEBOT_*` names and `SetEnvironmentVariable` appear in 236 test files. `ConfigUpdateTests` has 44 uses. Environment variables are global for the process. Project notes record that contract tests share a data folder in `bin`. 81 integration classes use named collections.
- **Alternatives**: Isolate every test per class. It is a large change with high risk, and the gain is small once the jobs are parallel.

## R7: Is a Linux runner possible? **Measure (time-boxed)**

- **Decision**: Do one bounded probe. Run the unit tests only on a Linux runner. Drop the idea if the unit tests do not pass without changes.
- **Reason**: The unit test project references `OpenCvSharp4.runtime.win`. 18 test files use Windows-specific items, such as `.exe`, `LOCALAPPDATA`, drive paths, and `OperatingSystem.IsWindows`. The Service and the installer tests also target Windows. A full move likely needs product changes. That is out of scope. The owner allowed a probe, not a move at any cost.
- **Alternatives**: A larger Windows runner. It is paid and is outside the plan.

## R8: Which tests are slow and which fail by chance?

- **Decision**: Run one diagnostic test run with a `trx` logger. Run it locally, or dispatch the workflow on this branch. Rank the tests by time. Sum the time of the fixed delays. The counts are 112 in unit tests, 66 in integration tests, and 8 in contract tests. 7 files wait 1 s or more. Shorten a delay only when the test stays deterministic. Use a fake clock or an event instead of a sleep.
- **Failure data**: Use `gh run list` and `gh run view --log-failed` for the 16 failed runs of the last 60. Classify each run (FR-010). The project notes name two flaky tests: `MaskedTemplateMatchTests` and `QueueTemplateLink`. Check them first.
- **Alternatives**: Guess from the code. Rejected: measure, do not grep.

## R9: What do the fixed parts cost, and what can drop? **Measure**

- **Decision**: The secret scan becomes part of the `changes` job on Linux. It takes 1 s there, and it fails the run first. The web UI job keeps `npm ci` with the npm cache. Test a cache of `node_modules` only if `npm ci` stays above 60 s in 3 runs. It varies from 19 to 73 s. Pin tools by version, so the cache keys stay stable.
- **Reason**: The fixed parts total 35 to 50 s of setup and 11 to 19 s of restore on the critical path.
- **Alternatives**: Remove the NuGet cache. It saves 11 to 19 s of restore time, so it stays.

## R10: What belongs in the guide?

- **Decision**: `docs/ci-time.md` holds the budget per part, and the baseline and final numbers. It holds the patterns, with a rule for each. It holds the measure steps (`scripts/ci/measure-ci.ps1`) and a check list for a change (FR-013, FR-014).
- **Patterns**:
  - serial tests;
  - long projects on one runner;
  - coverage on all projects;
  - analyzers on the critical path;
  - fixed delays;
  - flaky tests;
  - a full run for docs pushes;
  - queue time read as step time;
  - an extra Windows job for a small gate.
- **Reason**: The build time guide of feature 133 (`docs/build-time.md`) shows a format that works. The two guides link to each other.

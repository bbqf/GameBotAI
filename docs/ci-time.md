# CI Time Guide

This guide tells you how long the `.NET CI` workflow must take, what makes it slow,
and how to measure it. Read it before you change `.github/workflows/dotnet.yml`,
a CI script, or the test layout.

## Time budget

| Part | Budget | Measured (feature 134) |
|------|--------|------------------------|
| Code push, median `.NET CI` time | 4 min or less | 3 min 50 s (see "Final numbers") |
| Code push, worst case | 5 min or less | 3 min 54 s |
| Docs-only or spec-only push | 1 min or less | 23 to 40 s |
| Web-UI-only push | 3 min or less | 2 min 33 s |
| Test time, end of `compile` to end of the last test job | 50 % less than the old test step | 102 s against 239 s (57 % less) |
| Fixed parts on the critical path (checkout, setup, cache, restore, artifact transfer) | 60 s or less | about 45 to 60 s |

The limits cover the `.NET CI` workflow only. CodeQL runs apart from it (default
setup, on pull requests). Its time is not in the numbers above.

### Final numbers

Five full runs of the final layout (queue time included): 232, 230, 222, 228, and 234 s.
Median 230 s, worst 234 s. The runs are in `specs/134-ci-duration-reduction/measurements.md`.

The check on the first 20 runs on `master` after the merge (SC-006: failures from flaky
tests and late failures fall by half) is open. Owner: the repository owner. Date: when
`master` has 20 runs of the new layout. Use `measure-ci.ps1 -Branch master -Limit 20`
and classify each failed run with `gh run view <id> --log-failed`.

## What the workflow does

| Job | Runner | What it does |
|-----|--------|--------------|
| `changes` | Linux | Runs the secret scan. Sorts the changed files into groups. Writes the switches `dotnet` and `webui`. Lists the test shards |
| `compile` | Windows | Restores. Builds with `-p:RunAnalyzers=false`. Uploads one artifact for each test project |
| `analyze` | Windows | Builds with all analyzers and `-warnaserror`. A warning fails the job and the run. It does not stop the test jobs |
| `test-unit` (3 shards) | Windows | Runs the unit tests. The pinned shard collects coverage |
| `test-integration` (4 shards) | Windows | Runs the integration tests. The pinned shard collects coverage and runs the performance gate (`GAMEBOT_PERF_PROFILE: ci`) |
| `test-contract` (6 shards) | Windows | Runs the contract tests. No coverage |
| `web-ui-tests` | Windows | Runs `npm ci` and the Jest tests |
| `ci-gate` | Linux | Runs the five coverage thresholds. Gives the pass or fail result of the run |

`ci-gate` is the merge signal. It always runs. It passes when each needed job passed,
or when a path rule skipped it. It fails on a failed or cancelled job. Master has no
required status check today, so a person must read this result.

A compile failure stops the test jobs. A secret found by the scan stops the run.

### Path groups

The trigger has no `paths` filter. A run must exist for every push, because the speckit
pipeline waits for a run of the head commit. The `changes` job switches jobs off instead.
`scripts/ci/get-changed-groups.ps1` holds the rules. A skipped job counts as a pass.

| Group | Paths | .NET jobs | Web UI job |
|-------|-------|-----------|------------|
| `docs` | `specs/**`, `docs/**`, `**/*.md`, `.specify/**`, `.claude/**`, `.github/agents/**`, `.github/prompts/**` | skip | skip |
| `webui` | `src/web-ui/**` | skip | run |
| `backend` | `src/GameBot.*/**`, `tests/**`, `installer/**` | run | skip |
| `shared` | `.github/workflows/**`, `scripts/**`, root build files, project files, lock files | run | run |
| unknown | Any path that no group names | run | run |

The result is the union over all changed files. An empty file list, a manual run, and a
force push run all jobs.

### Test shards

`scripts/ci/test-shards.json` lists the shards of each test project. Each shard has a list
of `include` prefixes. The last shard of a project is the complement: it runs every test
that no named shard names. A new test class runs in the complement shard until you give it
a named place. A unit test proves that each test class is in exactly one shard
(`tests/unit/Ci/TestShardTests.cs`).

Some classes are pinned. They must stay in one shard:

- The tests that cover `EmulatorImageEndpoints` are in `integration-pinned`. The shard
  collects the coverage for that gate.
- The performance class `ExecutionLogsPerformanceIntegrationTests` is in the same shard.
  It runs a second time with the `ci` profile.
- The tests that cover the unit coverage gates are in `unit-3`.

When you add a shard, keep the slowest shard plus its fixed cost (about 25 s) below the
longest other job.

### Shard balance rule

This rule is binding. A shard whose local run time is more than 20 % above the average
local run time of all shards of the same project MUST be split again. Compare each shard
only with the shards of its own project (unit, contract, or integration).

- **Limit**: average of the shard times of the project, times 1.2.
- **Measure**: build the solution in Release, then run
  `& "C:\src\GameBot\scripts\ci\measure-shards.ps1" -Project contract`
  (or `unit`, or `integration`). The script runs each shard on your PC with its filter. It
  runs a shard that is above the limit one more time and keeps the lower time. It prints the
  times, the average, the limit, and one `Split <shard>` line for each shard above the limit.
  Exit status 0 means the rule is met. Exit status 1 means a split is required.
- **When**: measure when a change adds many tests, when it changes
  `scripts/ci/test-shards.json`, or when CI is slow. Write the result in the pull request.
- **Action**: if a shard is above the limit, split it in the same change. Move the heavy
  class prefixes to a new shard in `scripts/ci/test-shards.json`. Keep the last shard as the
  complement. Run the script again. Do not add a shard if the slowest shard plus its fixed
  cost (about 25 s) is not lower than before.
- **Check**: CI does not run the script. Local times and CI times are different, so the rule
  uses the local times. The pull request template asks you for the result.

To find the heavy classes, use step 4 of "How to measure".

Contract shards after feature 172: the old `contract-4` ran 3.1 times the average. It is now
three shards (`contract-4`, `contract-5`, `contract-6`). The measured times are in
`specs/172-ci-test-shard-split/measurements.md`.

## Patterns that make CI slow

| Pattern | Measured effect | Rule |
|---------|-----------------|------|
| Analyzers on the critical path | The build took 142 s with all analyzers and 35 s without | Build for the tests with `-p:RunAnalyzers=false`. Run the full build in `analyze`, next to the tests |
| Serial tests in one job | The three projects took 239 s together | Run the projects in separate jobs. Split a project into shards |
| One long project on one runner | The contract tests took 409 s on a developer PC | Shard it. Each shard costs about 25 s of fixed time |
| Coverage on all projects | Coverage cost 50 s or more on one shard | Collect coverage only where a gate reads it. Use `coverlet.collector` with `scripts/ci/coverage.runsettings` |
| A coverage run on a big shard | The pinned integration shard took 150 s with coverage and the complement tests | Pin the gate tests in a small shard |
| Fixed delays in tests | Two single tests wait 15.7 s and 10.6 s (the timeout is the behavior under test) | Use a fake clock, an event, or a short bounded wait. Keep a real timeout only if the timeout is what the test checks |
| Flaky tests | 8 of the 16 failed runs in the last 60 runs were flaky tests | Find the cause. Fix it, isolate the test, or record an owner and a reason |
| A full run for a docs push | A docs-only push took 7 min | Use the path groups. A skipped job counts as a pass |
| Queue time read as step time | Queue time is 3 s median and 84 s worst | Read the queue time apart from the step time (`scripts/ci/measure-ci.ps1`) |
| An extra Windows job for a small gate | A Windows job adds about 40 s of start time | Run small checks in the Linux `ci-gate` job |
| A wait that ends on a value that other states also have | A test ended its wait at the second of four bookings | End a wait only on the state you check |

## How to measure

1. Run `& "C:\src\GameBot\scripts\ci\measure-ci.ps1" -Limit 20 -Branch master -Conclusion success`.
   Add `-Steps` to see each step.
2. Read `QueueSec` and `CriticalPathSec` apart. Queue time is the wait for a runner.
   Critical path is the time from the first job start to the last job end.
3. The slow part is the job with the longest time plus its start. In the new layout this is
   `compile` followed by the slowest test shard.
4. To rank tests, run a project with `--logger "trx;LogFileName=<name>.trx"` and a results
   folder outside the repository. Sort the `UnitTestResult` nodes by `duration`.
5. For a change, compare 5 runs before and 5 runs after. Use the median and the worst value.

The numbers of two measurements of the same code are different by up to 10 %. A gain smaller
than that is noise. Do not keep a change for a gain smaller than the noise.

## Check list for a change

Ask these questions before you merge a change to the workflow, a CI script, or the tests:

1. Does the change add a second build of the same code?
2. Does it add a serial test group or a fixed delay?
3. Does it add coverage to a project that has no gate?
4. Does it add a new trigger that starts a full run?
5. Does it put a new job on the critical path (after `compile`, before `ci-gate`)?
6. Does it remove a check, a gate, or a scan? This is not allowed to save time.

## Linux runner probe

Result of the probe (feature 134): the unit tests ran on `ubuntu-latest`. 290 of 2971 tests
failed. The causes are Windows-only parts (GDI+ `PInvokeGdiPlus` and other Windows APIs). A
move to Linux needs product changes, so the idea is dropped. See
`specs/134-ci-duration-reduction/measurements.md`.

## Related

- `scripts/ci/measure-ci.ps1`: reads past runs.
- `scripts/ci/measure-shards.ps1`: runs the shards on your PC and checks the balance rule.
- `scripts/ci/get-changed-groups.ps1`, `scripts/ci/get-test-shard-filter.ps1`,
  `scripts/ci/evaluate-gate.ps1`: the scripts that the workflow calls.
- `specs/134-ci-duration-reduction/`: the spec, the plan, and the measurements.

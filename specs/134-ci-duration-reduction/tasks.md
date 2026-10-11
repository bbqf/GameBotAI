---

description: "Task list for feature 134: reduce CI run duration"
---

# Tasks: Reduce CI Run Duration

**Input**: Design documents from `/specs/134-ci-duration-reduction/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/workflow-contract.md, quickstart.md

**Tests**: The plan asks for script tests for the path groups and the shard completeness. Those tasks are included. Real runs on the pushed branch prove the workflow.

**Organization**: Tasks are grouped by user story. The workflow file `.github/workflows/dotnet.yml` is one file. Tasks that edit it are NOT marked [P].

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel. It uses different files and has no dependency on an incomplete task.
- **[Story]**: The user story of the task (US1 to US5).
- Use absolute paths when you run PowerShell. Write all new text in STE.
- A research step marked **Measure** stays only if the measured gain is larger than the noise.
- Record every result in `specs/134-ci-duration-reduction/measurements.md`.

## Phase 1: Setup and Baseline

**Purpose**: Get the numbers that every later decision needs.

- [X] T001 Create `scripts/ci/measure-ci.ps1`. It reads past runs with `gh run list` and `gh run view --json jobs`. It prints queue time, step time per job, critical path, and result for the last N runs. Parameters: `-Workflow`, `-Limit`, `-Branch`
- [X] T002 Run `measure-ci.ps1` for the last 20 successful runs on `master`. Write the baseline table to `specs/134-ci-duration-reduction/measurements.md`. Show the median and worst value per part. Show queue time apart
- [X] T003 Run all three test projects locally with a `trx` logger. Use `dotnet test` with `--logger "trx;LogFileName=<name>.trx"` and a results folder outside the repository. Rank the tests by time. Write the 30 slowest tests of each project to `specs/134-ci-duration-reduction/measurements.md`. Write the total time of fixed delays there also
- [X] T004 Classify the 16 failed runs of the last 60. Use `gh run view <id> --log-failed`. The classes are real-defect, flaky-test, build-warning, infrastructure, and other. Write the table to `specs/134-ci-duration-reduction/measurements.md`. List the tests that failed in more than one unrelated run

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The two scripts that the workflow uses, and the tests that prove them.

**CRITICAL**: The workflow tasks in Phases 3 and 4 need these scripts.

- [X] T005 [P] Create `scripts/ci/get-changed-groups.ps1`. Input: a list of files (`-Files`) or a base and head commit. Output: `dotnet=<bool>`, `webui=<bool>`, and `reason=<text>`. Use the groups in `data-model.md`. A `shared` path sets both values to true. A path in no group sets both to true. An empty file list sets both to true
- [X] T006 [P] Create `tests/unit/Ci/ChangedGroupsTests.cs`. Use CamelCase test names with no underscores. Cover these cases. Docs only. Spec only. Web UI only. Backend only. Web UI plus backend. Workflow file. `scripts/` file. Root build file. Project file. Lock file. Unknown path. Empty list
- [X] T007 Run the tests of T006. Fix `get-changed-groups.ps1` until all cases pass
- [X] T008 [P] Create `scripts/ci/get-test-shard-filter.ps1`. Input: `-Project` and `-Shard`, or `-List`. Output: the test filter string. The shards come from the manifest `scripts/ci/test-shards.json`. The last shard of a project is the complement of all named filters
- [X] T009 Create `scripts/ci/test-shards.json` with the first split of the integration tests into 3 shards. Use the timing data of T003. The first split is a start value, and T019 sets the real balance. Pin two groups to one named shard: all tests that cover `GameBot.Service.Endpoints.EmulatorImageEndpoints` (the integration coverage source), and the class `ExecutionLogsPerformanceIntegrationTests` (the performance gate). Add `unit` and `contract` as one shard each
- [X] T010 [P] Create `tests/unit/Ci/TestShardTests.cs`. Find the test classes of each test assembly by reflection. Do not start `dotnet test` from inside a test. Check that every class is in exactly one shard. Check that a made-up new class falls into the complement shard. Check that the pinned classes are in the pinned shard
- [X] T011 Run the tests of T010. Fix `get-test-shard-filter.ps1` and `test-shards.json` until the tests pass

**Checkpoint**: Both scripts are proved by tests. The workflow can use them.

---

## Phase 3: User Story 1 - Fast feedback on a normal code push (Priority: P1) MVP

**Goal**: A code push gets all checks in 4 minutes or less (median). All tests, gates, and scans stay.

**Independent Test**: Push a small code change 5 times. Measure with `measure-ci.ps1`. The median is 4 minutes or less. The test counts equal the baseline. All gates run.

### Implementation for User Story 1

- [X] T012 [US1] In `.github/workflows/dotnet.yml`, fix the final job names: `changes`, `compile`, `analyze`, `test-unit`, `test-integration`, `test-contract`, `web-ui-tests`, and `ci-gate`. Search the docs and notes for the old check name `build`. Update each hit
- [X] T013 [US1] In `.github/workflows/dotnet.yml`, split the old `build` job into `compile` and `analyze`. The `compile` job restores, builds with `-p:RunAnalyzers=false`, and uploads one artifact per test project. The `analyze` job builds with all analyzers and `-warnaserror`. Measure the compile time. Keep the split only if `compile` is at least 30 s faster than the old build step (research R2). If you drop the split, keep one build job with all analyzers (FR-007 fallback). Record the result
- [X] T014 [US1] In `.github/workflows/dotnet.yml`, add the jobs `test-unit`, `test-contract`, and `test-integration`. The `test-integration` job is a matrix over the shards of `test-shards.json`. Each job downloads its artifact. Each job runs `dotnet test <dll>` with its shard filter. Measure upload plus download time. If it is above 45 s, try the one-runner variant of research R3. Keep the better variant
- [X] T015 [US1] Move coverage collection to `test-unit` and to the pinned integration shard. Use `coverlet.collector` with a run settings file (`--collect`, `--settings`). The reason: `dotnet test <dll>` cannot use `coverlet.msbuild`. Set the module filter, the class filter, the output format, and the output path in the run settings. Remove coverage from `test-contract` and from the other shards (research R4). Upload the coverage files as artifacts
- [X] T016 [US1] In the pinned integration shard job, add a second test step for the performance gate. Set `GAMEBOT_PERF_PROFILE: ci`. Use the filter `ExecutionLogsPerformanceIntegrationTests`. The other shards must not run this class
- [X] T017 [US1] Change `scripts/check-coverage-thresholds.ps1`. It must read the collector files from folders that you pass as parameters. It must run on Linux (no backslash paths). Keep the rule that a missing file or a class with no data fails the gate. Check that the five gates give the same percent as a local run. Check the negative case: delete a coverage file and confirm that the gate fails
- [X] T018 [US1] In `.github/workflows/dotnet.yml`, run the coverage thresholds in the Linux `ci-gate` job. The job downloads the two coverage artifacts and runs `check-coverage-thresholds.ps1` with `pwsh`. Reason: a separate Windows job adds about 40 s of start time to the critical path (analysis C2)
- [X] T019 [US1] Make a compile failure stop the test jobs, with job `needs`. Make an analysis warning fail only `analyze` and `ci-gate` (FR-007). Test with a test branch. Push one deliberate compile error: `compile` fails and the test jobs do not start. Push one deliberate analysis warning: `analyze` and `ci-gate` fail and the run fails. Delete the test branch after
- [X] T020 [US1] Experiment: turn on in-process parallel tests for the unit tests only. The setting is in `tests/unit/xunit.runner.json`. First scan the unit tests for shared state: environment variables, ports, static values, and shared folders. List the result in `measurements.md`. Keep the change only if 3 runs in a row pass and the gain is above 20 s. Otherwise revert
- [X] T021 [US1] Re-balance the integration shards in `scripts/ci/test-shards.json`. Use the times of the first real runs. The slowest shard must be close to the other jobs. Keep the shard count only if the slowest shard plus its fixed cost is below the longest other job. Keep the pinned tests of T009 together in one shard
- [X] T022 [US1] Compare the test counts of the first full run to the baseline in SC-004. Count unit, integration, contract, and performance tests. Record the numbers in `specs/134-ci-duration-reduction/measurements.md`

**Checkpoint**: A code push runs in the new shape. The numbers are in `measurements.md`.

---

## Phase 4: User Story 2 - No wasted run for a push that cannot change a result (Priority: P2)

**Goal**: A docs-only push or a spec-only push finishes in 1 minute or less. A web-UI-only push skips the .NET work.

**Independent Test**: Push a docs-only branch, a web-UI-only branch, and a backend-only branch. Check which jobs run. Check that every run ends in success.

- [X] T023 [US2] In `.github/workflows/dotnet.yml`, add the `changes` job on Linux. It checks out with enough history. It calls `get-changed-groups.ps1` for the pushed range. It runs `scripts/installer/run-security-scans.ps1`. It sets the outputs `dotnet` and `webui`. For a new branch, compare to the default branch. For a force push, run all jobs
- [X] T024 [US2] In `.github/workflows/dotnet.yml`, add `if` rules to the .NET jobs: `needs.changes.outputs.dotnet == 'true'`. Add an `if` rule to `web-ui-tests`: `needs.changes.outputs.webui == 'true'`. Remove the secret scan from the old build job
- [X] T025 [US2] In `.github/workflows/dotnet.yml`, complete the `ci-gate` job. It has `if: always()` and needs all jobs. It passes if each needed job passed or was skipped by a path rule. It fails on a failed or cancelled job. If the logic is in a script, add a unit test for the pass and fail rules
- [X] T026 [US2] Prove each path rule with a real push on a test branch. Push a docs-only change, a spec-only change, a web-UI-only change, a backend-only change, and a web UI plus backend change. Push a workflow-file change and an unknown path. Check the jobs and the times against SC-005. Also push twice quickly to one branch. Check that the first run is cancelled and that each push makes one run (FR-011). Record the CodeQL time of a docs-only pull request apart. Record the run IDs and times in `measurements.md`. Delete the test branches after
- [X] T027 [US2] Check that the speckit pipeline still finds and watches the run for a docs-only head commit. Use `gh run list --branch <b> --commit <sha>` and `gh run watch --exit-status`. If the wait fails, fix the workflow. Do not change the pipeline

**Checkpoint**: Real runs prove all path rules.

---

## Phase 5: User Story 3 - Fewer repeated runs from unstable tests and late failures (Priority: P2)

**Goal**: Flaky tests and avoidable delays no longer cost reruns.

**Independent Test**: Each test in the flaky list is fixed, isolated, or recorded with a reason. The guide shows the failure classes.

- [X] T028 [US3] Take each test named in T004. Include `MaskedTemplateMatchTests` and the queue template link tests. Find the cause. Fix the test so that it is deterministic. If a fix is not possible, isolate the test in a serial group. Or record an owner and a reason in `measurements.md`
- [X] T029 [P] [US3] Take the 10 slowest tests of each project from T003 whose time comes from a fixed delay. Replace the delay with a fake clock, an event, or a shorter bounded wait. Find the files in the ranking in `specs/134-ci-duration-reduction/measurements.md`. The files are under `tests/unit`, `tests/integration`, and `tests/contract`. Change a delay only if the test stays deterministic. Record each change and the time gained
- [X] T030 [US3] Run the full test suite `C:\src\GameBot\GameBot.sln` locally 3 times after T028 and T029. All tests must pass. The number of tests must equal the baseline

---

## Phase 6: User Story 4 - A written record of what makes CI slow (Priority: P1)

**Goal**: The guide lists the budget, the patterns, the rules, and the measure steps.

**Independent Test**: A maintainer follows the guide and measures a run. The numbers are within 20 % of the recorded numbers.

- [X] T031 [US4] Create `docs/ci-time.md` in STE. Include the time budget per part. Include the baseline and final numbers from `measurements.md`. Include the job table and the path-group table. Include the sharding rule: a new test runs in the complement shard
- [X] T032 [US4] In `docs/ci-time.md`, list each pattern that raises the time. Give the measured effect and a rule for each. Use the pattern list in research R10. Add the patterns that the measurements show
- [X] T033 [US4] In `docs/ci-time.md`, state three facts. `ci-gate` is the merge signal. A docs-only push cannot change a test result. CodeQL runs apart from the workflow. Name the date and the owner of the SC-006 check on the first 20 runs on `master`
- [X] T034 [US4] In `docs/ci-time.md`, add the measure steps. Use `scripts/ci/measure-ci.ps1`. Explain how to read queue time versus step time. Add the check list for a change (FR-014). Ask these questions. Does the change add a second build? Does it add a serial test group or a fixed delay? Does it add coverage to a project with no gate? Does it add a new full-run trigger?
- [X] T035 [P] [US4] Add a link to `docs/ci-time.md` in `README.md`. Add a link from `docs/build-time.md` to the new guide

---

## Phase 7: User Story 5 - Small remaining overhead is removed (Priority: P3)

**Goal**: The fixed parts on the critical path use 60 seconds or less.

**Independent Test**: Compare the fixed-part times of runs before and after.

- [X] T036 [US5] Read the fixed-part times of the new runs with `measure-ci.ps1`. The parts are setup, cache restore, restore, and artifact transfer. List each part with a possible gain of 5 s or more in `measurements.md`
- [X] T037 [US5] In `.github/workflows/dotnet.yml`, apply the changes for the parts of T036 that show a gain in a repeated measurement. For the web UI job, test a cache of `node_modules`. Do this only if `npm ci` stays above 60 s in 3 runs. Keep package versions pinned, so the cache keys stay stable. Revert any change without a clear gain
- [X] T038 [US5] Check that a change in a project file rebuilds the caches. Check the same for `package-lock.json`. The run must still pass. Record the run IDs in `measurements.md`

---

## Phase 8: Optional Probe - Linux Runner (research R7)

**Purpose**: One bounded probe. Stop at the first sign that product changes are needed.

- [X] T039 Run the unit tests once on a Linux runner on a test branch. Record how many tests fail and why. Keep the idea only if all tests pass without test or product changes, and the gain is above the noise. Otherwise write the reason in `measurements.md` and in `docs/ci-time.md`. Delete the test branch after

---

## Phase 9: Polish and Final Proof

**Purpose**: Final measurement, documents, and cleanup. Do the CI proof last.

- [X] T040 Write a before and after list of all checks, gates, and scans to `measurements.md` (SC-008). The list has the secret scan, the warnings-as-errors build, and the unit, integration, and contract tests. It has the performance gate, the five coverage gates, and the web UI tests
- [X] T041 Check `docs/architecture.md`. Change it only if a described behavior changed. Note the result in `measurements.md`
- [X] T042 Set the `Status` line in `specs/134-ci-duration-reduction/spec.md` to `Implemented`. Add the line for 134 to `specs/STATUS.md`. If `docs/build-time.md` or an earlier CI spec describes the old workflow shape, mark it as iterated by 134
- [X] T043 Run `& "C:\src\GameBot\scripts\ci\measure-ci.ps1"` for at least 5 pushes of the final branch state. Write the median and the worst value to `docs/ci-time.md` and `measurements.md` (SC-001, SC-002). For SC-003, use the time from the end of `compile` to the end of the last test job. Compare it with the old test step. Suppose the median is above 4 minutes. Then record the measured floor and its cause. Record the end time of the CodeQL jobs next to the workflow time
- [ ] T044 Give the guide to a maintainer who did not write it. Let them repeat one measurement. Record whether the numbers are within 20 % (SC-007)
- [X] T045 Remove temporary files and test branches. Check `git status` for stray folders: `TestResults`, `*.trx`, and binlog files
- [ ] T046 Push the branch and open the pull request, if none exists. Wait for the final CI run to pass on the last commit. Use the run result of `.github/workflows/dotnet.yml` as the proof. Any later change needs a new run. This is the last task

---

## Dependencies and Execution Order

### Phase Dependencies

- Phase 1 has no dependency. T003 gives the data for T009, T021, and T029.
- Phase 2 needs T003, for T009 only. It blocks Phases 3 and 4.
- Phase 3 (US1) needs Phase 2. It is the MVP.
- Phase 4 (US2) needs Phase 2 and the job names from T012. T023 to T025 edit the same workflow file after T013 to T018.
- Phase 5 (US3) needs T003 and T004. It can run in parallel with Phases 3 and 4. T030 must finish before T046.
- Phase 6 (US4) needs the numbers from Phases 3, 4, 5, and 7. Write the draft early. Fill in the numbers last.
- Phase 7 (US5) needs the runs from Phase 3.
- Phase 8 is independent after Phase 2. It can run at any time.
- Phase 9 comes last. T046 is the final task.

### Within Phase 2

- T005, T006, T008, and T010 are [P]. They use different files.
- T007 follows T005 and T006.
- T011 follows T008, T009, and T010.

### Parallel Opportunities

- T001 with T003 and T004. They write different outputs.
- T005, T006, T008, and T010. They are different files.
- T028 and T029 with all workflow tasks in Phases 3 and 4.
- T035 with T031 to T034. It is a different file.

## Parallel Example: Foundational Scripts

```text
Task: "Create scripts/ci/get-changed-groups.ps1"                 (T005)
Task: "Create tests/unit/Ci/ChangedGroupsTests.cs"               (T006)
Task: "Create scripts/ci/get-test-shard-filter.ps1"              (T008)
Task: "Create tests/unit/Ci/TestShardTests.cs"                   (T010)
```

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1: baseline (T001 to T004).
2. Phase 2: scripts and their tests (T005 to T011).
3. Phase 3: new job shape and sharded tests (T012 to T022).
4. Stop and check. Measure 5 runs. This delivers most of the gain.

### Incremental Delivery

1. Add US2: path rules and `ci-gate`. Docs and web UI pushes get fast.
2. Add US3: flaky tests and delays. Reruns fall.
3. Add US4: the guide. Fill in the numbers as they arrive.
4. Add US5: fixed overhead. Run the Linux probe if time allows.
5. Do the final proof (Phase 9).

## Notes

- Push only to `134-ci-duration-reduction` and to named test branches. Delete the test branches.
- Do not remove a check to save time (FR-001). Do not relax the analysis rules.
- After each workflow change, wait for the real run. A local dry run is only a pre-check.
- Stop and ask the owner if a measured result makes a spec requirement impossible.

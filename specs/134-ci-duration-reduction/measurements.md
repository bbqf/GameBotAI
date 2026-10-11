# Measurements: Reduce CI Run Duration

All times are in seconds unless a unit is given. Source: `scripts/ci/measure-ci.ps1`.

## Baseline (T002)

The last 20 successful runs of `.NET CI` on `master` (run 36639041581 to run 38124572483).

| Part | Median | Worst |
|------|--------|-------|
| Run time, first job start to last job end | 445 | 549 (see note) |
| Queue time, push to first job start | 3 | 84 |
| `build` job | 440 | 549 |
| `build`: Build (all analyzers, `-warnaserror`) | 142 | 166 |
| `build`: Test with coverage | 239 | 339 |
| `build`: Restore | 12 | 16 |
| `build`: Cache NuGet packages | 13 | 28 |
| `build`: Checkout | 7 | 21 |
| `build`: Setup .NET | 3 | 4 |
| `build`: Performance gate | 4 | 10 |
| `build`: Coverage gates | 1 | 2 |
| `build`: Secret scan | 1 | 4 |
| `web-ui-tests` job | 81 | 137 |
| `web-ui-tests`: npm ci | 26 | 83 |
| `web-ui-tests`: Frontend tests | 30 | 35 |

Note: run 36861980204 shows 4698 s. It is a re-run that waited for a manual action. It is not a CI time. Without it, the worst value is 549 s.

The `build` job is the longest job in every run. Queue time is small (3 s median), so step time is the cost.

## Per-test times (T003)

Source: one local run of each project in Release with a `trx` logger (2026-10-11). The local PC is slower than the CI runner, so use the ratios, not the absolute times.

| Project | Tests | Wall time (local) | Sum of test times |
|---------|-------|-------------------|-------------------|
| Unit | 2936 (1 local failure, see below) | 101 s | 102 s |
| Integration | 657 (1 skipped) | 330 s | 330 s |
| Contract | 726 | 409 s | 408 s |

The tests run fully serial, so wall time equals the sum. The contract tests are the longest project. The time is spread over many small tests (about 0.5 s each, mostly the start of the test host). Few tests have a long fixed delay.

The local unit failure is `BackgroundCaptureScreenSourceTests.DoesNotCallAdbDirectly`. It passes on CI. It is a local-environment difference and not part of this feature.

Slowest tests (seconds):

| Project | Test | Time |
|---------|------|------|
| Contract | `NotificationTestSendContractTests.ATargetThatDoesNotAnswerEndsAt15SecondsWithTheTimeText` | 15.7 |
| Integration | `StepExecutionLogIntegrationTests.ExecuteStepTimeoutWritesAnExecutionLogEntry` | 10.6 |
| Integration | `DeviceNotLiveAlertTests.ABlockedDeviceGivesOneAlertThenOneLiveAgainMessage` | 3.5 |
| Integration | `TriggerEvaluationTests.DelayTriggerRespectsCooldown` | 3.1 |
| Integration | `DeviceRecoveryStaggerTests.TwoRebootsStartAtLeastOneStaggerTimeApart` | 3.1 |
| Integration | `NotificationParallelSendTests.V12_ThreeStuckTargetsDoNotDelayTheFastTargetOrTheWorker` | 3.0 |
| Unit | `BackgroundScreenCaptureGateTests.ACheckThatSaysRunningOrUnknown_KeepsTheDeviceSuspect` | 2.2 |
| Unit | `SequenceRunnerLoopTests.NestedLoopInnerNoBreakDoesNotFailAnyAncestor` | 2.2 |

In the unit project, no test is longer than 2.2 s. The 30 slowest unit tests total about 33 s. The 30 slowest integration tests total about 70 s. The 30 slowest contract tests total about 45 s, and 15.7 s of that is one test.

Time per integration group (local, seconds, tests): Queues 81 (124), Sequences 59 (140), Commands 37 (44), ExecutionLogs 21 (34), QueueTemplates 20 (42), Installer 14 (79), Notifications 12 (26), StepThrough 10 (18), all other tests about 75.

Time per contract group: Sequences 196 (327), Queues 53 (94), Notifications 29 (31), Images 20 (31), QueueTemplates 15 (25), all other tests about 95.

First shard split (used in `scripts/ci/test-shards.json`):

- `integration-1`: Queues and StepThrough, about 91 s.
- `integration-2`: Sequences, Notifications, and Installer, about 85 s.
- `integration-3` (pinned, the complement): about 90 s. It holds the `EmulatorImageEndpoints` coverage tests and the performance gate class.
- `contract-1`: Sequences, about 196 s. `contract-2` (complement): about 212 s.

The fixed delays (research R8: 112 unit, 66 integration, 8 contract) are mostly short. The two large single delays are the 15.7 s contract test and the 10.6 s integration test. T029 takes these two first.

## Failed runs, last 60 runs (T004)

60 runs: 41 success, 16 failure, 3 cancelled.

| Run | Branch | Class | Detail | Action |
|-----|--------|-------|--------|--------|
| 38146709463 | master | flaky-test | `SelfRescheduleRunIntegrationTests.FourTimerBookingsWithoutKeepLeaveTheLastBookingPending` | See T028 |
| 38122141895 | fix-update-local-guard-and-ci | other | Coverage gate read 0 % for the unit run. Cause: the coverage file path or the collector setup of that branch | Fixed on that branch |
| 37979331077 | master | flaky-test | `DetectionPerformanceTests.DetectionSaveReloadP95Under500ms` (a time limit on a shared runner) | See T028 |
| 37944984005 | master | flaky-test | Web UI: `CommandsPage.spec.tsx` "loads and saves an existing command" | See T028 |
| 37940887956 | 131-update-top-level-tab | infrastructure | coverlet.msbuild: "Unable to read beyond the end of the stream" | Removed by the move to `coverlet.collector` |
| 37939403776 | master | flaky-test | `QueueResumeOnRestartTests.StoppedAndNotOptedInQueuesStayStoppedAfterRestart` (30 s wait) | See T028 |
| 37929297857 | 131-auto-update | flaky-test | `SelfRescheduleRunIntegrationTests.FourTimerBookingsWithoutKeepLeaveTheLastBookingPending` | See T028 |
| 37927627952 | 131-auto-update | build-warning | CA1873 | None (the gate worked) |
| 37592927385 | 127-step-through-sequence-execution | flaky-test | `ResourceLimitsTests.ProcessWorkingSetStaysUnderBudget` | See T028 |
| 36993387019 | master | flaky-test | `ResourceLimitsTests.ProcessWorkingSetStaysUnderBudget` | See T028 |
| 36903615646 | 125-reschedule-self-keep-earliest | build-warning | CA1873 | None (the gate worked) |
| 36774092196 | master | flaky-test | `DeviceRecoveryFlowTests.TwoQueuesOnOneInstanceShareOneRebootAndEachSendsItsOwnMessages` | See T028 |
| 36771580704 | 121-device-not-live-alert-recovery | build-warning | CA1873 | None (the gate worked) |
| 36704362880 | 120-sequence-queue-notifications | real-defect | `QueueNotificationWorkerTests.V08_...` on a feature branch | None |
| 36699571611 | 120-sequence-queue-notifications | real-defect | `NotificationTestSendContractTests.V19_...` on a feature branch | None |
| 36698537260 | 120-sequence-queue-notifications | build-warning | CA2025 | None (the gate worked) |

Totals: build-warning 4, flaky-test 8, infrastructure 1, real-defect 2, other 1.

Tests that failed in more than one unrelated run:

- `SelfRescheduleRunIntegrationTests.FourTimerBookingsWithoutKeepLeaveTheLastBookingPending` (2 runs).
- `ResourceLimitsTests.ProcessWorkingSetStaysUnderBudget` (2 runs).

The 4 build-warning failures are the `analyze` gate working as intended. They stay (FR-001).

## Results of the new layout (T013 to T027)

Runs on the pushed branch. Times are from the first job start to the last job end, plus queue time.

| Run | What | Time |
|-----|------|------|
| 38153029763 | First new layout (3 + 2 shards), one flaky test failed | 326 s |
| 38153929824 | 3 unit, 4 contract, 3 integration shards. All green | 306 s |
| 38154598184 | 3 unit, 4 contract, 5 integration shards (pinned shard apart). All green | 232 s |
| 38155738143 | Web UI plus backend push (test branch) | 230 s |
| 38156025531 | Workflow-file push (test branch) | 222 s |
| 38156283314 | Unknown-path push (test branch) | 228 s |
| 38157082025 | Project file and lock file change (cache key test, T038) | 234 s |

Median of the last five full runs: 230 s. Worst: 234 s. This meets SC-001 (4 min or less) and SC-002 (5 min or less).

SC-003: in run 38154598184, `compile` ended at +113 s and the last test job ended at +215 s. That is 102 s. The old test step had a median of 239 s. The test time fell by 57 %.

Compile split (T013): the build with `-p:RunAnalyzers=false` takes 35 to 37 s. The old build took 142 s. The gain is more than 100 s, so the split stays. The `analyze` job takes 156 to 185 s and runs next to the tests. It is not on the critical path.

Artifact transfer (T014): upload 6 to 7 s for each of 3 artifacts (about 80 MB each). Download 3 to 5 s in the test jobs. This is below 45 s, so the shared artifacts stay. One combined artifact would add about 7 s to each download and would save about 11 s once. Not changed.

Coverage (T015 to T018): coverage now comes from `coverlet.collector` with `scripts/ci/coverage.runsettings`. The collector files travel in the unit and integration artifacts (`coverlet-collector` folder), because `dotnet test <dll>` finds no collector without `--test-adapter-path` and the test jobs do not restore. The thresholds run in the Linux `ci-gate` job (6 s). Local check: the five gates give 100 %, 100 %, 98.97 %, 99.32 %, and 99.36 %. Negative check: a missing coverage folder fails the script. The module path in the collector file is a Windows path, so the script now normalizes the separators.

The gate classes for `EmulatorImageEndpoints` need these integration classes: `EmulatorImageEndpointsCaptureTests`, `EmulatorImageServicesTests`, `EmulatorScreenshotCaptureTests`, and the `Images.` tests. With the capture test class alone the gate reads 37 %. The first design (coverage on the complement shard) took 150 s, so these tests moved to the small `integration-pinned` shard (49 s).

Failure paths (T019), real runs on a test branch:

- Compile error (run 38154922731): `compile` and `analyze` failed, the test jobs were skipped, `ci-gate` failed, the run failed.
- Analysis warning CA2201 (run 38155149512): `analyze` and `ci-gate` failed, the run failed. All test jobs passed.

Unit test in-process parallelism (T020): not applied. The unit jobs are no longer on the critical path (3 shards, 66 to 101 s, shorter than `compile` plus the integration and contract shards). 236 test files read or set `GAMEBOT_*` variables, which are global for the process. A gain on a job that is not on the critical path gives no gain in run time. Left as it was (`parallelizeTestCollections: false`).

Shard balance (T021): final shards and their times in run 38154598184 (job time with about 25 s fixed cost): unit 66 to 101 s, contract 81 to 104 s, integration 49 to 99 s. The slowest test job (104 s) is close to `compile` (101 s).

Test counts (T022), compared with the baseline of SC-004. Baseline from the local runs and the old CI run:

| Project | Before | After (sum of shards) |
|---------|--------|-----------------------|
| Unit | 2936 | 2971 (2936 plus 35 new CI script tests) |
| Integration | 656 passed, 1 skipped (657) | 657 passed, 1 skipped, plus 1 performance test that runs a second time with the `ci` profile, as before |
| Contract | 726 | 726 |
| Web UI | 796 | 796 |

Path rules (T026), real pushes on a test branch:

| Push | Run | Result | Time | Jobs that ran |
|------|-----|--------|------|---------------|
| Docs only | 38155459967 | success | 40 s | changes, ci-gate |
| Spec only | 38155514260 | success | 23 s | changes, ci-gate |
| Web UI only | 38155548728 | success | 153 s | changes, web-ui-tests, ci-gate |
| Backend only | 38155149512 | (failed on purpose, see T019) | | web-ui-tests skipped, all .NET jobs ran |
| Web UI plus backend | 38155738143 | success | 230 s | all jobs |
| Workflow file | 38156025531 | success | 222 s | all jobs |
| Unknown path | 38156283314 | success | 228 s | all jobs |

Two pushes 12 s apart (runs 38156561875 and 38156578932): the first run was cancelled and each push made one run (FR-011).

Speckit pipeline (T027): `gh run list --branch <b> --workflow dotnet.yml` lists a run for the head commit of a docs-only push (runs 38155459967 and 38155514260). A docs-only run ends in `success`, so `gh run watch --exit-status` returns 0. No change to the pipeline.

CodeQL: it runs apart from this workflow on pull requests. Its time is recorded in the pull request, not here.

## Flaky tests (T028)

| Test | Finding | Action |
|------|---------|--------|
| `SelfRescheduleRunIntegrationTests.FourTimerBookingsWithoutKeepLeaveTheLastBookingPending` | The wait ended when the first pending time was above 39 min. The second booking (50 min) also matches, so a slow runner read the state before the third booking. Cause found from the failure text (29.99 min is the third booking) | Fixed: the wait ends only when the pending time is between 39 and 40.2 min |
| `ResourceLimitsTests.ProcessWorkingSetStaysUnderBudget` | The test host ran all integration tests in one process. The working set grew with the test count | Mitigated: each shard is a separate process with about one quarter of the tests. Watch it on `master` |
| `DetectionPerformanceTests.DetectionSaveReloadP95Under500ms` | A time limit on a shared runner. The test already has a warm-up | Recorded. Owner: repository owner. Reason: the limit is the behavior under test |
| `QueueResumeOnRestartTests.StoppedAndNotOptedInQueuesStayStoppedAfterRestart` | A 30 s wait failure, seen once | Recorded. Not reproduced in 5 full runs with the new layout. Owner: repository owner |
| `DeviceRecoveryFlowTests.TwoQueuesOnOneInstanceShareOneRebootAndEachSendsItsOwnMessages` | Seen once | Recorded. Not reproduced in 5 full runs. Owner: repository owner |
| Web UI `CommandsPage.spec.tsx` "loads and saves an existing command" | Seen once on `master` | Recorded. Not reproduced in the web UI job runs of this feature |
| `MaskedTemplateMatchTests`, `QueueTemplateLink` (from the project notes) | Not in the last 60 runs | No action |

## Fixed delays (T029)

The two largest delays are the 15-second timeout test in `NotificationTestSendContractTests` and the 10.6-second timeout test in `StepExecutionLogIntegrationTests`. The delay is the behavior that each test checks (a real timeout), so a fake clock would change what the test proves. They are not changed. Sharding removes their effect on the run time: `contract-1` and `contract-2` end in 81 to 104 s and the integration shards in 49 to 99 s. All other delays in the ranking are 3.5 s or less.

## Full-suite proof (T030)

Deviation: the three local full runs were not done. The local PC needs about 15 minutes for one run, and the local unit test DoesNotCallAdbDirectly fails there for a reason outside this feature. The CI runs are the proof: five full runs of the final layout passed all tests, with the counts in the table of T022.

## Fixed parts on the critical path (T036 to T038)

From run 38154598184, in seconds: `changes` 10, `compile` start-up (checkout 7, setup 3, NuGet cache 13 to 34, restore 12) about 40, uploads 19, test job start-up (checkout 5 to 14, setup 3 to 4, download 3 to 5) about 15, `ci-gate` 9. No part has a safe gain of 5 s or more:

- The NuGet cache varies from 13 to 34 s. Without it the restore is longer.
- Three artifact uploads in series take 19 s. One combined artifact would add download time to every test job.
- `npm ci` takes 25 to 57 s in the web UI job. The job is not on the critical path of a code push (131 s against 232 s). A web UI only push takes 153 s, below the 180 s limit. No `node_modules` cache.

T038: run 38157082025 on a test branch changed a project file (new NuGet cache key) and package-lock.json. The run passed in 234 s. The NuGet cache step took 13 s (restore-key fallback) and the restore 11 s. 
pm ci took 63 s.

## Linux probe (T039)

The unit tests ran on `ubuntu-latest` on a test branch (run 38156676346). 290 of 2971 tests failed (for example `TypeInitializationException` for `PInvokeGdiPlus`). The failures come from Windows-only parts, so a move needs product changes. The idea is dropped. The test branch is deleted.

## Before and after: checks, gates, and scans (T040, SC-008)

| Check | Before | After |
|-------|--------|-------|
| Secret scan | `build` job | `changes` job (Linux), on every push |
| Warnings-as-errors build with all analyzers | `build` job | `analyze` job |
| Unit tests | `build` job | `test-unit` (3 shards) |
| Integration tests | `build` job | `test-integration` (4 shards) |
| Contract tests | `build` job | `test-contract` (4 shards) |
| Performance gate (`ci` profile) | `build` job | `integration-pinned` shard |
| Five coverage gates | `build` job | `ci-gate` job (Linux) |
| Web UI tests with coverage | `web-ui-tests` job | `web-ui-tests` job (runs when the web UI or a shared file changes) |

No check is removed.

## Architecture document (T041)

`docs/architecture.md` describes the domain, the API, and the persistence. None of them changed. No change to the document.
## Final proof (T043 to T046)

T043: the five full runs above are the final layout. Median 230 s, worst 234 s. SC-001 and SC-002 are met. SC-003 is met (see the result section).

T044 (SC-007): a maintainer who did not write the guide has not repeated a measurement yet. This check is open and needs a person. Owner: the repository owner.

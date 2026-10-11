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

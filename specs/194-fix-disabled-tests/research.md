# Research: Turn On and Repair the Disabled Tests

All runs were on a Windows 11 developer PC on 2026-11-19, Debug build of commit `a33eeaf` (master). For the D-1, D-2, and D-3 runs, the skip markers were removed and the comment was removed for a short time. After the runs, the files were restored.

## R-1: D-1 `WaitForImageCaptureLoopTests.StepTimesOutWhenImageNotOnScreen`

- **Result**: Pass. The class ran 3 times alone (21 of 21 each time). The full integration project ran once with the test on: the 3 cases passed.
- **Decision**: Remove the `Skip` argument. Make no product change.
- **Rationale**: The skip reason says "Fails on the baseline of feature 168". Later features changed the wait path, and the failure does not occur now.
- **Alternatives considered**: Keep the skip until a CI run shows that the test passes. Rejected: FR-001 forbids the skip, and the CI runs of this branch give that check (FR-003).

## R-2: D-2 `ConcurrentQueueRunTests.ARunBlockedByTheSessionLimitRecordsAnActionableReason`

- **Result**: Pass. The log shows `Capacity exceeded. Active=1 Max=1`, so the test host now applies `Service:Sessions:MaxConcurrentSessions`. The class ran 3 times alone, and the full integration project ran once: all pass.
- **Decision**: Remove the `Skip` argument. Delete the NOTE comment above the test, because it says that the setting "did not take effect".
- **Rationale**: `GameBotServiceSetup.RegisterSessionServices` binds `SessionOptions` from `builder.Configuration`, and the factory configuration now reaches it.
- **Alternatives considered**: Delete the test and keep only the unit tests. Rejected: the integration test checks the full path from the setting to the run summary.

## R-3: D-3 `TextMatchEvaluatorImagePipelineTests.CropsRegionToExpectedPixelSize`

- **Result**: Fail. The size assertions pass. The first log assertion fails: `Expected provider.Entries {empty} to have an item matching ... "Cropped image size"`.
- **Cause**: `TextMatchEvaluator` has a logger but writes no log entry. The test checks a log entry that the product no longer writes.
- **Decision**: Turn the test on. Delete the 2 log assertions. Add `ocr.LastWidth.Should().BeGreaterOrEqualTo(100)`, because the crop is 100 x 50 before the upscale. This keeps the purpose of the "not 1x1" assertion. Keep the aspect-ratio assertion.
- **Alternatives considered**: Add a debug log entry to the evaluator. Rejected: feature 190 removed trace noise, and a log entry only for a test is noise.

## R-4: D-4 browser tests (Playwright)

- **Result**: `npm run e2e` ran 69 tests in the `chromium` and `mobile` projects. 69 passed in 61 s (60 s of test time). No test was skipped.
- **Decision**: No test repair. Add the CI job `web-ui-e2e` (see plan). Add `src/web-ui/test-results/` and `src/web-ui/playwright-report/` to `.gitignore`, because a local run leaves `test-results/.last-run.json` as an untracked file.
- **Rationale**: The tests replace all service answers with `page.route`, so the job needs no back end.

## R-5: CI time of the new job

- **Estimate**: `npm ci` (about 30 s with cache), browser install (about 20 s with cache, about 60 s without), `vite build` for the preview server (about 20 s), tests (about 60 s with 2 workers). Total about 2 min 30 s with the cache, plus about 40 s of Windows start time.
- **Decision**: Cache the Playwright browser folder. Measure the job on the first push. If a web-UI-only push takes more than 3 min, use `workers: 4` in CI first. If that is not sufficient, split the job into one job for each Playwright project.
- **Alternatives considered**: Linux runner. Rejected: the pixel tests (`editor-row-height`, `queues-actions-width`) need the Windows fonts (clarification 3).

## R-6: Full baseline of all suites (new items from the full search)

The spec says that the plan must do a full search and add each new item. The full runs gave these results:

| Suite | Result |
|-------|--------|
| Unit (5,159 tests) | 1 failed: D-3 (expected). 0 skipped. |
| Integration (1,162 tests) | 1 failed: `DeviceRecoveryFlowTests.ABlockedDeviceIsRebootedOnceTheHeldFiringRunsAndOneLiveAgainMessageArrives`. 0 skipped. |
| Contract (1,312 tests) | 0 failed. 0 skipped. |
| Jest (1,658 tests) | 0 failed. 0 skipped. |
| Playwright (69 tests) | 0 failed. 0 skipped. |

A search of the last 15 failed `.NET CI` runs found these failed tests:

| Test | Runs | Last seen |
|------|------|-----------|
| `GamepackBadTypeContractTests.ABadTypeGives400WithThePath` (number type, no dry run) | 2, both on `master` | 2026-11-19 (the newest `master` run) |
| `GamepackBadTypeIntegrationTests.AnUnknownTypeAnswersInvalidJson` | 1 | 2026-11-13 |
| `StepThroughRunContractTests.ThreePushesRunThreeStepsInOrderAndTheStateEndsComplete` | 3 | 2026-11-10 |
| `AbandonedSequenceLogIntegrationTests.CancelledRunClosesItsEntryInsteadOfLeavingItRunning` | 1 | 2026-11-02 |

### D-5: gamepack type check depends on the test order

- **Result**: `GamepackBadTypeContractTests` alone fails 3 of 3 times: the case "number type, no dry run" gets 422, not 400. The same case passes in the full contract run. `GamepackFileSerializerInvalidTypeTests` alone fails the case "unknown" (`InvalidType` is false). The same case passes in the full unit run. The newest `master` CI run fails on the contract case in shard `contract-6`.
- **Possible cause**: The result changes with the tests that ran before in the same process. `GamepackTypeDiscriminatorCheck` reads `JsonSerializerOptions.GetTypeInfo(...).PolymorphismOptions`. Possibly, an earlier read changes a shared options object or a type-info cache. The implementation must find the exact cause.
- **Decision**: Product repair. First, write a test that fails in a fixed order (the class filter above gives one). Then make the check give the same result in each order. The 422 is an incorrect answer: an invalid type member must give 400 with the JSON path (feature 154).
- **Reproduction (T007, implementation)**: the contract filter failed 1 of 10 cases ("number type, dryRun False", 422). The unit filter failed 1 of 6 cases ("unknown", `InvalidType` is false). In each class, only the first case that ran in the process failed.
- **Cause (T009)**: `GamepackFileSerializer` keeps static `JsonSerializerOptions` objects with no `TypeInfoResolver`. `GamepackTypeDiscriminatorCheck.Find` calls `options.GetTypeInfo(type)`. This call locks the options, and it throws `InvalidOperationException` when the options have no resolver. The check caught this exception and returned "no problem". Then the typed read (`Deserialize`) added the default resolver, and it threw a `JsonException`, which gave a "wrong shape" problem (422) and `InvalidType` false. After the first typed read in the process, the options had a resolver, and the check worked. Thus the result changed with the tests that ran before.
- **Repair (T010)**: `Find` now calls `options.MakeReadOnly(populateMissingResolver: true)` when the options are not read-only. This is the same step that the typed read does. The new test `GamepackTypeCheckOrderTests.TheCheckFindsEachBadTypeWithOptionsThatWereNotUsedBefore` gives new options to the check, so it fails before the repair in each order. `ParseGivesTheSameResultInBothOrders` parses the 4 bad-type packs in 2 orders.
- **Result (T011)**: after the repair, the unit filter (with the new tests, 8 cases) and the contract filter (10 cases) passed 3 of 3 times.

### D-6: `DeviceRecoveryFlowTests` fails under load

- **Result**: Failed 1 time in the full integration run (`Expected dispatcher.Count(QueueAlertKind.LiveAgain) to be 1, but found 0`, after a 15 s wait). Alone, the class passes 3 of 3 times in 7 s.
- **Possible cause**: `DeviceRecoveryHost.WaitForAsync` ends after 15 s with no error. The assertions then run on an incomplete state. Under load, the reboot and the "live again" message take more than 15 s, or a message is lost.
- **Decision**: Find the cause. The wait must end only on the checked state (rule of `docs/ci-time.md`), and a time-out must fail with a clear message. If the product loses the message under load, first write a test that reproduces the loss and fails, and then repair the product.
- **Result (T012, T013, implementation)**: `DeviceRecoveryHost.WaitForAsync` now throws `TimeoutException` with the name of the condition (for example "the 'live again' message") and the time-out. Each call gives a name. The full integration project ran 3 times after this change (1162 of 1162 each time, 8.6 to 9.5 min). Run 2 had the Playwright suite at the same time as extra load. `DeviceRecoveryFlowTests` passed in all 3 runs. The time from the queue start to the "live again" message was about 1 s (the test time) in each run, much less than the 15 s wait. The failure did not occur again, so a failed run to measure is not available.
- **Conclusion**: no evidence of a lost message in the product. The time-out stays 15 s, because the measured time is about 1 s. If the failure occurs again, the new message names the condition that did not become true, and the log shows the state.

### D-7: older CI failures

- **Decision**: Run `StepThroughRunContractTests` and `AbandonedSequenceLogIntegrationTests` 10 times each, alone and with their CI shard filter. If a run fails, find and repair the cause. If all runs pass, record the result in the PR and make no change.
- **Rationale**: These tests failed in CI 9 or more days ago, and later runs passed. They are possible flaky tests, and FR-003 needs stable tests.
- **Result (T014, implementation)**: `StepThroughRunContractTests` passed 10 of 10 times (14 tests, about 0.9 s each run). `AbandonedSequenceLogIntegrationTests` passed 10 of 10 times (2 tests). With the CI shard filters, `contract-6` passed (322 of 322) and `integration-2` passed (247 of 247). No change.

## R-7: Guard design

- **Decision**: A PowerShell script, `scripts/ci/find-disabled-tests.ps1`, with regular expressions for each line. It runs in the Linux `changes` job. Unit tests in `tests/unit/Ci/DisabledTestGuardTests.cs` use `CiScriptRunner` and a temporary folder.
- **Rationale**: The other CI scripts use the same pattern (`get-changed-groups.ps1`, `evaluate-gate.ps1`). The `changes` job runs first and on each push. A line scan needs no build. The current code has 2 skip markers and 1 test in comments. After this feature, the guard finds 0 items.
- **Alternatives considered**: An ESLint rule (`no-disabled-tests`, `no-focused-tests`) for the TS files and a Roslyn analyzer for C#. Rejected: two tools with two configurations, and no check for tests in comments.
- **Conditional skip**: `mobile.spec.ts:288` has `test.skip(browserName !== 'chromium', '...')`. The first argument is not a string literal, so the guard permits it.

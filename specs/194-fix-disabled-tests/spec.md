# Feature Specification: Turn On and Repair the Disabled Tests

**Feature Branch**: `194-fix-disabled-tests`  
**Created**: 2026-11-19  
**Status**: Draft  
**Input**: User description: "Let's clean up all the failing tests. There are a number of tests that are failing on the back-end side and on the front-end side that were disabled. Please enable all of the tests, validate which one of them are failing, and fix all the errors."

## Current State *(input for planning)*

A search of the repository on 2026-11-19 found these disabled tests. The plan must do a full search again and add each new item that it finds.

| ID | Side | Location | How it is disabled | Recorded reason |
|----|------|----------|--------------------|-----------------|
| D-1 | Back end | `tests/integration/WaitForImageCaptureLoopTests.cs`, `StepTimesOutWhenImageNotOnScreen` (3 cases: step, force, sequence) | Skip marker | "Fails on the baseline of feature 168. Turned off by request. Find the cause and turn it on again." |
| D-2 | Back end | `tests/integration/Queues/ConcurrentQueueRunTests.cs`, `ARunBlockedByTheSessionLimitRecordsAnActionableReason` | Skip marker | "Session limit cannot be overridden in the test host; covered by unit tests instead." |
| D-3 | Back end | `tests/unit/TextMatchEvaluatorTests.cs`, `CropsRegionToExpectedPixelSize` | Test is in comments | None |
| D-4 | Front end | All browser end-to-end tests in `src/web-ui/tests/e2e` (desktop and phone projects) | The automated build does not run them | None |
| D-5 | Back end | `GamepackBadTypeContractTests.ABadTypeGives400WithThePath` and `GamepackFileSerializerInvalidTypeTests` | Not disabled. The result changes with the test order. The newest `master` build fails on it. | Found by the plan research (R-6) |
| D-6 | Back end | `DeviceRecoveryFlowTests.ABlockedDeviceIsRebootedOnceTheHeldFiringRunsAndOneLiveAgainMessageArrives` | Not disabled. Fails under load. | Found by the plan research (R-6) |
| D-7 | Back end | `StepThroughRunContractTests.ThreePushesRunThreeStepsInOrderAndTheStateEndsComplete` and `AbandonedSequenceLogIntegrationTests.CancelledRunClosesItsEntryInsteadOfLeavingItRunning` | Not disabled. Failed in older builds. | Found by the plan research (R-6) |

The user asked to repair all errors. Thus the failed tests D-5 to D-7 are in scope, also if no skip marker disables them.

The phone end-to-end test file has one conditional skip for browsers other than Chromium. That skip is correct, because the test needs a Chromium-only function. It is not in scope.

## Clarifications

### Session 2026-11-19

The pipeline ran in autonomous mode. The pipeline chose each answer. The rationale follows each answer.

- Q: Where does the automated build run the browser tests? → A: In a new, separate job. The job uses the same web UI path rule as the current front-end unit-test job, and the pass gate needs it. Rationale: a separate job runs in parallel, so the build does not become much slower.
- Q: Do the browser tests in the automated build need the back-end service? → A: No. All browser tests replace the service answers with test data. The build starts only the web UI servers. Rationale: a search found that all 12 browser test files replace the service answers.
- Q: On which operating system does the browser-test job run? → A: Windows, the same as the current front-end job. Rationale: some browser tests measure pixel sizes. Font sizes are different on Linux, and developers run the tests on Windows.
- Q: Does the guard fail the build, or does it only show a warning? → A: It fails the build. It fails for an unconditional skip marker, for a focus marker that runs only some tests, and for a test that is in comments. A conditional skip that has a reason is permitted. Rationale: a warning gets no attention, and the problem then occurs again.
- Q: Can the automated build retry a failed browser test? → A: No. The retry count is 0. The build keeps the trace and screenshot of each failed test. Rationale: a retry hides an unstable test, and FR-003 needs stable tests.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - All back-end tests run and pass (Priority: P1)

A developer runs the full back-end test suite. Each test runs. No test has a skip marker, and no test is in comments. All tests pass.

**Why this priority**: A disabled test hides a defect or gives false confidence. The back-end disabled tests (D-1, D-2, D-3) are a known gap in the release gate. The newest `master` build fails because of D-5, and D-6 and D-7 make builds fail at random.

**Independent Test**: Run the unit, integration, and contract test suites. Make sure that the skip count is zero and that the fail count is zero.

**Acceptance Scenarios**:

1. **Given** the test D-1 is turned on, **When** the integration suite runs, **Then** all 3 cases of the test pass. A step that waits for an image that is not on the screen stops at its time limit.
2. **Given** the test D-2 is turned on, **When** the integration suite runs, **Then** the test sets a session limit in the test host. A run that the limit blocks records a reason that the operator can act on.
3. **Given** the test D-3 is no longer in comments, **When** the unit suite runs, **Then** the test passes. The text-match evaluator crops the region to the expected pixel size.
4. **Given** the gamepack bad-type tests (D-5), **When** they run alone, in their CI shard, or in the full suite, **Then** each bad type member gives 400 with the JSON path in each case.
5. **Given** the device recovery test (D-6), **When** the full integration suite runs 3 times, **Then** the test passes each time. A wait that ends with no result fails with a message that names the condition.
6. **Given** a turned-on test fails because of a product defect, **When** the developer finds the cause, **Then** the developer repairs the product code, not the test assertion.

---

### User Story 2 - The front-end browser tests run and pass (Priority: P1)

A developer runs the front-end browser end-to-end tests on desktop and on the phone screen size. All tests pass. The automated build runs them on each push that changes the web UI, so they cannot fail again without a signal.

**Why this priority**: The browser tests are the only check of complete user flows in the web UI. Because no automated step runs them, they can be broken now and nobody knows.

**Independent Test**: Run the browser test command for the desktop and the phone projects. Make sure that the fail count is zero. Push the branch and make sure that the automated build shows a browser-test result.

**Acceptance Scenarios**:

1. **Given** the current browser tests, **When** a developer runs them on a clean checkout, **Then** all tests pass.
2. **Given** a push that changes the web UI, **When** the automated build runs, **Then** it runs the browser tests and shows the result on the pull request.
3. **Given** a browser test fails in the automated build, **When** the build completes, **Then** the build is marked as failed and the failure report is available for download.

---

### User Story 3 - Disabled tests cannot come back without a signal (Priority: P2)

A developer adds an unconditional skip marker, or puts a test in comments. The build fails, so the developer sees this before the merge.

**Why this priority**: Without a guard, the same problem can occur again. This story is less important than the repair itself.

**Independent Test**: Add an unconditional skip marker to a test. Run the guard. Make sure that the guard reports the test and fails.

**Acceptance Scenarios**:

1. **Given** a test has an unconditional skip marker, **When** the guard runs, **Then** the guard lists the test with its file and line, and the build fails.
2. **Given** a conditional skip that the platform needs (for example, Chromium only) and that has a reason, **When** the guard runs, **Then** the guard does not report it as a failure.

---

### Edge Cases

- A turned-on or repaired test fails only some of the time. The developer must find and remove the cause of the timing problem. A retry is not a sufficient repair. A longer wait is a correct repair only when a measured time shows that the behavior is correct but slow under load. Then the new limit must be more than the measured time, and a code comment must record the measured time.
- A test fails because the product loses a result. The developer first writes a test that reproduces the loss every time and fails. Then the developer repairs the product.
- A turned-on test tests behavior that the product no longer has. The developer deletes the test and records the reason in the pull request. The developer does not keep a skip marker.
- A test setting cannot be changed in the test host (D-2). The developer repairs the test host, so that the setting has an effect. If this is not possible, the developer deletes the test and records the unit tests that cover the behavior.
- The browser tests need the web UI server. In the automated build, the build starts the web UI servers before the tests and stops them after the tests. The back-end service does not start.
- A browser test that measures pixel sizes gives a different result on a different operating system. The browser-test job runs on Windows to prevent this.
- The browser tests make the automated build slower. The increase must stay in the limits of `docs/ci-time.md`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST have no test with an unconditional skip marker, in the back end or in the front end.
- **FR-002**: The repository MUST have no test that is in comments. Each such test is turned on again, or it is deleted with a recorded reason.
- **FR-003**: Each test that this feature turns on or repairs (D-1 to D-7) MUST pass in 3 consecutive runs locally. The automated build of the branch MUST pass 3 consecutive times.
- **FR-004**: When a turned-on test shows a product defect, the developer MUST repair the product code. The developer MUST NOT make the assertion weaker to get a pass.
- **FR-005**: The test D-1 MUST show that a wait-for-image step stops at its time limit when the image is not on the screen. This applies to the step, force, and sequence paths.
- **FR-006**: The test host MUST let a test set the session limit, so that the test D-2 can prove the blocked-run reason.
- **FR-007**: The automated build MUST run the front-end browser tests, desktop and phone, on each push that changes the web UI. They run in a separate job on Windows, and the pass gate needs this job. A failed browser test MUST fail the build. The job MUST NOT start the back-end service, because the tests replace the service answers.
- **FR-008**: The automated build MUST keep the browser-test failure report (traces, screenshots) as a downloadable result when a browser test fails. The build MUST NOT retry a failed browser test.
- **FR-009**: A guard MUST fail the build for each unconditional skip marker, each focus marker that runs only some tests, and each test that is in comments. The guard MUST show the file and line of each item. A conditional skip that has a reason is permitted. The guard runs in the automated build on each push.
- **FR-010**: The pull request MUST list each disabled test, its root cause, and the repair.
- **FR-011**: This feature does not change the web UI behavior. Phone support level: not applicable. The phone browser tests stay in scope as tests.

### Key Entities

- **Disabled test**: A test that the normal test run does not run. Attributes: location, side (back end or front end), method of disablement, recorded reason, root cause, repair.
- **Guard finding**: One skip marker, focus marker, or test in comments that the guard finds. Attributes: file, line, kind (skip, focus, commented), source line.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The number of open items in the "Current State" table goes from 7 to 0.
- **SC-002**: The back-end suites report 0 skipped tests and 0 failed tests in the automated build.
- **SC-003**: The front-end unit and browser tests report 0 failed tests in the automated build.
- **SC-004**: Each turned-on or repaired test passes in 3 of 3 consecutive runs.
- **SC-005**: The automated build time of a push stays in the limits of `docs/ci-time.md`.
- **SC-006**: A new unconditional skip marker, focus marker, or test in comments fails the build in 100% of the trial cases.

## Assumptions

- "Enable all of the tests" includes the browser end-to-end tests that the automated build does not run. They are disabled in effect.
- The conditional skip in the phone browser tests is correct and stays. It has a reason and the platform needs it.
- The tests that the shard filter of the automated build divides between shards are not disabled. The last shard runs all tests that the named filters do not include.
- The performance test that the build runs in a separate step is not disabled.
- The automated build can install a browser and start the web UI server on its Windows runner.

# Feature Specification: Reduce CI Run Duration

**Feature Branch**: `134-ci-duration-reduction`
**Created**: 2026-10-11
**Status**: Implemented
**Input**: User description: "I want to bring down the CI run duration drastically. Analyze past runs (no need to start new ones unless you need some special statistics), suggest the steps that bring most and ask me if you have any questions. The goal is to reduce the time for a PR checks run to the minimum. Also if you find any patterns, what increases the time, document the findings so they can be avoided in the future."

## Findings From Past Runs *(analysis input)*

The analysis used the last 60 runs of the `.NET CI` workflow. It also used the step times of 6 runs and the log of one run (2026-10-11). No new run was started.

**Where the time goes (typical successful run, 6.8 to 9.3 minutes)**

| Part | Typical time | Share of the critical path |
|------|--------------|----------------------------|
| Runner start, checkout, SDK setup, package cache | 35 to 50 s | about 8 % |
| Package restore | 11 to 19 s | about 3 % |
| Build with all analysis rules (Release, warnings are errors) | 113 to 158 s | about 25 % |
| Test run with coverage collection | 250 to 362 s | about 55 % |
| Coverage gates and performance gate | 10 s | about 2 % |
| Web UI tests (separate job, runs in parallel) | 66 to 130 s | not on the critical path |
| CodeQL C# analysis (separate check, runs in parallel) | about 170 s | not on the critical path |

The build job is the critical path. The web UI job and the other checks end first.

**Test run detail (run 38134635184)**

| Test project | Tests | Time |
|--------------|-------|------|
| Unit tests | 2936 | 1 min 51 s |
| Integration tests | 656 | 4 min 1 s |
| Contract tests | 726 | 4 min 2 s |

The integration tests and the contract tests run at the same time on one runner. The unit tests run first. Their time is added before the other two projects finish.

All three test projects are set to run fully serial. They have no parallel test collections and one worker thread. About 2 300 tests run one after the other.

The unit tests contain 112 fixed delays. The integration tests contain 66. 81 integration test classes are in named collections.

**Other patterns that raise the time**

- 16 of the last 60 runs failed, and 3 were cancelled. Each failure forces a new full run of 7 to 9 minutes.
- Four failures stopped after about 3 minutes in the build step, because of warnings that are errors. The other failures stopped in the test step, after the full build.
- Project notes record known unstable tests.
- Some runs took 22, 29, 51, or 78 minutes. Two dependency-bot runs took 5 hours and 43 hours. The step times of these runs add up to about 9 minutes. The rest is waiting for a runner or for a manual action.
- Every push to every branch starts a full run. This includes a push that changes only specs, docs, or notes.
- The coverage tool instruments the code in the same run that executes all tests. Only five gated areas need the data.
- The build time rose from 115 s to 158 s as the code base grew. Feature 133 handled the local build time and added the guide `docs/build-time.md`.
- Earlier CI work removed three things (PR #124, #152, #153, #283). These are a second build for pull requests, an installer build for pull requests, and a second test pass. Do not undo these changes.

## Clarifications

### Session 2026-10-11

- Q: What is the meaning of "PR checks run time"? → A: The time from the push to the end of the last job of the `.NET CI` workflow, on the hosted runner. Queue time is counted apart. The CodeQL jobs run apart from this workflow and cannot use path filters. Their time is recorded apart and is not part of SC-001 to SC-005. Rationale: the repository controls only the workflow.
- Q: May analysis rules be relaxed to save build time? → A: No. Feature 133 set this rule, and it stays. Rationale: policy treats warnings as errors.
- Q: Which pushes skip which work? → A: Path-based jobs. FR-008 lists the rules. Rationale: a web-UI-only push saves about 7 minutes. Shared files always run all jobs.
- Q: How is the test time of SC-003 measured when the old test step becomes several parallel jobs? → A: Measure the wall-clock time from the end of the compile job to the end of the last test job. This includes artifact download and job start. Rationale: it counts the real cost of the split. It compares fairly with the old single step.
- Q: May the coverage gates move to the default branch only? → A: No. The gates stay on every PR run. Only the projects that a gate needs collect coverage.
- Q: May the team test a Linux runner? → A: Yes, as a time-boxed probe. The team keeps it only if all tests give the same results. The measured gain must be larger than the noise.
- Q: What happens to a docs-only push? → A: A short check of about 1 minute runs and reports a pass. The pipeline wait keeps working.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fast feedback on a normal code push (Priority: P1)

A developer pushes a code change. The developer wants all checks to finish fast, with the same protection as today.

**Why this priority**: Code pushes are the most common case. The test step is more than half of the run time. It is the part with the largest gain.

**Independent Test**: Push a branch with a small code change. Measure the time from the push to the end of the last job. Compare it to the baseline of 6.8 to 9.3 minutes.

**Acceptance Scenarios**:

1. **Given** a push with a code change, **When** all jobs run, **Then** the last job ends in 4 minutes or less (median of 5 runs). Every test that ran before still runs.
2. **Given** the same push, **When** the run ends, **Then** all coverage gates and the performance gate still apply. They give the same pass or fail result.
3. **Given** a test that fails, **When** the run ends, **Then** the failure shows as it does today. The failing job does not hide the result of the other jobs.

---

### User Story 2 - No wasted run for a push that cannot change a result (Priority: P2)

A developer pushes a change that only touches specs, docs, or notes. A full build and test run gives no new information.

**Why this priority**: Many pushes in this project are spec and doc commits from the speckit pipeline. Each of them costs one full run.

**Independent Test**: Push a branch that changes only files under `specs/` and `docs/`. Check what the CI does. Then push a branch that changes one source file. Check that the .NET jobs start.

**Acceptance Scenarios**:

1. **Given** a push that cannot affect the build or the tests, **When** it reaches the CI, **Then** the build and test jobs do not run. A short check of about 1 minute reports a pass, so the pipeline wait works.
2. **Given** a push that changes a shared file, **When** it reaches the CI, **Then** all jobs run.
3. **Given** a push that changes only web UI files, **When** it reaches the CI, **Then** the web UI tests run. The .NET jobs do not run. The branch still shows a pass signal.
4. **Given** a push that changes only backend files, **When** it reaches the CI, **Then** the .NET jobs run. The web UI tests do not run.

---

### User Story 3 - Fewer repeated runs from unstable tests and late failures (Priority: P2)

A developer wants a run to fail early and for a real reason. A flaky test or a late failure doubles the wait.

**Why this priority**: 27 % of the last runs failed. A failure found in the build step after 3 minutes is cheaper than one found after 7 minutes. A flaky failure costs a full extra run.

**Independent Test**: Read the failure list of the last 60 runs. Each failure has a recorded class: real defect, flaky test, build warning, or infrastructure. Check the failure rate on the first 20 runs after the merge (SC-006).

**Acceptance Scenarios**:

1. **Given** the failed runs of the last 60, **When** a maintainer reads the finding report, **Then** each failure has a class. The tests that fail often are listed by name.
2. **Given** a flaky test from that list, **When** the feature is done, **Then** the team has fixed the test or isolated it. Or the team has recorded a reason.
3. **Given** a change that breaks the compile step, **When** the CI runs, **Then** the compile failure shows before any test work starts.
4. **Given** a change that only causes an analysis warning, **When** the CI runs, **Then** the analysis job fails. The final gate job fails, and so the run fails.

---

### User Story 4 - A written record of what makes CI slow (Priority: P1)

A maintainer wants to know which patterns raise the CI time, so that the same time does not come back. The record lists the patterns, the measured effect, and the rules to avoid them.

**Why this priority**: The build time was reduced before, and then it rose again. The record keeps the gain.

**Independent Test**: Read the guide. Each pattern has a measured time effect and a rule. Follow the measure steps and get comparable numbers.

**Acceptance Scenarios**:

1. **Given** the finished feature, **When** a maintainer reads the CI time guide, **Then** the guide lists the time budget per part of the run. It lists the patterns that raise the time, with a rule for each.
2. **Given** a proposed change to the workflow, **When** a maintainer follows the guide, **Then** the maintainer can measure the run time before and after. The maintainer can compare it to the stated limit.
3. **Given** the guide, **When** a new maintainer reads it, **Then** the guide shows how to read the step times of past runs. It shows how to tell queue time from step time.

---

### User Story 5 - Small remaining overhead is removed (Priority: P3)

The fixed parts of a run are small, but they add up on every run. The fixed parts are runner start, package cache, restore, and web UI install.

**Why this priority**: The gain is 30 to 60 seconds per run. The change is simple and has low risk.

**Independent Test**: Compare the step times of the fixed parts before and after.

**Acceptance Scenarios**:

1. **Given** a run with warm caches, **When** it ends, **Then** the fixed parts use 60 seconds or less on the critical path.
2. **Given** a change in a project file or a lock file, **When** the CI runs, **Then** the caches are rebuilt. The run still passes.

---

### Edge Cases

- A test relies on shared state, such as a data folder, a port, or a static value. If tests run in parallel, they can fail by chance. The team must find these tests before it turns on parallel runs. The team must fix or isolate them.
- A split into several jobs makes each job start a new runner and share the build output. The extra start time must not remove the gain. If a split gives no gain in a measurement, the team does not keep it.
- The pipeline waits for the CI run before it opens a pull request. A skipped run or a renamed check must not leave the pipeline waiting for ever.
- A push changes both docs and one source file. The jobs for that source file must run.
- A push changes both web UI files and backend files. All jobs must run.
- A path rule misses a file group, for example a new folder. The default must be to run all jobs.
- A new push to the same branch must still cancel the older run.
- A coverage gate or the performance gate must not pass by accident. This applies when a job is skipped or when no coverage data exists.
- Hosted runner speed changes from day to day. A comparison must use several runs.
- Waiting time for a runner is outside the control of the repository. The guide must separate it from step time.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The CI MUST keep all checks. These are the tests, the coverage gates, the performance gate, the secret and security scans, and the warnings-as-errors build. No check is removed or weakened to save time.
- **FR-002**: The team MUST record a baseline before any change. The baseline has the step times of at least 10 successful runs on the default branch. It has the median and the worst value per part, and the queue time per run.
- **FR-003**: The team MUST shorten the test step by the largest safe amount. The team MUST measure each step below. The team keeps a step only if its gain is larger than the noise.
  - (a) Run the three test projects in parallel, in separate jobs or processes.
  - (b) Turn on parallel test execution inside a test project where the tests are free of shared state.
  - (c) Split the longest test project into shards.
  - (d) Collect coverage only where a gate needs it. The coverage gates stay on every PR run.
- **FR-004**: Before the team turns on parallel test execution, it MUST find the tests that share state. Shared state means data folders, ports, static values, and environment variables. The team MUST fix or isolate each such test. Or the team keeps it in a serial group and records a reason.
- **FR-005**: The team MUST record the slowest tests of each test project. The data comes from a measurement of per-test times. The team MUST remove or shorten avoidable fixed delays in those tests. Or the team records why a delay stays.
- **FR-006**: The build output for the tests MUST come from one build per run. All test jobs MUST get it from that build. They MUST NOT build the code again. A separate full-analysis build MAY run in parallel, outside the critical path. It MUST keep the warnings-as-errors rules at full strictness. A failure of that build MUST fail the run. The team MUST record the measured time gain before it keeps this split.
- **FR-007**: A failure in the compile step or the secret scan MUST stop the test work early. The failure MUST show first. A failure of the parallel full-analysis build MUST fail the run through the final gate job. It does not stop test jobs that already run. If the team drops the split, an analysis warning fails the single build job, and the test jobs do not start.
- **FR-008**: The CI MUST choose the jobs from the changed paths.
  - A push that cannot affect the build or the tests MUST NOT start the build and test jobs. Docs, specs, and notes are in this group.
  - A push that changes only web UI files MUST skip the .NET jobs.
  - A push that changes only backend files MUST skip the web UI tests.
  - A push that changes a shared file MUST run all jobs. Shared files are the workflow, scripts, root build settings, API contract files, and dependency files.
  - A push that changes files of both areas MUST run all jobs.
  - A path in no known group MUST run all jobs.
  - The team MUST list the path groups in the guide. The team MUST test each rule with a real push.
- **FR-009**: The team MUST measure the fixed overhead on the critical path. The parts are runner start, SDK setup, package cache, restore, and web UI install. The team MUST reduce a part where the measured gain is 5 seconds or more.
- **FR-010**: The team MUST classify the failed runs of the last 60 runs. The classes are real defect, flaky test, build warning, infrastructure, and other. The team MUST list tests that failed in more than one unrelated run. The team MUST fix each listed test, isolate it, or record a reason.
- **FR-011**: The CI MUST keep the cancel-older-run behavior for pushes to the same branch. Two quick pushes to one branch prove it. The CI MUST keep one run per push.
- **FR-012**: A skipped or merged job MUST NOT let a coverage gate or the performance gate pass without data. A missing result MUST fail the gate.
- **FR-013**: The team MUST write a CI time guide in the `docs` folder. The README MUST link to it. The guide MUST contain these items:
  - the time budget per part of the run;
  - the measured baseline and the result after the change;
  - every pattern that raised the time, with the measured effect;
  - a rule to avoid each pattern;
  - the steps to measure a run;
  - how to tell queue time from step time.
- **FR-014**: The guide MUST include a check list for a change to the workflow or the test setup. One question is: does this add a second build, a serial test group, a fixed delay, or a new full-run trigger?
- **FR-015**: The team MUST measure the final state of the branch with at least 5 runs on the hosted runners. The team MUST record the median and the worst value in the guide. Any later change to the workflow needs a new measurement.
- **FR-016**: The spec `Status` line and the spec status index MUST be current at the end of the feature.

### Key Entities

- **CI run**: One execution of all checks for one push. It has a start time, an end time, a result, a queue time, and a time per step.
- **Critical path**: The chain of steps that sets the time from the push to the end of the last job.
- **Time budget**: The target time for each part of the run. The guide holds it.
- **Slow-time pattern**: A repeatable cause of a longer run, such as serial tests, a fixed delay, a second build, or a flaky test. It has a measured effect and a rule to avoid it.
- **Failure class**: The cause group of a failed run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The median time from the push to the end of the last job, for a code push, is 4 minutes or less. The baseline is 6.8 to 9.3 minutes, with a median of about 8 minutes. The measurement uses at least 5 runs on the hosted runners after the final change.
- **SC-002**: The worst of those 5 runs is 5 minutes or less, not counting queue time. Suppose the measured floor is above 4 minutes for the median. Then the team records the floor and its cause. The team then reaches the lowest time that keeps all checks. The quality rules have priority over the limit.
- **SC-003**: The test time on the critical path falls by at least 50 %. The baseline is 250 to 362 seconds for the old test step. The new test time is the wall-clock time from the end of the compile job to the end of the last test job. It includes the artifact download, the job start, and all test work.
- **SC-004**: The number of tests in a full run does not fall. The baseline is 2936 unit tests, 656 integration tests plus 1 performance test, and 726 contract tests. All of them pass.
- **SC-005**: A push that changes only specs or docs has a total check time of 1 minute or less. A push that changes only web UI files has a total check time of 3 minutes or less. A real push proves each path rule. A file of an unknown group runs all jobs.
- **SC-006**: The first 20 runs on the default branch after the merge show a failure rate of 5 % or less. This counts failures from a flaky test or an infrastructure cause. The baseline comes from FR-010. This is a post-merge check. The guide names the date and the owner.
- **SC-007**: The CI time guide exists. It lists every pattern from this analysis with a measured effect and a rule. A maintainer who did not write it can measure a run with it. The maintainer gets numbers within 20 % of the recorded numbers.
- **SC-008**: No check, gate, or scan from before the change is missing after the change. A before and after list proves it.

## Assumptions

- The repository is public. Hosted runners have 4 virtual CPUs. More parallel jobs have no extra cost for the owner. If this changes, the team must check the cost of parallel jobs again.
- The CI stays on the hosted Windows runner for the build. The team MAY run one probe on a Linux runner. Some code is Windows-specific, so test changes can be needed.
- The CodeQL jobs are a separate check that is set up outside the workflow files. They run in parallel. They do not change in this feature. The team records their end time next to the workflow time, because CodeQL can become the longest check after the speed-up.
- The 5-hour and 43-hour runs of the dependency bot are waiting time, not step time. They are out of scope, except for a note in the guide.
- The release installer workflow is manual only and is out of scope.
- Feature 133 handled the local build time. This feature handles the CI run time.
- No status check is required on the default branch. The speckit pipeline waits for the CI run before it opens a pull request. Each new job or skip rule must keep this wait working.
- Analysis rules and warnings-as-errors stay at the current strictness (feature 133 rule).

# Feature Specification: Reduce Build Time

**Feature Branch**: `133-reduce-build-time`
**Created**: 2026-10-11
**Status**: Implemented
**Input**: User description: "We need to bring build time down. We did this before, but the time is rising again. Check for redundant steps and for code structure, and bring the build time down. Expected local duration of a build without tests: under 4 minutes, ideally 2-3 minutes."

## Clarifications

### Session 2026-10-11

- Q: What does "build" cover? → A: The default solution build command that developers run, in the default configuration. It includes any web UI step that this command starts. Installer packaging and test runs are excluded. Rationale: this is the command the developer runs most often.
- Q: May code analysis rules be relaxed on the developer PC to gain speed? → A: No. The same rules stay active in all configurations. Rationale: project policy treats warnings as errors, and a local-only relaxation hides defects until CI.
- Q: Which PC and state count for the measured result? → A: The developer PC, with packages restored and no build output from a previous build (clean). Three runs, no other heavy program running. Rationale: gives a repeatable number.
- Q: What if the 4-minute limit cannot be reached with the analysis rules kept? → A: The team must then record the measured floor and the cause, and must still reach the lowest time that keeps all rules. Rationale: the quality rules have priority over the limit.

### Session 2026-10-11 (loop 1)

- Q: How does SC-003 (30% fall) work if the baseline is already low, or if the floor is recorded under FR-008? → A: SC-003 applies only if the baseline is above 4 minutes. If the floor is recorded under FR-008, the team reports the achieved fall and the floor instead, and SC-003 is not a failure. Rationale: the 4-minute limit is the real goal.
- Q: What is the exact build command for the baseline and final runs? → A: The default solution build command: `dotnet build C:\src\GameBot\GameBot.sln` in the Debug configuration. Both runs use it with no extra option. Rationale: same command gives comparable numbers.
- Q: Must a change in one project avoid a rebuild of unrelated projects? → A: Yes, and the team MUST check it. After a change in one source file of the lowest-level project and in one file of the top-level service project, only the projects that depend on the changed project may compile again. Rationale: closes the edge-case gap.
- Q: What proves that CI still passes (SC-006)? → A: The CI workflow runs on the pushed branch finish with success. Local dry runs of the CI commands are only a pre-check. Rationale: only the real run proves it.
- Q: What if the default build has no web UI step? → A: The measurement MUST state this result explicitly (for example, "no web UI step in the default build"). Rationale: the spec mentions a web UI step, so an absent step must be recorded, not left silent.

### Session 2026-10-11 (loop 2)

- Q: In what order do the test baseline and the clean step run? → A: The test-baseline run (SC-005) happens BEFORE the clean step. The clean step runs immediately before each timed clean build, so no test output stays in `bin` or `obj`. Rationale: a test run fills `bin` and `obj`, so a clean state must come after it.
- Q: How does the team write the measure script? → A: As one task, because it is one file. No parallel split of one file.
- Q: Which build settings does the team test for the compiler server and parallel build? → A: `UseSharedCompilation`, `-maxcpucount`, `BuildInParallel`, and `Deterministic`. Each test is linked to the per-project timing results. Rationale: a named list is testable.

### Session 2026-10-11 (loop 2)

- Q: Does the project policy for spec status and architecture docs apply to this feature? → A: Yes. At the end, the spec `Status` line and the spec status index MUST be current. If a project split happens, the architecture document MUST change in the same pull request, with a new review date. Rationale: the project constitution (Principle V) requires it.
- Q: When may the team split a project into several projects? → A: Only as a last resort. The trigger is: the ranked cause list names a project-wide cause, and the clean build is still 4 minutes or more after the method and file splits. After such a split, the team MUST run the FR-007 incremental checks again. Rationale: a project split changes the dependency graph.
- Q: Which build state must the CI proof (SC-006) cover? → A: The final state of the branch. The CI proof is the last step, after the result record, the cleanup, and the doc links. Any later change to the branch needs a new CI run. Rationale: a CI run on an earlier commit does not prove the final commit.
- Q: Are settings and package decisions made before the code splits still valid after the splits? → A: The team MUST measure the kept settings and packages again in the final measurement, after the splits. A decision that is not confirmed by the final measurement is reverted. Rationale: the splits change the code shape and the earlier baseline.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fast local build without tests (Priority: P1)

A developer changes code and builds the full solution on their PC, without running tests. Today this build takes too long. The developer wants a result in less than 4 minutes. The best result is 2 to 3 minutes.

**Why this priority**: The developer builds many times each day. A long build slows each change, each fix, and each speckit pipeline run. This is the main goal of the feature.

**Independent Test**: Run a clean full-solution build without tests, on the developer PC, and measure the wall-clock time. Compare it to the recorded baseline.

**Acceptance Scenarios**:

1. **Given** a clean checkout with restored packages, **When** the developer builds the full solution without tests, **Then** the build ends in less than 4 minutes with no errors.
2. **Given** a clean checkout with restored packages, **When** the developer builds the full solution without tests, **Then** the build ends in 3 minutes or less (target).
3. **Given** a build that already finished and no source change, **When** the developer builds again, **Then** the build ends in much less time than the clean build, because it does no repeated work.

---

### User Story 2 - Find and show the cause of the build time (Priority: P1)

A maintainer needs to know where the build time goes. This feature must give a measured breakdown of the build time per project and per step. The breakdown must show which steps are redundant (work that is done more than once, or work with no effect on the result) and which code structure makes the build slow.

**Why this priority**: The build time rose again after an earlier fix. Without a measured cause, a new fix is a guess. A recorded cause also helps the next maintainer.

**Independent Test**: Read the recorded breakdown. Each named cause must have a measured time. After the changes, measure the same items again and compare.

**Acceptance Scenarios**:

1. **Given** the current repository, **When** a maintainer measures a clean build, **Then** the result lists the time per project and per major build step, and names the largest causes.
2. **Given** the list of redundant steps, **When** a maintainer removes or merges a step, **Then** the measured time before and after is recorded.

---

### User Story 3 - Build time stays low (Priority: P2)

After the fix, the build time must not rise again without notice. A maintainer needs a short guide and a simple check, so a future change that raises the build time is easy to see.

**Why this priority**: The build time was fixed once before and then rose again. Keeping the gain is as important as the first gain.

**Independent Test**: Read the guide. Follow its check on a clean build and see a time value that the maintainer can compare to a stated limit.

**Acceptance Scenarios**:

1. **Given** the finished feature, **When** a maintainer follows the guide, **Then** the maintainer can measure the build time and compare it to the stated limit.
2. **Given** a new large source file or a new build step, **When** the maintainer follows the guide, **Then** the guide states which rules to keep.

---

### User Story 4 - CI and other tools keep their quality checks (Priority: P2)

The speed gain must not remove quality checks. The code analysis rules, warnings treated as errors, and the tests must give the same protection after the change.

**Why this priority**: A fast build that hides defects has no value. The project already treats warnings as errors.

**Independent Test**: Run the full build and the full test suite on the final code. Confirm that the same rules are active and that all tests that passed before still pass.

**Acceptance Scenarios**:

1. **Given** the final code, **When** the full build runs in CI, **Then** it ends with no new warnings and no errors.
2. **Given** the final code, **When** the full test suite runs, **Then** all tests that passed before the change still pass.

---

### Edge Cases

- A build on a PC with fewer CPU cores than the developer PC: the time can be longer. The measured result must state the PC used.
- A first build after package restore with an empty package cache: the download time is outside the scope of the target.
- A build after a change in one project only: the build must not rebuild unrelated projects.
- A single very large method or file: it can slow code analysis for the whole project, as in the earlier fix.
- A change that removes a step used by the installer, the release flow, or CI: these flows must keep working.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The team MUST measure and record the baseline of a clean full-solution build without tests, before any change.
- **FR-002**: The team MUST measure the build time per project and per major build step, and MUST record the largest causes of build time. The record MUST state explicitly whether the default build starts a web UI step (for example, "no web UI step in the default build").
- **FR-003**: The team MUST find build steps that are redundant (done more than once, or with no effect on the result) and MUST remove or merge them, unless the step is needed by CI, the installer, or the release flow.
- **FR-004**: The team MUST find source files and methods whose size or structure makes the code analysis slow, and MUST restructure them so the build time falls. The team splits ONLY the methods and files named in the measured, ranked cause list. Each split has a stated target (for example, the analyzer time of the method or project falls below a figure that the team sets from the measurement). A split task MUST NOT start before the ranked cause list is done.
- **FR-005**: The project MUST NOT build the same project more than once in one full-solution build, unless a documented reason exists.
- **FR-006**: A clean full-solution build without tests MUST end in less than 4 minutes on the developer PC. The target is 3 minutes or less, and the ideal is between 2 and 3 minutes.
- **FR-007**: An incremental build with no source change MUST do no repeated compile work. After a change in one source file, only the projects that depend on the changed project MAY compile again; unrelated projects MUST NOT compile again. The team MUST check this for a change in the lowest-level project and for a change in the top-level service project.
- **FR-008**: The change MUST keep the same code quality rules active in all configurations: the analysis level, the warnings treated as errors, and the pinned analyzer versions. The team MUST NOT relax a rule only on the developer PC. If the 4-minute limit cannot be reached with all rules kept, the team MUST record the measured floor and its cause, and MUST reach the lowest time that keeps all rules.
- **FR-009**: The change MUST NOT change the product behavior. All tests that passed before MUST still pass.
- **FR-010**: CI workflows, the installer build, and the release flow MUST keep working after the change.
- **FR-011**: The team MUST record the final measured result (clean build, incremental build) next to the baseline.
- **FR-012**: The team MUST write a short maintainer guide that lists the causes found, the rules to keep the build time low, and how to measure the build time.
- **FR-013**: A project split MAY happen only as a last resort, when the ranked cause list names a project-wide cause and the clean build is still 4 minutes or more after the method and file splits. After a split, the FR-007 checks MUST run again, and the architecture document MUST change in the same pull request with a new review date.
- **FR-014**: At the end, the spec `Status` line and the spec status index MUST be current. Settings and package decisions MUST be confirmed by the final measurement, after all splits; an unconfirmed decision MUST be reverted.

### Key Entities

- **Build baseline**: The recorded time of a clean full-solution build without tests, before the change. Includes the PC description and the time per project.
- **Build time breakdown**: The list of projects and build steps with their measured times, and the largest causes marked.
- **Redundant step**: A build step that repeats work or has no effect on the result.
- **Build time limit**: The stated maximum time (4 minutes), the target (3 minutes), and the ideal range (2 to 3 minutes).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A clean full-solution build without tests ends in less than 4 minutes on the developer PC, measured 3 times; the worst of the 3 results is below the limit.
- **SC-002**: The median of the 3 clean-build measurements is 3 minutes or less (target). A median between 2 and 3 minutes is the ideal.
- **SC-003**: If the recorded baseline is above 4 minutes, the clean build time falls by at least 30% compared to the baseline. If the baseline is 4 minutes or less, or if a floor is recorded under FR-008, the team reports the achieved fall and the floor, and SC-003 is not a failure.
- **SC-004**: An incremental build with no source change ends in less than 30 seconds.
- **SC-005**: The full test suite result is the same as before the change: no test that passed before now fails.
- **SC-006**: All CI workflow runs on the final commit of the pushed branch finish with success. The CI proof is the last step; no commit follows it. Local dry runs of the CI commands are a pre-check only.
- **SC-007**: A maintainer can find the build time causes, the rules, and the measure method in one document, and can repeat the measurement in less than 10 minutes of effort. The guide check MUST record the effort time (the time from the start of the guide to the measured result, not counting the build run itself) in the build record.

## Assumptions

- "Build without tests" means the default full-solution build command in the default configuration. It compiles all projects, includes any web UI step that the command starts, and does not run any test or package the installer.
- The developer PC is the Windows PC used for this project. The baseline and the final result use the same PC.
- The baseline and final runs use the same command: `dotnet build C:\src\GameBot\GameBot.sln` in the Debug configuration, with no extra option.
- The 4-minute limit, the 3-minute target, and the 2-to-3-minute ideal apply to a clean build with packages already restored.
- Code analysis rules stay at the current strictness. Removing or relaxing rules is out of scope, because the project policy treats warnings as errors.
- The earlier build time fix (smaller methods and files) is known. The new work starts with a new measurement, not with the assumption that the same cause applies.
- Test run time is out of scope, except that tests must keep passing.
- Package download time is out of scope.

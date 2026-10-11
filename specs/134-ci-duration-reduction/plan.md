# Implementation Plan: Reduce CI Run Duration

**Branch**: `134-ci-duration-reduction` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/134-ci-duration-reduction/spec.md`

## Summary

A normal PR check run takes 6.8 to 9.3 minutes. The `build` job sets that time. It has about 40 s of setup and 113 to 158 s of build with all analyzers. It has 250 to 362 s of tests with coverage. All three test projects run fully serial. The integration and contract tests share one runner.

The plan has five parts:

1. Add a `changes` job that sorts the pushed files into path groups and sets job switches. A final `ci-gate` job always runs and gives one pass signal. The speckit pipeline always finds a run.
2. Compile the solution once for the tests, with the analyzers off, and share the output. Run the full-analysis build (warnings as errors) in a parallel job.
3. Run the test projects in separate parallel jobs. Split the integration tests into shards. Collect coverage only in the unit job and in one pinned integration shard. These are the only gate sources. Run the coverage thresholds in the Linux `ci-gate` job, and run the performance gate in the pinned shard.
4. Measure per-test times once. Fix or isolate flaky tests and avoidable fixed delays that the data shows.
5. Write `docs/ci-time.md` with the budget, the measured numbers, the slow-time patterns, and the rules.

Decisions from the analysis and the clarification (2026-10-11):

- A compile or secret-scan failure stops the test jobs. An `analyze` failure fails `ci-gate` and the run, but does not stop the test jobs (FR-007).
- The time limits cover the `.NET CI` workflow only. CodeQL runs apart and its time is recorded apart.
- Test jobs use `coverlet.collector` with a run settings file. The `EmulatorImageEndpoints` gate tests are pinned to one integration shard.
- SC-003 measures the time from the end of `compile` to the end of the last test job.
- SC-006 is a post-merge check on the first 20 runs on `master`.

In-process test parallelism is a measured experiment, not the default. 236 test files read or set `GAMEBOT_*` environment variables. These are global for the process. Process-level parallelism (jobs and shards) is safe by construction.

## Technical Context

**Language/Version**: GitHub Actions workflow YAML, PowerShell 7 scripts (`pwsh`), .NET 9 SDK, Node 24 (existing web UI job)
**Primary Dependencies**: `actions/checkout@v5`, `actions/setup-dotnet@v5`, `actions/setup-node@v5`, `actions/cache@v5`, `actions/upload-artifact@v6`, `actions/download-artifact@v6`; xUnit 2.7.1; coverlet 6.0.0 (existing)
**Storage**: Build artifacts of one run (short retention); no application data
**Testing**: xUnit tests: unit (2936), integration (656 + 1 performance), and contract (726). Jest web UI tests. Script tests are xUnit tests in `tests/unit/Ci`
**Target Platform**: GitHub-hosted `windows-latest` (4 vCPU, public repository). A Linux probe is time-boxed (see research R7)
**Project Type**: CI pipeline for a .NET service and a web UI
**Performance Goals**:
- Code push, median `.NET CI` time: 4 min or less (SC-001).
- Code push, worst case: 5 min or less (SC-002).
- Test time from the end of `compile`: down 50 % or more (SC-003).
- Docs-only push: 1 min or less (SC-005).
- Web-UI-only push: 3 min or less (SC-005).
**Constraints**: No check, gate, or scan is removed (FR-001, SC-008). Analysis rules stay at full strictness. No required status check exists on `master`. The speckit pipeline needs a run to exist for every push. Workflow text must be STE
**Scale/Scope**: 1 workflow file changed. 2 to 4 scripts added. 1 coverage script changed. 1 guide added. Product code changes only for flaky-test or delay fixes that the data shows

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Result | Note |
|-----------|--------|------|
| I. Code quality | Pass | Analysis rules stay at full strictness; the secret scan stays and runs on every push. New scripts follow repo script style |
| II. Testing standards | Pass | No test is removed. A new script test proves the path rules and the shard completeness. Flaky tests are fixed or quarantined with an owner (matches the Definition of Done) |
| III. UX consistency | Pass | Check names stay readable; the guide lists them |
| IV. Performance | Pass | The plan declares goals (SC-001 to SC-003), and the guide holds the budget and measurement steps |
| V. Living documentation | Pass | `docs/architecture.md` has no change in domain, API, or persistence. Spec `Status` and `specs/STATUS.md` are updated at the end. The guide is current-state documentation |
| VI. STE | Pass | All new text is written in STE |
| Release blocker | Pass | The branch CI must be green before the PR |

**Post-design re-check (Phase 1)**: Pass. The design adds no weaker check. The `ci-gate` job fails when a needed job fails, is cancelled, or produces no result.

## Project Structure

### Documentation (this feature)

```text
specs/134-ci-duration-reduction/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── workflow-contract.md   # Job names, switches, path groups, gate rules
├── checklists/requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
.github/workflows/dotnet.yml          # Changed: changes, scan, compile, analyze, test matrix, web-ui, ci-gate jobs
scripts/ci/
├── get-changed-groups.ps1            # New: path groups to job switches (default: run all)
├── get-test-shard-filter.ps1         # New: shard name to test filter (last shard is the complement)
└── measure-ci.ps1                    # New: read past runs, print step and queue times
scripts/check-coverage-thresholds.ps1 # Changed: read the coverage files of the unit and integration jobs
docs/ci-time.md                       # New: CI time guide
README.md                             # Changed: link to the guide
specs/STATUS.md                       # Changed: status line for 134
tests/unit/Ci/                        # New: tests for the two path and shard scripts
```

**Structure Decision**: Pipeline-only change. Logic that can break (path groups, shard filters, coverage gate) lives in small scripts under `scripts/ci/` so that unit tests can prove it. The workflow only wires the scripts together. Product code changes only for flaky tests or fixed delays that the measurement names.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| A second compile of the same code (analysis job) | The test path needs a build without analyzer cost; the analysis build keeps full strictness (FR-006 allows it) | One build with analyzers keeps about 115 to 158 s on the critical path. It is kept if R2 shows a gain smaller than the noise |

# Implementation Plan: Turn On and Repair the Disabled Tests

**Branch**: `194-fix-disabled-tests` | **Date**: 2026-11-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/194-fix-disabled-tests/spec.md`

## Summary

Turn on the 3 disabled back-end tests (D-1, D-2, D-3). Repair the cause of each failure. Add a new CI job that runs the Playwright browser tests (D-4) on each push that changes the web UI. Add a guard script that fails the build for an unconditional skip marker, a focus marker, or a test in comments.

The research (see [research.md](research.md)) ran each disabled test on 2026-11-19:

- D-1 passes 3 of 3 times. The cause of the old failure is not in the current code. The test turns on with no product change.
- D-2 passes 3 of 3 times. The test host now applies the session limit. The test turns on with no product change. The old NOTE comment goes.
- D-3 fails only on 2 log assertions. The evaluator writes no log now. The log assertions go, and a stronger size assertion replaces them.
- D-4 passes: 69 of 69 browser tests. Only the CI job is necessary.
- The full baseline (R-6) found 3 more items. D-5: the gamepack type check gives a result that changes with the test order, and the newest `master` build fails on it. D-6: `DeviceRecoveryFlowTests` fails under load. D-7: 2 tests that failed in older builds.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (back-end tests). TypeScript 5.6 (web UI). PowerShell 7 (`pwsh`) for CI scripts. Windows PowerShell 5.1 runs the scripts locally.
**Primary Dependencies**: xUnit 2, FluentAssertions, `Microsoft.AspNetCore.Mvc.Testing` (back end). Jest 29, `@playwright/test` 1.49 or later, Vite 8 (web UI). GitHub Actions.
**Storage**: N/A
**Testing**: `dotnet test` for unit, integration, and contract projects. `npm test` (Jest) and `npm run e2e` (Playwright) in `src/web-ui`. Unit tests for CI scripts in `tests/unit/Ci` through `CiScriptRunner`.
**Target Platform**: Windows (developer PCs and the GitHub `windows-latest` runner). The `changes` and `ci-gate` jobs run on `ubuntu-latest`.
**Project Type**: Web service with a web UI. This feature changes only tests, CI scripts, and the CI workflow.
**Performance Goals**: The CI time budget of `docs/ci-time.md` stays true. A web-UI-only push takes 3 min or less. A code push takes 4 min or less (median). The guard adds 5 s or less to the `changes` job.
**Constraints**: The browser-test job runs in parallel with the other test jobs. It is not after `compile`, so it is not on the .NET critical path. No retry of failed tests. The guard runs on Linux under `pwsh`.
**Scale/Scope**: 3 disabled back-end tests (5 test cases) and 4 failed back-end test classes (D-5 to D-7). 12 browser test files (69 tests) in 2 Playwright projects (`chromium`, `mobile`). The guard reads all test source files below `tests/` and `src/web-ui`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: This feature does not change the web UI. Phone support level: not applicable. The phone browser tests (`mobile` project) are in scope as tests.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | Pass | The guard script has a header comment with inputs, outputs, and exit codes. No new dependency. |
| II. Testing | Pass | This feature removes skipped tests and adds the browser tests to CI. The guard script gets unit tests in `tests/unit/Ci`. CI blocks on failures. |
| III. UX consistency | Pass | The guard output gives file, line, and a remedy for each item. |
| IV. Performance | Pass | The plan measures the CI time before and after (R-5). |
| V. Living documentation | Pass | `docs/ci-time.md` gets the new job and the guard. `INSTALL.md` gets the new job name. `specs/STATUS.md` gets the row for 194. No change to `docs/architecture.md`, because the product does not change. |
| VI. STE | Pass | All new text uses STE. |
| Version bump | Pass | Phase 2 raises `patch` before the PR. |
| Shard balance | Pass | No shard change. D-1 and D-2 add about 8 s to `integration-rest` and `integration-1`. Measure with `measure-shards.ps1 -Project integration`, and write the result in the PR. |

Post-design check: all gates pass. No item in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/194-fix-disabled-tests/
├── plan.md              # This file
├── research.md          # Phase 0: measured results and decisions
├── data-model.md        # Phase 1: disabled-test record and guard finding
├── quickstart.md        # Phase 1: how to run and check each part
├── contracts/
│   └── find-disabled-tests.md   # Phase 1: CLI contract of the guard script
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
tests/
├── unit/
│   ├── TextMatchEvaluatorTests.cs          # D-3: turn on CropsRegionToExpectedPixelSize
│   └── Ci/
│       ├── DisabledTestGuardTests.cs       # New: tests of the guard script
│       └── GateEvaluationTests.cs          # Add the web-ui-e2e job
└── integration/
    ├── WaitForImageCaptureLoopTests.cs     # D-1: remove the Skip marker
    └── Queues/ConcurrentQueueRunTests.cs   # D-2: remove the Skip marker and the old NOTE

src/GameBot.Domain/Gamepacks/
└── GamepackTypeDiscriminatorCheck.cs       # D-5: likely place of the order-dependent result (find the cause first)

tests/integration/DeviceRecoveryTestSupport.cs   # D-6: a wait time-out fails with a clear message
tests/unit/Gamepacks/                            # D-5: new order test

.gitignore                                  # Playwright output folders

src/web-ui/
├── playwright.config.ts                    # CI settings: retries 0, trace and screenshot on failure, CI reporter
└── tests/e2e/**                            # D-4: repair each failed browser test (see R-4)

scripts/ci/
├── find-disabled-tests.ps1                 # New: the guard
├── evaluate-gate.ps1                       # Add web-ui-e2e as a required job when webui=true
└── test-shards.json                        # Put DisabledTestGuardTests in unit-2

.github/workflows/dotnet.yml                # Guard step in `changes`; new `web-ui-e2e` job; ci-gate needs it
docs/ci-time.md                             # New job, guard, measured times
INSTALL.md                                  # Job list
```

**Structure Decision**: The current layout stays. One new script (`scripts/ci/find-disabled-tests.ps1`) and one new test class (`tests/unit/Ci/DisabledTestGuardTests.cs`). The other changes edit current files.

## Design

### D-1 and D-2: remove the skip markers

Remove the `Skip` argument from each attribute. In `ConcurrentQueueRunTests.cs`, delete the NOTE comment above D-2, because it says that the limit "did not take effect". This is not true now. Run each class 3 times. Then run the full integration project once, so that the tests run with load.

### D-3: repair the obsolete assertions

The evaluator (`src/GameBot.Domain/Triggers/Evaluators/TextMatchEvaluator.cs`) has a logger but writes no log entry. The 2 log assertions test behavior that the product no longer has. Delete them. Do not add a log entry to the product only for the test, because feature 190 removed trace noise. Keep the purpose of the deleted "not 1x1" assertion: assert that the OCR image is at least 100 pixels wide (the crop is 100 x 50 before the upscale). Keep the aspect-ratio assertion.

### D-4: browser tests in CI

1. Run `npm run e2e` locally. Repair each failed test (R-4). A failure from a product defect gets a product repair. A failure from an out-of-date test gets a test repair.
2. Change `playwright.config.ts`: `retries: 0`, `trace: 'retain-on-failure'`, and in CI (`process.env.CI`) the reporter `[['list'], ['html', { open: 'never' }]]`. `reuseExistingServer` is `!process.env.CI`.
3. Add the job `web-ui-e2e` to `.github/workflows/dotnet.yml`:
   - `needs: changes`, `if: needs.changes.outputs.webui == 'true'`, `runs-on: windows-latest`.
   - Steps: checkout; `setup-node` 24.x with npm cache; `npm ci`; cache `~\AppData\Local\ms-playwright` with a key from the `@playwright/test` version in `package-lock.json`; `npx playwright install chromium`; `npm run e2e`.
   - On failure, upload `src/web-ui/playwright-report` and `src/web-ui/test-results` as the artifact `playwright-report` (retention 7 days).
4. `ci-gate` needs `web-ui-e2e`. `evaluate-gate.ps1` requires `web-ui-e2e` when `webui=true`.

### Guard: `scripts/ci/find-disabled-tests.ps1`

See [contracts/find-disabled-tests.md](contracts/find-disabled-tests.md). The script reads the files under `tests/` (`*.cs`) and `src/web-ui/src` plus `src/web-ui/tests` (`*.ts`, `*.tsx`). It reports each finding with file, line, kind, and remedy. Exit code 1 if it finds an item, 0 if not. It runs in the `changes` job after the secret scan, so it runs on each push, also a docs-only push. It needs no build.

### D-5: order-dependent gamepack type check

1. Reproduce: `dotnet test` of the contract project with the filter `FullyQualifiedName~GamepackBadTypeContractTests` fails 3 of 3 times. The unit filter `FullyQualifiedName~GamepackFileSerializerInvalidTypeTests` also fails.
2. Find the shared state that changes the result of `GamepackTypeDiscriminatorCheck.Find` (possible cause: see research.md, D-5) (`src/GameBot.Domain/Gamepacks/GamepackTypeDiscriminatorCheck.cs`) or of the code that calls it in `GamepackFileSerializer`.
3. Repair the product code, so that each order gives the same result: 400 with the JSON path for each bad type member.
4. Add a unit test that runs the 4 bad-type cases in 2 different orders in one process and expects the same result.

### D-6: `DeviceRecoveryFlowTests` under load

1. Change `DeviceRecoveryHost.WaitForAsync` (`tests/integration/DeviceRecoveryTestSupport.cs`) so that a time-out fails the test with a message that names the condition. A silent time-out hides the cause.
2. Run the integration project with the class 3 times under load (the full project). Read the log of a failed run.
3. If the message is lost, first write a test that reproduces the loss every time and fails (constitution II). Then repair the product. If the recovery is correct but slow under load, set the time-out of that wait above the measured time, and record the measured time in a code comment.

### D-7: older CI failures

Run each test 10 times with its CI shard filter. Repair a failure. If all runs pass, write the result in the PR.

### .gitignore

Add `src/web-ui/test-results/` and `src/web-ui/playwright-report/`.

### CI time

Measure the `web-ui-e2e` job time on the first push (R-5). If a web-UI-only push takes more than 3 min, use these steps in this order: cache the browser; set `workers` to 4 in CI; split the job into 2 jobs, one for each Playwright project. Write the measured times in `docs/ci-time.md`.

## Complexity Tracking

No violation. This section is empty.

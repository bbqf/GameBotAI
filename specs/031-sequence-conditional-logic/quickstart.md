# Quickstart: Visual Conditional Sequence Logic

## Prerequisites

- Windows development environment
- .NET SDK and Node.js versions used by this repository
- Backend and web-ui dependencies restored

## 1) Build and baseline tests

1. Run workspace task `build` (`dotnet build -c Debug`).
2. Run workspace task `test` (`dotnet test -c Debug`).
3. Confirm baseline is green before feature validation.

## 2) Run service and authoring UI

1. Start backend task `run-service`.
2. Start frontend task `run-web-ui`.
3. Open authoring UI and navigate to sequence editor.

## 3) Validate conditional authoring and branching

1. Create a sequence with one command step and one condition step.
2. Configure true/false branches with valid targets.
3. Save and reload; confirm visual graph and logical expression are preserved.
4. Execute once with condition true and once false; verify expected branch path each run.

## 4) Validate nested logic and image operand

1. Build nested expression using `AND` + `OR` + `NOT`.
2. Configure image operand and threshold.
3. Execute with matching screen state; verify at least-one-match-at-threshold selects true branch.
4. Execute with no qualifying match; verify false branch is selected.

## 5) Validate failure and bounded-cycle behavior

1. Cause an unevaluable condition (missing image target or evaluator error).
2. Verify current condition step is marked failed and sequence stops immediately.
3. Create a cycle without iteration limits; verify save/activation is rejected.
4. Add explicit cycle limit and run past limit; verify current step fails and sequence stops.
5. Start a new run and verify cycle counters reset at run start.

## 6) Validate optimistic concurrency contract

1. Open same sequence in two editor instances.
2. Save instance A, then attempt save from stale instance B.
3. Verify API returns HTTP `409` with payload containing `sequenceId` and `currentVersion`.
4. Reload stale editor and retry save successfully.

## 7) Validate logging and deep-link behavior

1. Execute a conditional sequence and inspect execution logs.
2. Verify each step log includes immutable IDs (`sequenceId`, `stepId`) and readable labels.
3. Enable debug logging; verify each condition includes operand results, operator evaluation, and final decision.
4. Follow a valid deep link; verify direct navigation to sequence step.
5. Remove a previously linked step and open historical log deep link; verify fallback to sequence overview with "referenced step missing" message.

## 8) Validate performance target

1. Run repeated conditional-step evaluations with debug traces enabled under normal load.
2. Measure conditional-step evaluation latency distribution.
3. Verify p95 added latency per conditional step is ≤ 200 ms.

## 9) Phase 6 verification record (2026-03-03)

- `dotnet build -c Debug`: **Passed**.
- `dotnet test -c Debug --logger trx --results-directory TestResults`: **Passed** (`362/362`).
- `powershell -NoProfile -File scripts/analyze-test-results.ps1 -ResultsDir TestResults -LatestOnly -VerifySecurity`: **Passed**.
- Added and validated Phase 6 tests:
	- `LegacySequenceCompatibilityIntegrationTests`
	- `ConditionalExecutionPerformanceIntegrationTests`
	- `OpenApiBackwardCompatTests` conditional-flow assertions
- Quality gate script enhancements completed:
	- Optional coverage gate verification (`-VerifyCoverage`, Cobertura thresholds)
	- Optional security gate verification (`-VerifySecurity`)

# Quickstart: Per-Step Optional Conditions

## Prerequisites

- Windows development environment.
- Service and web UI can run locally.
- Clean-slate scope: only new per-step sequences are used for this feature.

## 1) Author a linear sequence with optional per-step conditions

Create a sequence with three steps:
1. Step `go-home` action: click Home button, condition: `imageVisible(mapImage)`
2. Step `go-back` action: click Back button, condition: `imageVisible(bagImage)`
3. Step `open-event-menu` action: click OpenEventMenu image, condition: none

Validation expectations:
- Save succeeds with mixed conditional and unconditional steps.
- No entry-step or branch-link fields are required.
- Reloading sequence preserves all step-level condition settings.

## 2) Validate commandOutcome condition authoring

Add an additional step condition using `commandOutcome`:
- Example: Step `open-event-menu` condition `commandOutcome(stepRef=go-back, expectedState=skipped)`.

Validation expectations:
- Save succeeds when `stepRef` points to a prior step.
- Save fails when `stepRef` references current/later step.
- Save fails when `expectedState` is not one of `success|failed|skipped`.

## 3) Validate runtime semantics

Run sequence under screen permutations:
1. map visible, bag visible
2. map visible, bag not visible
3. map not visible, bag visible
4. map not visible, bag not visible

Expected behavior:
- Each conditioned step evaluates independently right before execution.
- True -> action executed.
- False -> step skipped, sequence continues.
- Condition evaluation errors -> sequence fails and stops.

## 4) Validate execution logging

For each step outcome, verify presence of:
- `stepId`
- `conditionResult` (for conditioned steps)
- `actionOutcome` (`executed`, `skipped`, `failed`)
- Failure message when evaluation/execution fails

## 5) Validation commands

- `dotnet build -c Debug`
- `dotnet test -c Debug`
- `npm --prefix src/web-ui test -- --runInBand`
- `powershell -NoProfile -File scripts/analyze-test-results.ps1 -ResultsDir TestResults -LatestOnly`

## 6) Performance non-regression check

- Run representative mixed sequence under existing normal-load profile.
- Confirm conditional evaluation p95 does not regress beyond existing budget (<=200 ms target profile). 

## 7) Final Verification Evidence (2026-03-10)

- `dotnet test -c Debug --logger trx --results-directory TestResults`
	- Result: passed (`401/401` tests), latest TRX files generated under `TestResults/`.
- `powershell -NoProfile -File scripts/analyze-test-results.ps1 -ResultsDir TestResults -LatestOnly -VerifyCoverage -VerifySecurity -VerifyLintFormat -VerifyStaticAnalysis`
	- Result: passed all enabled gates.
	- Coverage gate behavior: skipped touched-file threshold enforcement because no `src/*.cs` files were modified in the final polish commit window.
	- Security gate evidence: `.NET dependency scan`, `web-ui npm audit`, and repository secret scan all passed.
	- Lint/format evidence: `dotnet format --verify-no-changes` passed for changed C# files; no changed web-ui files required eslint execution.
	- Static-analysis evidence: analyzer-enabled `dotnet build` passed.

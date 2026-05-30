# Quickstart: Conditional Sequence Steps (Minimal)

## Prerequisites

- Windows development environment.
- Service builds and tests are green (`dotnet build -c Debug`, `dotnet test -c Debug`).
- Repository is treated as clean-slate for this feature (no existing actions/commands/sequences).

## 1) Author minimal conditional sequence

Create sequence with three ordered steps:
1. `conditional`: `imageVisible(A)` -> action `primitiveTap(x1,y1)`
2. `conditional`: `imageVisible(B)` -> action `primitiveTap(x2,y2)`
3. `action`: action `primitiveTap(x3,y3)`

Validate save succeeds and payload uses explicit `stepType` on all steps.
Validate action payloads can be any currently supported action type in both unconditional and conditional steps.
Create an additional sequence where one step uses a non-tap supported action type (for example, a supported swipe/input action) and validate save/validation succeeds.

## 2) Validate execution semantics

Run sequence in four visibility states:
1. A true, B false -> step1 executed, step2 skipped, step3 executed.
2. A false, B true -> step1 skipped, step2 executed, step3 executed.
3. A true, B true -> step1 executed, step2 executed, step3 executed.
4. A false, B false -> step1 skipped, step2 skipped, step3 executed.

Additionally verify:
- False condition does not fail sequence.
- Evaluation error (missing/deleted image reference at runtime, capture failure, evaluator timeout) fails step and stops sequence.

## 3) Validate logging contract

For each step result, confirm log/response fields include:
- `stepType`
- `conditionSummary` (conditional only)
- `conditionResult` (`true`/`false`/`error`)
- `actionOutcome` (`executed`/`skipped` or `failed` when applicable)

## 4) Validate empty-state persistence

- Confirm first sequence save succeeds when data repository starts empty.
- Confirm saved JSON shape contains explicit `stepType` for each step.

## 5) Validate determinism oracle

- Execute the same sequence repeatedly with identical image visibility/frame inputs.
- Compare ordered per-step pairs of (`conditionResult`, `actionOutcome`) across runs.
- Ignore run-instance metadata differences (`executionId`, timestamps, log record IDs).

## 6) Validate performance goal

Measure conditional-step evaluation under normal load profile:
- One active sequence execution (no concurrency)
- 30 total steps per run
- 10 conditional steps per run
- 15-minute continuous run

Pass criteria:
- p95 added latency per conditional evaluation <= 200 ms.

## 7) Phase 6 verification record (2026-03-06)

- `dotnet build -c Debug`: **Passed**.
- `dotnet test -c Debug --logger trx --results-directory TestResults`: **Passed** (`384/384`).
- `dotnet test` targeted additions: **Passed**
	- `tests/integration/Sequences/ConditionalStepPerformanceIntegrationTests.cs`
	- `tests/contract/Sequences/SequenceConditionalStepsOpenApiTests.cs`
- `scripts/analyze-test-results.ps1 -ResultsDir TestResults -LatestOnly -VerifyCoverage -CoverageFile <TestResults/.../coverage.cobertura.xml> -VerifySecurity -VerifyLintFormat -VerifyStaticAnalysis`: **Passed**.
	- Coverage gate enforces thresholds when changed runtime `src/*.cs` files are present; this run skipped threshold enforcement because only tests/contracts/scripts were touched.
	- Security checks passed (`dotnet list --vulnerable`, `npm audit --omit=dev --audit-level=high`, installer secret scan script).
	- Lint/format checks passed for changed-file scope (`dotnet format whitespace --verify-no-changes --include <changed .cs>`, eslint when web-ui files are touched).
	- Static-analysis check passed (`dotnet build GameBot.sln -c Debug`).

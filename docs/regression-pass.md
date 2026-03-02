# Regression Pass: Unified Authoring UI

Scope: Actions, Commands, Triggers, Games, Sequences pages (unified layout).

## Scenarios to cover (manual)
- Actions: create, edit, delete; validate name required; description optional; list reload reflects changes.
- Commands: create/edit with steps (actions/commands), detection optional; reorder steps persists; validation blocks missing name or detection reference ID when set.
- Triggers: create/edit with criteria JSON, actions/commands arrays, optional sequence; invalid JSON blocks save; reorder persists.
- Games: create/edit with metadata key/values; validation on name; delete flow handles references.
- Sequences: create/edit with command steps; reorder persists; delete step.

## Execution notes
- Run against local dev (`npm run dev`) and backend (`dotnet run -c Debug --project src/GameBot.Service`).
- Use realistic sample data; confirm dropdowns populate; confirm create-new links open.
- Verify order persistence by saving, returning to list, re-opening item.
- Capture any failures with steps + expected vs actual; file issues with repro.
- For installer/versioning scenarios, verify state files exist and are readable:
	- `installer/versioning/version.override.json`
	- `installer/versioning/release-line.marker.json`
	- `installer/versioning/ci-build-counter.json`

## Latest Run
- Date: __
- Tester: __
- Findings: __
- Issues filed: __

## Execution Logs Tab Regression Evidence (2026-03-02)

### T001 – Baseline build/test
- **Command**: `dotnet build -c Debug; dotnet test -c Debug`
- **Result**: Pass (`333/333` tests passed).
- **Artifact**: Console output from local verify run (2026-03-02).

### T038 – Backend/API performance test (1,000 logs)
- **Command**: `dotnet test .\tests\integration\GameBot.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~ExecutionLogsPerformanceIntegrationTests" --logger "console;verbosity=detailed"`
- **Result**: Pass.
- **Artifact**:
	- `Execution logs first-open p95: 5.14 ms (budget < 100 ms)`
	- `Execution logs filter/sort p95: 1.03 ms (budget < 300 ms)`

### T040 – Full verify + execution logs scenarios
- **Command**:
	- `dotnet test -c Debug --logger trx`
	- `powershell -NoProfile -File .\scripts\analyze-test-results.ps1`
	- `npm test -- --runInBand --coverage=false ExecutionLogs.test.tsx ExecutionLogsResponsive.test.tsx`
- **Result**: Pass.
- **Artifact**:
	- TRX files under `tests/**/TestResults/*.trx`
	- Analyzer summary: `All tests passed across 6 TRX file(s)`
	- UI scenarios: `2 passed, 2 total` suites.

### T044 – Frontend lint/format gate
- **Command**: `npm run lint` (in `src/web-ui`)
- **Result**: Pass with warnings only (`0 errors`, `2 warnings`).
- **Artifact**: ESLint console output (warnings in existing files outside this feature scope).

### T045 – Backend format/static-analysis gate
- **Command**:
	- `dotnet build -c Debug`
	- `dotnet format --verify-no-changes --verbosity minimal`
- **Result**:
	- `dotnet build`: Pass.
	- `dotnet format --verify-no-changes`: Fail due pre-existing repository-wide analyzer/whitespace debt not introduced by this feature.
- **Artifact**: Console output including CA1515/CA2007 and WHITESPACE findings in unrelated existing test files.

### T046 – Security scan gate
- **Command**:
	- `dotnet list GameBot.sln package --vulnerable --include-transitive`
	- `npm audit --omit=dev --audit-level=high` (in `src/web-ui`)
	- `powershell -NoProfile -File .\scripts\installer\run-security-scans.ps1`
- **Result**: Pass (`0 vulnerabilities`, secret scan completed).
- **Artifact**: Console output from combined security scan run.

### T047 – Coverage threshold verification (touched area)
- **Command**:
	- `runTests` coverage mode on `tests/unit/ExecutionLogs/ExecutionLogRepositoryQueryTests.cs`
	- Coverage target file: `src/GameBot.Domain/Logging/FileExecutionLogRepository.cs`
- **Result**:
	- Line coverage pass: `95.17%` (`138/145`).
	- Branch coverage: deferred to repo-level Cobertura gating (existing branch gate baseline `70%`) until per-file branch extraction is standardized in automation.
- **Artifact**: C# Dev Kit coverage summary output and `tools/coverage/output/coverage.cobertura.xml` baseline.

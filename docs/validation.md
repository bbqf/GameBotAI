# Validation Log

## Installer versioning inputs
- Checked-in override values: `installer/versioning/version.override.json`
- Release-line transition marker: `installer/versioning/release-line.marker.json`
- CI authoritative build counter: `installer/versioning/ci-build-counter.json`

## SC-001 – Command creation usability (Authoring UI)
- **Date**: 2025-12-28
- **Objective**: Confirm non-technical users can create a new Command without encountering JSON/ID fields and finish within 3 minutes.
- **Method**: Internal timed walkthrough using unified authoring UI (Commands tab). Scenario: start from landing page, create new Command with name, select one action via dropdown, add one detection target, save.
- **Participants**: 1 (internal, non-technical proxy). *Note: additional external users recommended to reach statistical confidence.*
- **Result**: Completed in 2m 12s; no JSON/ID fields shown; save succeeded on first attempt.
- **Issues observed**: None blocking. Minor: dropdown fetch spinner briefly overlaps label; not affecting completion.
- **Next steps**: Recruit at least 2 external non-technical users to confirm ≥90% success and timing threshold; monitor dropdown spinner polish.

## SC-004 – Cross-page clarity survey (Authoring UI)
- **Date**: 2025-12-28
- **Objective**: Validate that clarity ≥ 4/5 for ≥90% of users after editing multiple object types.
- **Method**: Guided task sequence (edit Action, Command, Trigger) followed by a single-question clarity survey (1–5 Likert scale). Conducted remotely via screen-share with existing unified UI build.
- **Participants**: 5 (3 non-technical, 2 semi-technical).
- **Result**: 5/5: 3 users, 4/5: 2 users (100% ≥4/5). Average: 4.6/5. No navigation confusion reported.
- **Issues observed**: Minor: one user noted dropdown hints could be closer to fields; no blockers.
- **Next steps**: Keep hint placement under watch during next UX round; repeat survey post-release with ≥10 users to confirm stability.

## Primitive Tap Feature Validation (2026-02-27)

- **Backend full suite**: `dotnet test -c Debug` passed (299/299).
- **Targeted PrimitiveTap + regression suites**: Passed for unit/integration/contract and web-ui command tests.
- **Security scans**:
	- `.NET dependency vulnerability scan`: `dotnet list GameBot.sln package --vulnerable --include-transitive` reported no vulnerable packages.
	- `web-ui` production dependency scan: `npm audit --omit=dev --audit-level=high` reported 0 vulnerabilities.
	- Repository secret signature scan (`git grep` for common key/token signatures) found no matches.
- **Coverage check (touched-area sample)**:
	- `CommandExecutor.cs` measured at 60.71% statement coverage in focused coverage run (136/224), below the target threshold and tracked for follow-up expansion.

- **Coverage check (touched-area final)**:
	- Broader command execution suite coverage run measured `CommandExecutor.cs` at 84.38% statement coverage (189/224), satisfying the >=80% touched-area threshold.

## SC-004 Primitive Authoring Completion-Time Evidence (2026-02-27)

- **Method**: Automated authoring flow timing from `CommandsPage` UI test (`creates a command with a primitive tap step`) in Jest.
- **Observed duration**: ~52 ms for the scripted primitive authoring + save flow in test environment.
- **Context**: This is automation timing (not human usability timing) and is recorded as repeatable baseline evidence for flow completion behavior.

## Primitive API Performance Evidence (2026-02-27)

- Measured against local debug service (`http://localhost:5081`) over 25 iterations each:
	- Create command with PrimitiveTap: `p95=1.91 ms`, `avg=1.35 ms`
	- Update command with PrimitiveTap: `p95=2.19 ms`, `avg=1.56 ms`
- Result: command API latency remains comfortably below the plan target (`< 200 ms p95`).

## Execution Log Feature Validation (2026-02-27)

- **Build validation**: `dotnet build -c Debug` succeeded after implementation wiring and analyzer remediation.
- **Test validation**: `dotnet test -c Debug --logger trx` succeeded with `299/299` passing tests.
- **Required failure analysis**: `scripts/analyze-test-results.ps1` reported `All tests passed across 3 TRX file(s)` after clearing stale TRX files.
- **Contract validation**: Swagger example coverage includes execution-log routes, including `PUT /api/execution-logs/retention` request/response examples.
- **Privacy check**: Execution log persistence path enforces redaction/masking through `ExecutionLogSanitizer` before repository writes.
- **Security scans**:
	- `.NET dependency vulnerability scan`: `dotnet list GameBot.sln package --vulnerable --include-transitive` reported no vulnerable packages.
	- Repository secret-signature scan (`git grep` over common API key/private-key patterns) returned no matches.
- **Coverage evidence (touched area)**:
	- `src/GameBot.Service/Services/CommandExecutor.cs`: `85.71%` statement coverage (`runTests` coverage mode).
	- `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`: `85.32%` statement coverage (`runTests` coverage mode).
	- Coverage artifact recorded at `tools/coverage/execution-log-coverage-20260227.json`.
- **Performance evidence (p95 write/query)**:
	- `PUT /api/execution-logs/retention` (25 iterations): `p95=4.44 ms`, `avg=10.68 ms`.
	- `GET /api/execution-logs?pageSize=50` (25 iterations): `p95=6.31 ms`, `avg=4.51 ms`.

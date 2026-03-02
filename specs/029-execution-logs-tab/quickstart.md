# Quickstart: Execution Logs Tab

## Prerequisites

- Windows development environment
- .NET SDK matching repository requirements
- Node.js environment for web UI

## 1) Build and test baseline

1. Build:
   - Run workspace task `build` (`dotnet build -c Debug`).
2. Run tests:
   - Run workspace task `test` (`dotnet test -c Debug`).

## 2) Run service and web UI

1. Start backend service:
   - Run workspace task `run-service`.
2. Start web UI:
   - Run workspace task `run-web-ui`.
3. Open the application and verify tab order:
   - "Execution" -> "Execution Logs" -> "Configuration".

## 3) Functional verification checklist

- Default list shows columns: timestamp, execution object name, status.
- Default sort is timestamp descending.
- Default first load contains 50 most recent rows.
- Clicking any column header toggles sort direction for that column.
- Per-column free-text filters are case-insensitive contains.
- Combined sorting + filtering works in one result set.
- Selecting a row opens detail view with summary, related objects, optional snapshot, and step outcomes.
- Detail rendering remains non-technical (no raw JSON shown).
- Timestamp defaults to exact local and can switch to relative mode.
- Rapid filter/sort changes show only newest response (latest-request-wins behavior).
- Desktop variant shows split list/detail; phone variant supports list -> detail drill-down with state preserved.

## 4) Local performance validation (1,000 logs)

- Seed/load dataset with at least 1,000 execution log entries in local data.
- Measure API + page interaction timings across repeated runs.
- Validate local p95 targets:
  - First open list load: `<100ms`
  - Filter/sort update: `<300ms`
- Record results in PR notes for any hot-path changes.

## 5) CI performance guardrail

- Execute performance checks in CI pipeline with relaxed thresholds:
  - First open list load: `<200ms`
  - Filter/sort update: `<450ms`
- Fail CI when thresholds are exceeded at p95.

## 6) Non-functional checks

- Confirm no new high/critical static analysis or security issues.
- Confirm deterministic tests for query semantics and detail projection.
- Confirm user-facing error/empty states are actionable and non-technical.

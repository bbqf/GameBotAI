# Feature Specification: Security Alerts Fix

**Feature Branch**: `191-security-alerts-fix`  
**Created**: 2026-11-17  
**Status**: Implemented  
**Input**: User description: "fix the dependable and other security alerts created by github"

## Context

On 2026-11-17, the repository has these open GitHub security alerts:

| Source | Alert | Package | Where | Severity | Vulnerable range | First fixed version |
|--------|-------|---------|-------|----------|------------------|---------------------|
| Dependabot | #79, GHSA-g3hv-75wg-m249 ("minimatch has potential denial of service via globparts amplification") | `minimatch` (npm) | `src/web-ui/package-lock.json`, development scope | Moderate | `>= 7.0.0, < 11.0.1` | `11.0.1` |

The web UI installs `minimatch` 9.0.9 through `@typescript-eslint/parser` 7.18.0 → `@typescript-eslint/typescript-estree` 7.18.0. All other copies of `minimatch` in the web UI are 3.1.5. Version 3.1.5 is not in the vulnerable range. The local npm audit shows 6 moderate findings. All 6 come from this one advisory.

Code scanning has no open alerts. Secret scanning has no open alerts. The .NET solution has no vulnerable NuGet packages (direct or transitive).

## Clarifications

### Session 2026-11-17

- Q: How do we remove the vulnerable `minimatch` 9.0.9: an npm override of only that copy, or an upgrade of `@typescript-eslint` to major version 8? → A: An override that forces `minimatch` `^11.0.1` on the 9.x copy only. Rationale: it is the smallest change, and the project already uses overrides for `js-yaml` and `brace-expansion`; major version 8 of `@typescript-eslint` is a breaking change.
- Q: Do we close alert #79 by hand on GitHub? → A: No. Dependabot closes it as fixed after the merge to `master`. Rationale: a manual dismissal hides the real state and needs a person with admin access.
- Q: Do we add an `npm audit` step to CI, or a Dependabot configuration file? → A: No, both are out of scope. Rationale: an audit gate can fail CI on new advisories that have no relation to a change; the GitHub alerts already work without a configuration file.
- Q: Which Node.js version must the forced `minimatch` version support? → A: Node.js 24 (CI uses `24.x`; the developer setup uses 24.12.0). Rationale: `minimatch` 11 needs Node.js `20 || >=22`, so 24 is supported.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The repository has no open security alerts (Priority: P1)

The maintainer opens the GitHub security page of the repository. Dependabot alert #79 is closed because the lock file no longer has a vulnerable `minimatch` version. No other open alert stays.

**Why this priority**: This is the request. An open alert is a known risk, and it hides new alerts.

**Independent Test**: Run the npm audit for the web UI. It shows 0 vulnerabilities. Search the lock file for `minimatch` entries. No entry has a version in the vulnerable range.

**Acceptance Scenarios**:

1. **Given** the change is on `master`, **When** Dependabot scans the repository again, **Then** alert #79 is closed as fixed.
2. **Given** a clean install of the web UI from the lock file, **When** the maintainer runs the npm audit, **Then** it reports 0 vulnerabilities.
3. **Given** the .NET solution, **When** the maintainer lists vulnerable packages (direct and transitive), **Then** no project has a vulnerable package.

---

### User Story 2 - The developer tools continue to work (Priority: P1)

A developer runs lint, the unit tests, and the build of the web UI. They work as before the change.

**Why this priority**: The vulnerable package is part of the lint tools. A fix that breaks lint or the build is not acceptable.

**Independent Test**: Run lint, the Jest tests, and the production build of the web UI. All three pass with the same result as on `master`.

**Acceptance Scenarios**:

1. **Given** the fixed lock file, **When** a developer runs lint on the web UI, **Then** lint completes with no new errors or warnings.
2. **Given** the fixed lock file, **When** a developer runs the web UI tests and the build, **Then** both pass.
3. **Given** the fixed lock file, **When** CI runs on the branch, **Then** all CI workflows pass.

---

### Edge Cases

- A new alert opens between the scan and the merge: the implementation MUST check the alert lists again before the PR and include each new alert that it can fix the same way.
- No patched version is available in the major version that the parent package asks for: the fix forces the first patched major version on that parent package only. Other copies of the package that are not vulnerable stay as they are.
- The forced version needs a newer runtime: the forced version MUST support the Node.js version that CI and the developer setup use.
- The fix changes the shipped product: it MUST NOT. The vulnerable package is a development dependency only. The shipped web UI bundle and the service MUST not change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The web UI lock file MUST NOT contain a `minimatch` version in the range `>= 7.0.0, < 11.0.1`.
- **FR-002**: The fix MUST be an npm `overrides` entry in `src/web-ui/package.json` that forces `minimatch` `^11.0.1` on the 9.x copy only, so that a later `npm install` keeps the fixed version. It MUST NOT be a manual edit of the lock file only.
- **FR-003**: The fix MUST change only the vulnerable copy of `minimatch`. The copies at 3.1.5 MUST stay unchanged, and the direct development dependencies MUST keep their major versions.
- **FR-004**: The npm audit of the web UI MUST report 0 vulnerabilities after the fix.
- **FR-005**: The lint, test, and build commands of the web UI MUST pass after the fix, with no new lint warnings.
- **FR-006**: The fix MUST NOT change the runtime dependencies of the web UI, the .NET projects, or the shipped files.
- **FR-007**: Before the PR, the implementation MUST list the open Dependabot, code scanning, and secret scanning alerts again, and the NuGet vulnerable packages. It MUST record the result in the feature directory.
- **FR-008**: The change MUST have a `CHANGELOG.md` entry and a version bump, as the constitution says.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 0 open Dependabot alerts on `master` after the merge and the next scan (was 1).
- **SC-002**: 0 vulnerabilities in the npm audit of the web UI (was 6 moderate).
- **SC-003**: 0 vulnerable NuGet packages in the solution (no change from now).
- **SC-004**: 100% of the CI workflows pass on the branch.
- **SC-005**: 0 new lint warnings or errors in the web UI.

## Assumptions

- "Other security alerts" means the other GitHub alert lists: code scanning and secret scanning. Both are empty now. The NuGet audit is also included as a check.
- The project already uses npm `overrides` for transitive security fixes (`js-yaml`, `brace-expansion`). This fix uses the same method.
- An upgrade of `@typescript-eslint` to major version 8 is out of scope, because it is a breaking change to the lint setup. The override is the smaller change.
- The web UI is not changed for users, so a phone support level does not apply.
- Out of scope: a manual dismissal of alerts, an `npm audit` gate in CI, and a Dependabot configuration file (see Clarifications).

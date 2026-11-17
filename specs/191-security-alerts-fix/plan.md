# Implementation Plan: Security Alerts Fix

**Branch**: `191-security-alerts-fix` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/191-security-alerts-fix/spec.md`

## Summary

Close Dependabot alert #79 (GHSA-g3hv-75wg-m249, `minimatch` `>= 7.0.0, < 11.0.1`). The only vulnerable copy is `minimatch` 9.0.9. `@typescript-eslint/typescript-estree` 7.18.0 installs it in the web UI as a development dependency.

The change has these parts:

1. Add the entry `"minimatch@9": "^11.0.1"` to `overrides` in `src/web-ui/package.json`. This entry changes only the 9.x copy. The 3.1.5 copies do not match the selector, so they stay.
2. Run `npm install` in `src/web-ui` to update `package-lock.json`. Then run `npm ci` from the lock file to make sure that a clean install works.
3. Make sure that `npm audit` reports 0 vulnerabilities, and that lint, Jest, and the build pass.
4. List the GitHub alerts (Dependabot, code scanning, secret scanning) and the vulnerable NuGet packages again. Record the result in `specs/191-security-alerts-fix/alert-audit.md`.
5. Add a CHANGELOG entry, bump the version, and set the spec status.

## Technical Context

**Language/Version**: TypeScript 5.6 and Node.js 24 (web UI tools); C# 13 on .NET 9 (service, audit only)  
**Primary Dependencies**: npm 11 `overrides`; `@typescript-eslint/parser` 7.18.0 and `@typescript-eslint/typescript-estree` 7.18.0 (not changed); `minimatch` 9.0.9 → 11.x  
**Storage**: N/A  
**Testing**: `npm audit`, `npm ls minimatch --all`, `npm ls --omit=dev --all` (runtime tree, before and after), `npm run lint`, `npm test` (Jest), `npm run build` (Vite), `dotnet list package --vulnerable --include-transitive`, `gh api` for the alert lists  
**Target Platform**: Developer computers and GitHub Actions (`windows`, Node.js `24.x`)  
**Project Type**: Web service with a web UI. This feature changes the development dependencies of the web UI only.  
**Performance Goals**: No change. The shipped bundle does not include `minimatch`. Lint time stays the same (±10%).  
**Constraints**: No change to the runtime dependencies, the shipped bundle, or the .NET projects (FR-006). The major versions of the direct development dependencies stay the same (FR-003).  
**Scale/Scope**: 2 web UI files (`package.json`, `package-lock.json`), 1 audit record, 1 CHANGELOG entry, 1 version bump, 1 status row.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI for users. No phone support level applies.

| Gate | Result | Note |
|------|--------|------|
| I. Code quality | Pass | Lint MUST pass with 0 errors and no new warnings (SC-005). The security check gate (SAST and secret scan) is the purpose of this feature. |
| II. Testing | Pass | No product code changes, so no new unit test. The checks are `npm audit` (0 findings), lint, Jest, the build, and CI. A "failing test before the fix" is the current `npm audit` result (6 moderate). Record it in `alert-audit.md`. |
| III. UX consistency | Pass | No change for users. |
| IV. Performance | Pass | No hot path changes. The CHANGELOG entry says "no performance effect". |
| V. Living docs | Pass | `docs/architecture.md` does not list development dependencies, so it does not change. Spec 191 gets the status "Implemented" and a row in `specs/STATUS.md`. No earlier spec is reworked. |
| VI. STE | Pass | All new text in this plan and its artifacts uses STE. |
| Version bump | Planned | Bump `patch` in `installer/versioning/version.override.json` before the push (CLAUDE.md rule). |

Post-design check: all gates stay Pass.

## Project Structure

### Documentation (this feature)

```text
specs/191-security-alerts-fix/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (the dependency change)
├── quickstart.md        # Phase 1 output (how to check the fix)
├── alert-audit.md       # Made during implementation (FR-007)
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

No `contracts/` folder: the feature changes no interface that users or other systems use.

### Source Code (repository root)

```text
src/web-ui/package.json                       # add "minimatch@9": "^11.0.1" to overrides
src/web-ui/package-lock.json                  # updated by npm install
CHANGELOG.md                                  # Security entry
installer/versioning/version.override.json    # patch bump
specs/STATUS.md                               # 191 row
```

**Structure Decision**: The feature changes the current web UI manifest and lock file. It adds no new project, folder, or source file.

## Complexity Tracking

No constitution violation. This section is empty.

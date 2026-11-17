# Alert Audit: Security Alerts Fix

Date: 2026-11-17. Node.js: v24.12.0.

## Before

- `npm audit` (`src/web-ui`): 6 moderate severity vulnerabilities. All come from GHSA-g3hv-75wg-m249 ("minimatch has potential denial of service via globparts amplification").
- `npm ls minimatch --all`: `minimatch@9.0.9` below `@typescript-eslint/parser@7.18.0` → `@typescript-eslint/typescript-estree@7.18.0`. All other copies are `minimatch@3.1.5` (eslint, eslint plugins, jest `glob`, `babel-plugin-istanbul`).
- `npm ls --omit=dev --all`: `@dnd-kit/core@6.3.1`, `@dnd-kit/sortable@10.0.0`, `@dnd-kit/utilities@3.2.2`, `react@18.3.1`, `react-dom@18.3.1`, and their dependencies (`tslib`, `loose-envify`, `js-tokens`, `scheduler`).
- `npm run lint`: 0 errors, 1 warning (`react-hooks/exhaustive-deps` in `TargetForm.tsx`). Duration: 11.5 s (first, cold run); 9.0 s and 8.9 s (warm runs).

## After

- `npm audit` (`src/web-ui`): `found 0 vulnerabilities` (FR-004, SC-002).
- `npm ls minimatch --all`: `minimatch@11.0.9` below `@typescript-eslint/typescript-estree@7.18.0`. All other copies stay `minimatch@3.1.5` (FR-001, FR-003).
- `npm ci` from the updated lock file: completes with no error.
- Lock file diff (`src/web-ui/package-lock.json`): only the `minimatch` 11.x dependency tree changes:
  - `minimatch` 9.0.9 → 11.0.9.
  - `brace-expansion` 2.1.7 → 5.0.13. Only `minimatch` 9.0.9 used the 2.x copy. The `brace-expansion@1.1.21` copies of `minimatch` 3.1.5 do not change.
  - New: `balanced-match` 4.0.4 (nested below `brace-expansion`) and `@isaacs/nonrecursive-glob-match` 1.1.0. Both are dependencies of `minimatch` 11 and are development-only.
  - The `brace-expansion@2` override now matches no package. It stays, so that a later install cannot bring back a vulnerable 2.x copy.
- `npm ls --omit=dev --all`: the same output as before (FR-006).
- `npm run lint`: 0 errors, 1 warning (the same warning). Duration: 9.9 s (first run); 9.2 s, 8.9 s, 9.1 s (warm runs). The warm runs are in ±10% of the warm baseline (SC-005).
- `npm test`: 217 test suites, 1640 tests, all pass.
- `npm run build`: passes (only the old "chunks larger than 500 kB" message).

## Other alert lists (FR-007)

| List | Open alerts | Note |
|------|-------------|------|
| Dependabot | 1 | #79, `minimatch`, GHSA-g3hv-75wg-m249, medium. Dependabot closes it after the merge to `master` (SC-001). |
| Code scanning | 0 | |
| Secret scanning | 0 | |
| NuGet (`dotnet list GameBot.sln package --vulnerable --include-transitive`) | 0 | All 8 projects: "has no vulnerable packages" (SC-003). |

No new alert opened after the design scan.

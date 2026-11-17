# Research: Security Alerts Fix

## R1. Which alerts are open?

- **Decision**: The scope is Dependabot alert #79 only.
- **Rationale**: On 2026-11-17, `gh api repos/bbqf/GameBotAI/dependabot/alerts?state=open` returns one alert (#79, `minimatch`, moderate). The code scanning list and the secret scanning list are empty. `dotnet list GameBot.sln package --vulnerable --include-transitive` reports no vulnerable package in the 8 projects. `npm audit` in `src/web-ui` reports 6 moderate findings. All 6 come from GHSA-g3hv-75wg-m249: `minimatch` and the 5 `@typescript-eslint` packages that depend on it.
- **Alternatives considered**: None. The lists are the source of truth.

## R2. Which copy of `minimatch` is vulnerable?

- **Decision**: Only `minimatch` 9.0.9, below `@typescript-eslint/typescript-estree` 7.18.0.
- **Rationale**: `npm ls minimatch --all` shows 9.0.9 (one copy) and 3.1.5 (eslint, eslint plugins, jest, babel-plugin-istanbul). The advisory range is `>= 7.0.0, < 11.0.1`, so 3.1.5 is not in it.
- **Alternatives considered**: None.

## R3. How do we remove the vulnerable copy?

- **Decision**: Add the npm override `"minimatch@9": "^11.0.1"` to `src/web-ui/package.json`.
- **Rationale**:
  - No 9.x or 10.x release has the fix. The advisory gives `11.0.1` as the first patched version, and no backport is listed.
  - The selector `minimatch@9` matches only installs of major version 9. The 3.1.5 copies do not change (FR-003).
  - `typescript-estree` 7.18.0 uses `require("minimatch")` and calls `minimatch_1.minimatch(filePath, pattern)` (only in `useProgramFromProjectService.js`). Version 11 keeps a CommonJS build (`exports["."].require`), the named export `minimatch`, and this call signature.
  - `minimatch` 11 needs Node.js `20 || >=22`. CI uses `24.x`, and the developer setup uses 24.12.0.
  - The project already uses this method: `js-yaml`, `brace-expansion@1`, and `brace-expansion@2` are in `overrides`.
- **Alternatives considered**:
  - Upgrade `@typescript-eslint/*` to 8.73.0 (`npm audit fix --force`). Rejected: a major version change to the lint setup, with new rules and a new configuration format. That is out of scope.
  - Edit only `package-lock.json`. Rejected: a later `npm install` can bring back 9.x (FR-002).
  - Dismiss the alert because the scope is "development". Rejected: the alert stays a known risk, and the spec does not allow a manual dismissal.

## R4. How do we prove that the fix works?

- **Decision**: Use `npm audit` (0 findings), `npm ls minimatch --all` (no 7.x to 11.0.0 copy), `npm ci` from the lock file, `npm run lint`, `npm test`, `npm run build`, and CI.
- **Rationale**: These checks cover FR-001 to FR-005. After the merge, Dependabot closes alert #79 on its next scan (SC-001). The PR cannot show SC-001 before the merge.
- **Alternatives considered**: A new unit test. Rejected: no product code changes, and the audit is the direct check.

## R5. Is lint output the same?

- **Decision**: Compare the lint output before and after the change. The count of errors and warnings MUST be the same.
- **Rationale**: `minimatch` in `typescript-estree` selects the files of a TypeScript project. A behavior change can change which files lint checks. The same output proves that it does not.
- **Alternatives considered**: None.

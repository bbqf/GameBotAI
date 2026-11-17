# Data Model: Security Alerts Fix

The feature has no stored data. The "model" is the dependency graph of the web UI development tools.

## Security alert

| Field | Value |
|-------|-------|
| Source | Dependabot |
| Number | 79 |
| Advisory | GHSA-g3hv-75wg-m249 |
| Package | `minimatch` (npm) |
| Manifest | `src/web-ui/package-lock.json` |
| Scope | development |
| Vulnerable range | `>= 7.0.0, < 11.0.1` |
| First patched version | `11.0.1` |

State transitions: `open` → `fixed` (Dependabot sets this after the merge to `master` and its next scan). No manual `dismissed` state.

## Dependency change

| Path | Before | After |
|------|--------|-------|
| `@typescript-eslint/parser` → `@typescript-eslint/typescript-estree` → `minimatch` | 9.0.9 | 11.x (`^11.0.1`) |
| `eslint`, `@eslint/eslintrc`, `@humanwhocodes/config-array`, `eslint-plugin-react`, `eslint-plugin-jsx-a11y`, `glob` (jest), `test-exclude` (ts-jest) → `minimatch` | 3.1.5 | 3.1.5 (no change) |
| new `minimatch` 11.x → `brace-expansion` (and `balanced-match`) | `brace-expansion` 2.x below 9.0.9 | the version that `minimatch` 11.x asks for; the 2.x copy that only 9.0.9 used can go away |

Validation rules:

- No `minimatch` entry in `package-lock.json` has a version from 7.0.0 to 11.0.0.
- The `dependencies` block of `src/web-ui/package.json` does not change.
- The `devDependencies` block of `src/web-ui/package.json` does not change.

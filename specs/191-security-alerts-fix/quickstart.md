# Quickstart: Security Alerts Fix

Run these commands from any folder. All paths are absolute.

## 1. Clean install from the lock file

```powershell
npm --prefix C:\src\GameBot\src\web-ui ci
```

Expected: the install completes with no error.

## 2. Audit

```powershell
npm --prefix C:\src\GameBot\src\web-ui audit
npm --prefix C:\src\GameBot\src\web-ui ls minimatch --all
npm --prefix C:\src\GameBot\src\web-ui ls --omit=dev --all
```

Expected: `found 0 vulnerabilities`. The `minimatch` copy below `@typescript-eslint/typescript-estree` is 11.x. All other copies are 3.1.5. The runtime tree (`--omit=dev`) is the same as on `master`.

## 3. Lint, tests, and build

```powershell
npm --prefix C:\src\GameBot\src\web-ui run lint
npm --prefix C:\src\GameBot\src\web-ui test
npm --prefix C:\src\GameBot\src\web-ui run build
```

Expected: all three pass. Lint shows the same errors and warnings count as on `master`.

## 4. .NET packages

```powershell
dotnet list C:\src\GameBot\GameBot.sln package --vulnerable --include-transitive
```

Expected: each project "has no vulnerable packages".

## 5. GitHub alert lists

```powershell
gh api "repos/bbqf/GameBotAI/dependabot/alerts?state=open" --jq "length"
gh api "repos/bbqf/GameBotAI/code-scanning/alerts?state=open" --jq "length"
gh api "repos/bbqf/GameBotAI/secret-scanning/alerts?state=open" --jq "length"
```

Expected before the merge: `1`, `0`, `0`. Expected after the merge and the next Dependabot scan: `0`, `0`, `0`.

# Quickstart: Standalone Windows Installer (EXE/MSI)

## 1. Build installer payload

```powershell
.\scripts\package-installer-payload.ps1 -Configuration Release -Runtime win-x64
```

## 2. Build installer scaffold artifacts

```powershell
.\scripts\build-installer.ps1 -Configuration Release
```

## 3. Interactive install smoke check

1. Run generated installer EXE from `installer/wix/bin/Release`.
2. Select mode/scope and complete install.
3. Verify:
   - App files installed under expected root
   - Backend and web UI endpoints reachable
   - Log file created under `%LocalAppData%\GameBot\Installer\logs`

## 4. Silent install smoke check

```powershell
# Example placeholder; exact bootstrapper property names defined in contracts
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT="%LocalAppData%\\GameBot\\data" BIND_HOST=127.0.0.1 PORT=8080 ALLOW_ONLINE_PREREQ_FALLBACK=true
```

Expected:
- Exit code in standardized set: `0`, `3010`, `1603`, `1618`, `2`
- Deterministic logs in `%LocalAppData%\GameBot\Installer\logs`

## 5. Validation checklist

- Installer runs in per-user scope only
- Background mode works in per-user scope
- Interactive installer UI allows changing the data directory and validates writeability before continuing
- Runtime data root is writable at `%LocalAppData%\\GameBot\\data`
- Non-allowlisted prerequisite source is rejected
- Install duration SLOs met in clean-machine tests (excluding reboot)

## 6. Endpoint defaults

1. Installer configures service bind interface via `BIND_HOST` and shared `PORT` value.
2. Browser shortcut opens `http://localhost:[PORT]/` for loopback binding or `http://<computer-name>:[PORT]/` for non-loopback binding.
3. Validate install blocks continuation when bind host or port validation fails.

Remediation:
- If validation fails, provide valid `BIND_HOST` and `PORT` values and rerun install.

## 7. CI runner notes

- `ci-installer-fast.yml` and `ci-installer-logic.yml` run on GitHub-hosted Windows runners for build/test/static/security checks.
- Release signing verification in `release-installer.yml` is a policy marker and requires organization-managed signing credentials in the release environment.
- For local validation, standard user PowerShell is sufficient for installer actions.

## 8. SLO evidence checklist

- Record interactive install start/end times and compute total seconds.
- Record silent install start/end times and compute total seconds.
- Confirm interactive install duration <= 600 seconds (excluding reboot).
- Confirm silent install duration <= 480 seconds (excluding reboot).
- Record validation command outputs and attach them to release notes or PR evidence.

## 9. Latest quality gate outcomes

Executed commands:

- `dotnet format --verify-no-changes`
- `dotnet format analyzers --verify-no-changes`
- `dotnet build -c Debug -warnaserror`
- `dotnet test -c Debug`
- `powershell -NoProfile -File scripts/installer/run-static-analysis.ps1`
- `powershell -NoProfile -File scripts/installer/run-security-scans.ps1`

Observed result summary:

- Format/analyzer/build/test gates failed due existing repository-wide analyzer-as-error findings (for example `CA1515`, `CA2007`) in pre-existing test files.
- Installer-focused filtered test runs succeeded during implementation checks.
- Security scan script execution completed for current repository snapshot.

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
   - Log file created under `%ProgramData%\GameBot\Installer\logs`

## 4. Silent install smoke check

```powershell
# Example placeholder; exact bootstrapper property names defined in contracts
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT="%LocalAppData%\\GameBot\\data" BACKEND_PORT=5000 WEB_PORT=8080 PROTOCOL=http ALLOW_ONLINE_PREREQ_FALLBACK=true
```

Expected:
- Exit code in standardized set: `0`, `3010`, `1603`, `1618`, `2`
- Deterministic logs in `%ProgramData%\GameBot\Installer\logs`

## 5. Validation checklist

- Service mode enforces per-machine scope
- Background mode works in per-user and per-machine scope
- Interactive installer UI allows changing the data directory and validates writeability before continuing
- Runtime data root is scope-correct and writable:
   - Per-machine: `%ProgramData%\\GameBot\\data`
   - Per-user: `%LocalAppData%\\GameBot\\data`
- Non-allowlisted prerequisite source is rejected
- Install duration SLOs met in clean-machine tests (excluding reboot)

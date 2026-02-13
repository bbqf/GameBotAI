# Quickstart

## 1) Build backend and validate baseline
```powershell
cd C:/src/GameBot
dotnet build -c Debug
dotnet test -c Debug
```

## 2) Prepare web UI assets
```powershell
cd C:/src/GameBot/src/web-ui
npm install
npm run build
```

## 3) Interactive install validation flow
1. Launch installer in interactive mode.
2. Select install mode (`service` or `background application`).
3. Confirm prerequisite scan completes and missing prerequisites are installed.
4. Enter/confirm backend port and preferred web UI ports.
5. If a port conflict is reported, accept one of the suggested alternatives.
6. Complete installation and confirm final output announces:
   - Web UI URL
   - Web UI port
   - Backend URL/port

## 4) Unattended CLI validation flow
```powershell
cd C:/src/GameBot
# Example arguments; exact switch names to align with implementation
./scripts/install-gamebot.ps1 `
  -Mode service `
  -BackendPort 5000 `
  -WebPorts "8080,8088,8888,80" `
  -Protocol http `
  -Unattended
```

Expected behavior:
- No interactive prompts.
- Invalid arguments fail fast with actionable remediation text.
- Final console output includes announced endpoint URLs and ports.

## 5) Security and network checks
- Verify backend listens on non-loopback interfaces.
- Verify firewall scoping defaults to private-network access when installer can manage firewall rules.
- Verify explicit warning + confirmation path when firewall rules cannot be applied.

## 6) Startup behavior checks
- Service mode: confirm service is registered and configured for boot auto-start.
- Background mode: confirm app startup is user-login scoped only when enabled.

## 7) Regression checks
- Re-run installer where prerequisites already exist and confirm skip behavior.
- Re-run with occupied preferred ports and confirm deterministic fallback order: `8080 -> 8088 -> 8888 -> 80`.
- Launch web UI and verify API endpoint is preconfigured to installed backend URL/port.

# Quickstart — Runtime Logging Control (001)

## Prerequisites
- .NET 9 SDK installed
- GameBot.Service running on branch `001-runtime-logging-control`
- Config auth token available (set `GAMEBOT_AUTH_TOKEN` before using curl)
- `jq` installed for JSON inspection (optional but recommended)

## 1. Start the service and confirm defaults
```powershell
$env:GAMEBOT_AUTH_TOKEN = "local-dev-token"
dotnet run -c Debug --project src/GameBot.Service
```
Observe that logs emit at Warning or higher (default posture).

## 2. List current component states
```powershell
curl -s https://localhost:5001/config/logging `
  -H "X-Auth-Token: $env:GAMEBOT_AUTH_TOKEN" | jq
```
The response enumerates every component with `enabled` and `effectiveLevel`.

## 3. Raise verbosity for `GameBot.Domain.Triggers`
```powershell
curl -s -X PUT https://localhost:5001/config/logging/components/GameBot.Domain.Triggers `
  -H "X-Auth-Token: $env:GAMEBOT_AUTH_TOKEN" `
  -H "Content-Type: application/json" `
  -d '{
        "level": "Debug",
        "enabled": true,
        "actor": "quickstart"
      }' | jq '.effectiveLevel'
```
Subsequent logs from that component should immediately include Debug entries without restarting the app.

## 4. Temporarily silence `Microsoft.AspNetCore`
```powershell
curl -s -X PUT https://localhost:5001/config/logging/components/Microsoft.AspNetCore `
  -H "X-Auth-Token: $env:GAMEBOT_AUTH_TOKEN" `
  -H "Content-Type: application/json" `
  -d '{
        "enabled": false,
        "actor": "quickstart",
        "notes": "silencing noisy middleware"
      }'
```
Verify that new ASP.NET Core logs stop appearing until re-enabled.

## 5. Reset everything back to Warning/Enabled
```powershell
curl -s -X POST https://localhost:5001/config/logging/reset `
  -H "X-Auth-Token: $env:GAMEBOT_AUTH_TOKEN" `
  -H "Content-Type: application/json" `
  -d '{ "reason": "cleanup", "actor": "quickstart" }' | jq '.defaultLevel'
```
All components should now show `effectiveLevel = Warning` and `enabled = true` via step 2.

## 6. Run validation tests
```powershell
dotnet test -c Debug tests/unit/ tests/integration/ --filter RuntimeLoggingControl
```
Tests cover persistence, authorization, and immediate propagation to ensure the feature meets constitutional gates.

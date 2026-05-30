# Quickstart — Logging Config Refresh (004)

## Prerequisites
- .NET 9 SDK installed
- GameBot.Service running from branch `004-logging-config-refresh`
- Operator auth token (set `GAMEBOT_AUTH_TOKEN` or `Service:Auth:Token` in `appsettings.Development.json`)

## 1. Verify default logging format
```powershell
$env:GAMEBOT_AUTH_TOKEN = "local-dev-token"
dotnet run -c Debug --project src/GameBot.Service | Select-String "\[202"
```
Expected log line template: `[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u}] [{SourceContext}] Message`.

## 2. Inspect current config
```powershell
curl -s -H "Authorization: Bearer local-dev-token" https://localhost:5001/api/logging/config | jq
```

## 3. Raise verbosity for a component
```powershell
curl -s -X POST https://localhost:5001/api/logging/config `
	-H "Authorization: Bearer local-dev-token" `
	-H "Content-Type: application/json" `
	-d '{
				"globalLevel": "Information",
				"components": [
					{ "key": "GameBot.Service.Services.SessionService", "level": "Debug" },
					{ "key": "Microsoft.AspNetCore", "level": "Warning" }
				],
				"source": "quickstart"
			}'
```
Response includes the updated `lastUpdatedUtc` timestamp.

## 4. Reload from disk
If you edited `data/config/config.json` manually, sync runtime switches:
```powershell
curl -X POST https://localhost:5001/api/logging/config/reload -H "Authorization: Bearer local-dev-token"
```

## 5. Run tests
```powershell
dotnet test -c Debug tests/unit/ tests/integration/
```
All new/updated tests under `LoggingConfig*` suites must pass to satisfy the constitution gates.

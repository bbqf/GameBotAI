# Quickstart

## 1) Build and run the service
```powershell
Set-Location C:/src/GameBot
dotnet build -c Debug
dotnet run -c Debug --project src/GameBot.Service
```

## 2) Trigger a command execution that succeeds
```powershell
$commandId = "<existing-command-id>"
$sessionId = "<active-session-id>"
Invoke-RestMethod -Method Post -Uri "http://localhost:5081/api/commands/$commandId/force-execute?sessionId=$sessionId"
```

Expected:
- Execution is accepted.
- A persisted execution log entry is created with `finalStatus=success`.

## 3) Trigger a command/step path that does not execute
```powershell
$commandId = "<command-id-with-detection-threshold-failure>"
$sessionId = "<active-session-id>"
Invoke-RestMethod -Method Post -Uri "http://localhost:5081/api/commands/$commandId/force-execute?sessionId=$sessionId"
```

Expected:
- Persisted entry includes step outcome `not_executed`.
- Entry includes user-facing reason (for example threshold not met).

## 4) Query recent execution logs
```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:5081/api/execution-logs?pageSize=20"
```

Expected:
- Items include timestamp, object identity, hierarchy context, `finalStatus`, step outcomes, and relative navigation context.
- Nested entries include both direct object path and parent hierarchy path.

## 5) Filter for failures of a specific object
```powershell
$objectId = "<command-or-sequence-id>"
Invoke-RestMethod -Method Get -Uri "http://localhost:5081/api/execution-logs?finalStatus=failure&objectId=$objectId&pageSize=20"
```

Expected:
- Returned rows are limited to matching failed executions.

## 6) Inspect one log entry
```powershell
$logId = "<execution-log-id>"
Invoke-RestMethod -Method Get -Uri "http://localhost:5081/api/execution-logs/$logId"
```

Expected:
- Detail includes sanitized `details` payload, hierarchy links, and route-style relative navigation paths.

## 7) Update retention policy (configurable period)
```powershell
$body = @'
{
  "enabled": true,
  "retentionDays": 60,
  "cleanupIntervalMinutes": 30
}
'@
Invoke-RestMethod -Method Put -Uri "http://localhost:5081/api/execution-logs/retention" -ContentType "application/json" -Body $body
```

Expected:
- Policy update is persisted.
- Future cleanup cycles enforce the new retention duration.

## 8) Run automated tests
```powershell
Set-Location C:/src/GameBot
Get-ChildItem -Path tests -Filter *.trx -Recurse | Remove-Item -Force
dotnet test -c Debug --logger trx
./scripts/analyze-test-results.ps1
```

Focus areas:
- Execution-to-log mapping for command and sequence runs.
- Hierarchy linkage and relative navigation context.
- `success/failure` plus `executed/not_executed` semantics.
- Sensitive value masking/redaction before persistence.
- Retention configuration and cleanup behavior.
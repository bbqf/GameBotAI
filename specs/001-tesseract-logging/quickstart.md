# Quickstart — Tesseract Logging & Coverage

## 1. Enable Debug Logging for OCR
```powershell
$env:GAMEBOT_LOG_LEVEL__GameBot__Domain__Triggers__Evaluators__TesseractProcessOcr = "Debug"
$env:GAMEBOT_TESSERACT_PATH = "C:\\Program Files\\Tesseract-OCR\\tesseract.exe"
```
Restart GameBot.Service (or trigger configuration reload) so the logger observes the new level.

## 2. Trigger a Sample OCR Invocation
```powershell
curl -X POST https://localhost:5001/api/triggers/test-ocr `
  -H "Authorization: Bearer dev-token" `
  -H "Content-Type: application/json" `
  -d '{"imageId":"fixtures/ocr/sample-score.png"}'
```
Check logs for a structured entry containing the Tesseract command, sanitized arguments, stdout, stderr, exit code, duration, and `invocationId`.

Example log line (values truncated for brevity):

```
2025-11-24T10:15:12.345Z DBG GameBot.Service.Logging.TesseractInvocationLogger[5200]
  tesseract_invocation invocationId=7c1c0c5c6d8a4e24b8a0c6ce4362086c exe=C:\\Program Files\\Tesseract-OCR\\tesseract.exe args="C:\\Temp\\a.png C:\\Temp\\a --psm 6 --oem 1" context="workDir=C:\\Temp env= exit=0 durationMs=132.40" streams="stdout=stdout message stderr=...<truncated>" truncated=False
```

## 3. Run OCR Integration Tests with Coverage
```powershell
pwsh tools/coverage/report.ps1 `
  -Project tests/integration/GameBot.IntegrationTests.csproj `
  -NamespaceFilter "GameBot.Domain.Triggers.Evaluators.Tesseract*" `
  -TargetPercent 70
```
The script executes `dotnet test`, collects coverlet data, prints a summary (percentage vs target), and exits with non-zero status if coverage < target.

## 4. Publish & Query Coverage Summary
1. **Generate the summary JSON** (writes to `data/coverage/latest.json` automatically). Use the same data directory the API will read from:
   ```powershell
   $repo = "C:\src\GameBot"
   pwsh tools/coverage/report.ps1 `
     -Project tests/integration/GameBot.IntegrationTests.csproj `
     -NamespaceFilter "[GameBot.Domain]GameBot.Domain.Triggers.Evaluators.Tesseract*" `
     -TargetPercent 70 `
     -DataDirectory (Join-Path $repo "data") `
     -ReportUrl "https://cicd/GameBot/coverage/latest.html" `
     -UncoveredScenarios @('timeout_5s','malformed_output')
   ```
   The script emits `data/coverage/latest.json` plus a timestamped history file and exits non-zero if coverage < target.
2. **Start GameBot.Service with the same data root** so `/api/ocr/coverage` sees the new file:
   ```powershell
   $env:GAMEBOT_DATA_DIR = Join-Path $repo "data"
   dotnet run -c Release --project src/GameBot.Service
   ```
3. **Query the endpoint** (requires the standard bearer token). Expect HTTP 200 with coverage metadata:
   ```powershell
   curl https://localhost:5001/api/ocr/coverage `
     -H "Authorization: Bearer dev-token" | jq
   ```
   Example response:
   ```json
   {
     "generatedAtUtc": "2025-11-24T22:15:08.4123456Z",
     "namespace": "GameBot.Domain.Triggers.Evaluators.Tesseract",
     "lineCoveragePercent": 74.3,
     "targetPercent": 70.0,
     "passed": true,
     "uncoveredScenarios": null,
     "reportUrl": "https://cicd/GameBot/coverage/latest.html"
   }
   ```
4. **Handle failure states**. If the JSON is missing or older than 24h the API returns `503` with guidance:
   ```json
   {
     "error": "Coverage summary is stale. Re-run tools/coverage/report.ps1.",
     "details": {
       "generatedAtUtc": "2025-11-20T18:02:10.1020912Z"
     }
   }
   ```
   Re-run the script (step 1) to refresh `latest.json`, then re-query the endpoint.

## 5. Monitor Logs in Production
- Keep debug logging disabled by default; toggle via configuration or runtime logging endpoint only when investigating OCR issues.
- Search logs by `invocationId` to correlate stdout/stderr with upstream session IDs.
- If stdout/stderr are truncated (flagged with `wasTruncated=true`), reproduce locally with the same CLI arguments captured in the log entry.

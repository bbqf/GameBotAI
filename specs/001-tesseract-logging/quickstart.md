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

## 4. Expose Coverage Summary via API
1. Copy the generated summary JSON to `data/coverage/latest.json` (done automatically by the script).
2. Start the service: `dotnet run -c Release --project src/GameBot.Service`.
3. Fetch the summary:
   ```powershell
   curl -H "Authorization: Bearer dev-token" https://localhost:5001/api/ocr/coverage | jq
   ```

## 5. Monitor Logs in Production
- Keep debug logging disabled by default; toggle via configuration or runtime logging endpoint only when investigating OCR issues.
- Search logs by `invocationId` to correlate stdout/stderr with upstream session IDs.
- If stdout/stderr are truncated (flagged with `wasTruncated=true`), reproduce locally with the same CLI arguments captured in the log entry.

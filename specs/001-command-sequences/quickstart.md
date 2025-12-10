# Quickstart — Command Sequences

## Create a Sequence
```powershell
$seq = @{
  name = "Sample Sequence";
  steps = @(
    @{ order = 1; commandId = "cmd-001"; delayMs = 500 },
    @{ order = 2; commandId = "cmd-002"; delayRangeMs = @{ min = 300; max = 800 } },
    @{ order = 3; commandId = "cmd-003"; timeoutMs = 2000; retry = @{ maxAttempts = 2; backoffMs = 250 } }
  )
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/sequences -Body $seq -ContentType 'application/json'
```

## Execute a Sequence
```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/sequences/<sequenceId>/execute
```

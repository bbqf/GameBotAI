# Quickstart — Sequence Logic Blocks

## Setup

```powershell
$env:GAMEBOT_AUTH_TOKEN = "test-token"
$env:GAMEBOT_DATA_DIR = (Join-Path $PWD 'data')
```

## Repeat N Times

```json
{
  "id": "sample-repeat",
  "name": "Repeat N",
  "blocks": [
    {
      "type": "repeatCount",
      "maxIterations": 3,
      "steps": [ { "order": 1, "commandId": "cmd-collect" } ]
    }
  ]
}
```

## Repeat Until Detected

```json
{
  "id": "sample-until",
  "name": "Until Image",
  "blocks": [
    {
      "type": "repeatUntil",
      "timeoutMs": 2000,
      "cadenceMs": 100,
      "condition": { "source": "image", "targetId": "home_button", "mode": "Present", "confidenceThreshold": 0.9 },
      "steps": [ { "order": 1, "commandId": "cmd-open" } ]
    }
  ]
}
```

## If/Then/Else Branch

```json
{
  "id": "sample-branch",
  "name": "Branch by Trigger",
  "blocks": [
    {
      "type": "ifElse",
      "condition": { "source": "trigger", "targetId": "readyTrigger", "mode": "Present" },
      "steps": [ { "order": 1, "commandId": "cmd-start" } ],
      "elseSteps": [ { "order": 1, "commandId": "cmd-wait" } ]
    }
  ]
}
```

## Loop Control

```json
{
  "id": "sample-control",
  "name": "Break/Continue",
  "blocks": [
    {
      "type": "repeatCount",
      "maxIterations": 10,
      "control": {
        "breakOn": { "source": "text", "targetId": "errorBanner", "mode": "Present" },
        "continueOn": { "source": "image", "targetId": "busySpinner", "mode": "Present", "confidenceThreshold": 0.95 }
      },
      "steps": [ { "order": 1, "commandId": "cmd-step" } ]
    }
  ]
}
```

## Create & Execute

### Run the service

Start the API in another terminal:

```powershell
dotnet run -c Debug --project src/GameBot.Service
```

### Create a sequence and execute it

```powershell
$headers = @{ Authorization = 'Bearer test-token' }
$body = Get-Content sample-repeat.json -Raw
Invoke-RestMethod -Uri "http://localhost:5000/api/sequences" -Method POST -Headers $headers -ContentType 'application/json' -Body $body
Invoke-RestMethod -Uri "http://localhost:5000/api/sequences/sample-repeat/execute" -Method POST -Headers $headers -Body ''
```

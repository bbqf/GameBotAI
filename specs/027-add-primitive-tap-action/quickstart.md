# Quickstart

## 1) Build and run service
```powershell
cd C:/src/GameBot
dotnet build -c Debug
dotnet run -c Debug --project src/GameBot.Service
```

## 2) Create a command with a primitive tap step
```powershell
$body = @'
{
  "name": "tap-detected-target",
  "steps": [
    {
      "type": "PrimitiveTap",
      "order": 0,
      "primitiveTap": {
        "detectionTarget": {
          "referenceImageId": "enemy-button",
          "confidence": 0.85,
          "offsetX": 5,
          "offsetY": -3,
          "selectionStrategy": "HighestConfidence"
        }
      }
    }
  ]
}
'@
Invoke-RestMethod -Method Post -Uri "http://localhost:5081/api/commands" -ContentType "application/json" -Body $body
```

Expected:
- Request succeeds and returns command id.
- Primitive tap step persists without requiring an action id.

## 3) Validate missing-detection rejection
```powershell
$invalidBody = @'
{
  "name": "invalid-primitive-tap",
  "steps": [
    {
      "type": "PrimitiveTap",
      "order": 0,
      "primitiveTap": {}
    }
  ]
}
'@
Invoke-RestMethod -Method Post -Uri "http://localhost:5081/api/commands" -ContentType "application/json" -Body $invalidBody
```

Expected:
- API returns `400` with actionable validation error for missing detection target.

## 4) Execute and inspect primitive tap outcomes
```powershell
$commandId = "<replace-command-id>"
Invoke-RestMethod -Method Post -Uri "http://localhost:5081/api/commands/$commandId/force-execute?sessionId=<session-id>"
```

Expected:
- Response includes existing `accepted` count.
- Response includes `stepOutcomes` for primitive steps with status such as:
  - `executed`
  - `skipped_detection_failed`
  - `skipped_invalid_target`

Sample success payload:
```json
{
  "accepted": 1,
  "stepOutcomes": [
    {
      "stepOrder": 0,
      "status": "executed",
      "resolvedPoint": { "x": 0, "y": 0 },
      "detectionConfidence": 0.99
    }
  ]
}
```

Sample skip payload:
```json
{
  "accepted": 0,
  "stepOutcomes": [
    {
      "stepOrder": 0,
      "status": "skipped_invalid_config",
      "reason": "template_not_found"
    }
  ]
}
```

## 5) Regression-check explicit action flow
- Execute a pre-existing command that references only explicit action steps.
- Confirm behavior and accepted input count are unchanged.

## 6) Run automated tests
```powershell
cd C:/src/GameBot
dotnet test -c Debug
```

Focus areas:
- Command create/update validation for `PrimitiveTap`.
- Executor behavior for detection success/failure, out-of-bounds skip, and highest-confidence selection.
- Backward compatibility for action-based commands.

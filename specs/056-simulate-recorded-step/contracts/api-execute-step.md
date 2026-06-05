# API Contract: Execute Single Step

**Route**: `POST /api/steps/execute`  
**Purpose**: Execute one primitive action step against the connected emulator. Used by the command recorder's "Run step" and "Run all" features to validate individual steps in-session without persisting a command.

---

## Request

**Headers**: `Content-Type: application/json`

**Body**:

```json
{
  "step": {
    "type": "PrimitiveTap | KeyInput | Swipe",
    "order": 0,
    "primitiveTap": { ... },
    "keyInput": { ... },
    "swipe": { ... }
  },
  "sessionId": "string (optional)"
}
```

The `step` field reuses the existing `CommandStepDto` shape. Only the field(s) corresponding to the declared `type` need to be populated; others may be omitted.

### PrimitiveTap step

```json
{
  "step": {
    "type": "PrimitiveTap",
    "order": 0,
    "primitiveTap": {
      "detectionTarget": {
        "referenceImageId": "abc123",
        "offsetX": 10,
        "offsetY": -5
      }
    }
  }
}
```

Fields `confidence` and `selectionStrategy` in `detectionTarget` are optional; the backend defaults to `0.8` and `HighestConfidence` respectively.

### KeyInput step

```json
{
  "step": {
    "type": "KeyInput",
    "order": 0,
    "keyInput": { "key": "KEYCODE_HOME" }
  }
}
```

### Swipe step

```json
{
  "step": {
    "type": "Swipe",
    "order": 0,
    "swipe": {
      "startX": 100,
      "startY": 500,
      "endX": 100,
      "endY": 200,
      "durationMs": 300
    }
  }
}
```

---

## Response

### 202 Accepted — successful dispatch

```json
{
  "accepted": 1,
  "stepOutcomes": [
    {
      "stepOrder": 0,
      "status": "executed",
      "stepType": "PrimitiveTap",
      "resolvedPoint": { "x": 540, "y": 960 },
      "detectionConfidence": 0.93
    }
  ]
}
```

### 200 OK — execution timed out (10 s)

Returned as 200 (not 504) so the client handles it as an inline step error, not a network failure.

```json
{
  "accepted": 0,
  "stepOutcomes": [
    {
      "stepOrder": 0,
      "status": "timeout",
      "reason": "Step execution timed out after 10 seconds"
    }
  ]
}
```

### 400 Bad Request — invalid step configuration

```json
{ "error": "missing_required_field", "detail": "primitiveTap.detectionTarget.referenceImageId is required" }
```

### 503 Service Unavailable — emulator not running

```json
{ "error": "emulator_unavailable", "detail": "No active emulator session" }
```

---

## Outcome `status` values

| Value | Meaning |
|---|---|
| `executed` | Step completed successfully |
| `timeout` | No response within 10 seconds |
| `missing_session_context` | No session and no active game context |
| `not_running` | Session exists but emulator is not in running state |
| `skipped_invalid_config` | Step config is structurally valid but semantically incomplete at runtime |

---

## Notes

- `sessionId` is optional. When omitted the executor resolves the active session automatically (same behavior as `force-execute`).
- `Command`-type steps are not accepted by this endpoint (use `force-execute` for recursive command invocation).
- `WaitForImage` and `EnsureGameRunning` step types are accepted for completeness but are not expected to be recorded by the recorder UI (they have no visual equivalent in the VisualStepPicker).

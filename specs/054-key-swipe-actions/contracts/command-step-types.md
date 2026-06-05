# Contract: Command Step Types Extension

**Feature**: 054-key-swipe-actions
**Date**: 2026-06-04
**Endpoint**: `/api/commands` (POST, PATCH, GET)

## Overview

Two new step type values are added to the `type` discriminator field accepted and returned by the Commands API. All existing step types and shapes are unchanged.

## New Step Type: KeyInput

### Request (POST /api/commands or PATCH /api/commands/{id})

```json
{
  "steps": [
    {
      "type": "KeyInput",
      "order": 0,
      "keyInput": {
        "key": "Enter"
      }
    }
  ]
}
```

### Response (GET /api/commands/{id})

Same shape as the request step object. The `keyInput` property is present when `type` is `"KeyInput"`.

### Validation

| Field | Rule |
|-------|------|
| `keyInput.key` | Required. Non-empty string. |

The API does not validate that `key` maps to a supported key name; that is enforced at runtime execution.

## New Step Type: Swipe

### Request

```json
{
  "steps": [
    {
      "type": "Swipe",
      "order": 1,
      "swipe": {
        "startX": 100,
        "startY": 800,
        "endX": 100,
        "endY": 200,
        "durationMs": 300
      }
    }
  ]
}
```

### Response

Same shape as the request step object. The `swipe` property is present when `type` is `"Swipe"`.

### Validation

| Field | Rule |
|-------|------|
| `swipe.startX` | Required integer. |
| `swipe.startY` | Required integer. |
| `swipe.endX` | Required integer. |
| `swipe.endY` | Required integer. |
| `swipe.durationMs` | Optional integer ≥ 0. Omit or null for runtime default. |

Coordinates are absolute screen pixels. No range restriction is enforced by the API.

## Backward Compatibility

- Existing step types (`Command`, `PrimitiveTap`, `WaitForImage`, `EnsureGameRunning`) are unchanged.
- No migration of persisted command files is required.
- The `CommandStepTypeDto` enum values are additive; existing serialized values remain valid.

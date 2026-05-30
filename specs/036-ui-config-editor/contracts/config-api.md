# API Contract: Configuration Endpoints

**Feature**: 035-ui-config-editor
**Date**: 2026-04-14
**Base path**: `/api/config`

## Existing Endpoints (unchanged)

### GET /api/config

Returns the current configuration snapshot.

**Response** `200 OK`:
```json
{
  "generatedAtUtc": "2026-04-14T12:00:00+00:00",
  "serviceVersion": "1.0.0.0",
  "dynamicPort": null,
  "refreshCount": 5,
  "envScanned": 68,
  "envIncluded": 3,
  "envExcluded": 65,
  "parameters": {
    "Logging__LogLevel__Default": {
      "name": "Logging__LogLevel__Default",
      "source": "File",
      "value": "Info",
      "isSecret": false
    },
    "GAMEBOT_TESSERACT_ENABLED": {
      "name": "GAMEBOT_TESSERACT_ENABLED",
      "source": "File",
      "value": "true",
      "isSecret": false
    },
    "GAMEBOT_AUTH_TOKEN": {
      "name": "GAMEBOT_AUTH_TOKEN",
      "source": "Environment",
      "value": "***",
      "isSecret": true
    }
  }
}
```

**Notes**: Parameter key order in the `parameters` object determines UI display order.

### POST /api/config/refresh

Reloads configuration from environment + files and returns the refreshed snapshot.

**Response** `200 OK`: Same shape as GET.

---

## New Endpoints

### PUT /api/config/parameters

Batch-update parameter values. Only changed parameters should be included. Environment-sourced parameters cannot be updated via this endpoint.

**Request body**:
```json
{
  "updates": {
    "Logging__LogLevel__Default": "Debug",
    "Service__Detections__Threshold": "0.9",
    "GAMEBOT_TESSERACT_LANG": "deu"
  }
}
```

**Validation**:
- `updates` must be a non-empty object.
- Keys must be non-empty strings.
- Values are strings or null (null clears/resets to default).
- If any key corresponds to an Environment-sourced parameter, the endpoint returns `400 Bad Request` with an error for that key.

**Response** `200 OK`: Full `ConfigurationSnapshot` (same shape as GET) reflecting the applied changes.

**Response** `400 Bad Request` (validation error):
```json
{
  "error": {
    "code": "invalid_request",
    "message": "Cannot update Environment-sourced parameter 'GAMEBOT_AUTH_TOKEN'.",
    "hint": "Environment parameters can only be changed by modifying host environment variables."
  }
}
```

**Response** `400 Bad Request` (empty updates):
```json
{
  "error": {
    "code": "invalid_payload",
    "message": "At least one parameter update is required.",
    "hint": null
  }
}
```

**Behavior**:
1. Load saved config from `data/config/config.json`.
2. For each key in `updates`: validate it is not Environment-sourced; merge into saved config.
3. Call `RefreshAsync()` which re-applies precedence, runs `IConfigApplier.Apply()`, and persists.
4. Return the new snapshot.

---

### PUT /api/config/parameters/reorder

Persist a new display order for configuration parameters. Called immediately on drag-and-drop.

**Request body**:
```json
{
  "orderedKeys": [
    "Logging__LogLevel__Default",
    "GAMEBOT_TESSERACT_ENABLED",
    "Service__Detections__Threshold",
    "GAMEBOT_USE_ADB"
  ]
}
```

**Validation**:
- `orderedKeys` must be a non-empty array.
- Duplicate keys are silently deduplicated (first occurrence wins).
- Unknown keys are silently ignored.

**Response** `200 OK`: Full `ConfigurationSnapshot` with parameters in the new order.

**Response** `400 Bad Request` (empty array):
```json
{
  "error": {
    "code": "invalid_payload",
    "message": "At least one parameter key is required.",
    "hint": null
  }
}
```

**Behavior**:
1. Load saved config from `data/config/config.json`.
2. Rebuild the parameters dictionary with keys inserted in `orderedKeys` order.
3. Append any keys not in `orderedKeys` at the end (in their previous order).
4. Persist and refresh.
5. Return the new snapshot.

---

## Error Response Shape (consistent with existing API)

All error responses follow the existing pattern:
```json
{
  "error": {
    "code": "string",
    "message": "string",
    "hint": "string | null"
  }
}
```

## Authentication

All endpoints use the existing bearer token authentication. No changes to auth flow.

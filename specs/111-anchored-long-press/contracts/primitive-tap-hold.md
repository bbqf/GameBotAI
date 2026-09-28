# Contract: `holdMs` on a PrimitiveTap step

## Request and read-back

`POST /api/commands`, `PATCH /api/commands/{id}`, `GET /api/commands/{id}`, `GET /api/commands`, `POST /api/steps/execute`.

```json
{
  "type": "PrimitiveTap",
  "order": 0,
  "primitiveTap": {
    "detectionTarget": { "referenceImageId": "claim-button", "confidence": 0.9, "offsetX": 0, "offsetY": 0 },
    "holdMs": 700
  }
}
```

| Case | Result |
|------|--------|
| `holdMs` absent | 201/200. Read-back has no `holdMs`. Runtime: tap of today. |
| `holdMs: 0` | 201/200. Read-back has `holdMs: 0`. Runtime: tap of today. |
| `holdMs: 700` | 201/200. Read-back has `holdMs: 700`. Runtime: press and hold for 700 ms. |
| `holdMs: 5000` | 201/200. |
| `holdMs: -1` or `holdMs: 5001` | 400. The error text contains `primitiveTap.holdMs must be between 0 and 5000`. |
| `holdMs: 700.5` or `holdMs: "abc"` | Not saved. Same error status as for a non-integer value in another integer field of a step (no change by this feature). |
| `fieldTemplates: { "primitiveTap.holdMs": "{{hold}}" }` | 400 (unsupported key, rule of today). |

## OpenAPI

`components.schemas.PrimitiveTapConfigDto.properties.holdMs`:

```json
{
  "maximum": 5000,
  "minimum": 0,
  "type": "integer",
  "format": "int32",
  "nullable": true,
  "description": "Optional. Hold duration in milliseconds, 0 to 5000. More than 0: press and hold at the detected point (plus offsets) for this duration. Absent or 0: a single tap."
}
```

`components.schemas.StepExecutionOutcomeDto.properties.holdMs`: integer, nullable, with a description. It is set only for a step that pressed and held.

## Step outcome (execute responses)

```json
{
  "stepOrder": 0,
  "status": "executed",
  "resolvedPoint": { "x": 540, "y": 1210 },
  "executedPoint": { "x": 542, "y": 1207 },
  "detectionConfidence": 0.97,
  "holdMs": 700
}
```

A step without a hold has no `holdMs` field in `POST /api/commands/{id}/force-execute` and `POST /api/commands/{id}/evaluate-and-execute` responses, and `holdMs: null` is not written.

## Execution log detail

```json
{
  "kind": "tap",
  "message": "Press and hold targeted (540,1210), executed at (542,1207) for 700 ms.",
  "attributes": { "x": 540, "y": 1210, "executedX": 542, "executedY": 1207, "confidence": 0.97, "holdMs": 700 }
}
```

When the executed point is the same as the target: "Press and hold at (540,1210) for 700 ms.". A tap without a hold keeps the text and attributes of today.

# API Contract: POST /api/images/detect-all

## Purpose

Runs image template matching for **all known reference images** against a previously captured screenshot, returning bounding boxes and confidence scores for every match above the default confidence threshold.

Used by the Visual Step Picker to populate image-region overlays.

## Request

```
POST /api/images/detect-all
Content-Type: application/json
```

```json
{
  "captureId": "<string>"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `captureId` | string | yes | Value from the `X-Capture-Id` header of a prior `GET /api/emulator/screenshot` response |

## Response

```
200 OK
Content-Type: application/json
```

```json
{
  "matches": [
    {
      "imageId": "<string>",
      "imageName": "<string>",
      "x": 120,
      "y": 45,
      "width": 80,
      "height": 32,
      "confidence": 0.94
    }
  ]
}
```

| Field | Type | Description |
|---|---|---|
| `matches` | array | All matches found; empty array if no reference images exist or none match |
| `matches[].imageId` | string | `ReferenceImageStore` key |
| `matches[].imageName` | string | Display label (filename without extension) |
| `matches[].x` | int | Bounding box left edge in screenshot pixels |
| `matches[].y` | int | Bounding box top edge in screenshot pixels |
| `matches[].width` | int | Bounding box width in screenshot pixels |
| `matches[].height` | int | Bounding box height in screenshot pixels |
| `matches[].confidence` | float | Match confidence [0–1] |

## Error Responses

| Status | Condition |
|---|---|
| `400 Bad Request` | `captureId` missing or empty |
| `404 Not Found` | No cached screenshot found for the given `captureId` |
| `503 Service Unavailable` | Emulator session not connected |

## Behaviour Notes

- Matching runs in parallel across all reference images (see research.md: Performance Note).
- Only matches at or above the system default confidence threshold are included.
- Multiple matches per image are possible (e.g., repeated UI elements); all are returned.
- When two matches for different images have overlapping bounding boxes, both are returned — overlap resolution is the frontend's responsibility (highest-confidence wins per FR-005).

## Related Endpoints

- `GET /api/emulator/screenshot` — captures the screenshot and returns the `captureId`
- `POST /api/images/detect` — single-image detection (existing, unchanged)

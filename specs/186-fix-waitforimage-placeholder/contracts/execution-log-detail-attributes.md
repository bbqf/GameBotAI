# Contract: execution log detailAttributes for WaitForImage

The execution log endpoints return `detailAttributes` for a WaitForImage step. This feature adds one optional field.

## Response fragment

```json
{
  "detailAttributes": {
    "timeoutMs": 2300,
    "effectiveTimeoutMs": 2300,
    "referenceImageId": "pns-showdown-tile",
    "confidence": 0.8,
    "exitCondition": "image_detected",
    "imageLoadStatus": "loaded",
    "matchedImageId": "pns-city-anchor"
  }
}
```

## Rules

- `matchedImageId` is optional. The service omits it when no image matched.
- `referenceImageId` shows the resolved id (the first id for a list). It never shows a `{{name}}` placeholder for a step that ran.
- Existing clients that ignore unknown fields are not affected. This is not a breaking change.

## Sequence step input

No change. `detectionTarget.referenceImageId` accepts a whole placeholder of a single-value or a multi-value image parameter, as the published schema says.

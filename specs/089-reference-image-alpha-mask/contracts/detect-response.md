# Contract: `POST /api/images/detect` response

**Feature**: 089-reference-image-alpha-mask
**Change type**: additive only — no existing field changes name, type, shape, or meaning.

## Request

Unchanged. No new field. Masking is a property of the reference image, never of the caller
(FR-007).

## Response (200)

```jsonc
{
  "matches": [
    {
      "templateId": "pns-collect-steel",
      "score": 0.9312,
      "confidence": 0.9312,
      "x": 0.1398,
      "y": 0.2068,
      "width": 0.0389,
      "height": 0.0271,
      "overlap": 0.3,
      "bbox": { "x": 0.1398, "y": 0.2068, "width": 0.0389, "height": 0.0271 }
    }
  ],
  "limitsHit": false,

  // NEW
  "masked": true,
  "retainedPixelCount": 1421
}
```

| Field | Type | Meaning |
|---|---|---|
| `masked` | bool | whether this detection compared only the retained pixels of a transparency-masked reference image |
| `retainedPixelCount` | int | number of pixels the comparison actually used; `0` when `masked` is false |

> **Naming**: the field counts the pixels **kept**, not the pixels masked out, and is named
> `retainedPixelCount` to say so. It matches the domain member `RetainedPixelCount` exactly,
> so the same word means the same thing at every layer.

### Guarantees

- **G-1**: For a reference image with no alpha channel, or with an all-opaque alpha
  channel, `masked` is `false`, `retainedPixelCount` is `0`, and every `score` / `confidence`
  is bit-identical to the value returned before this feature existed (FR-004, FR-005).
- **G-2**: When `masked` is `true`, `score` is on the same 0..1 normalised-correlation scale
  as an unmasked score, restricted to the retained pixels — a threshold calibrated against
  unmasked scores keeps its meaning (FR-003a).
- **G-3**: `bbox` and `x`/`y`/`width`/`height` describe the **full template rectangle**, not
  the bounding box of the retained pixels (FR-010).
- **G-4**: An existing client that ignores unknown fields is unaffected.

### Error responses

Unchanged: `invalid_request` (400), `not_found` (404), the feature-085 screen-resolution
failures, and `emulator_unavailable` (503) all behave exactly as before.

## `POST /api/images/detect-all`

Honours masks (FR-007). Its response shape is **unchanged** — no `masked` field is added,
because a sweep covers many images at once and a single flag would be meaningless. Per-image
mask state is available from `/api/images/detect`.

## Logs

`/api/images/detect` adds `masked` and `retainedPixelCount` as structured fields on its
existing detection-result log event. No message format is repurposed.

# Data Model: Document detect vs detect-all coordinate units

No persisted data and no response shape changes. The entities below are the published response schemas whose
descriptions this feature adds.

## MatchResult (one match of `POST /api/images/detect`)

| Field | Type | Unit / meaning (to be published) |
|-------|------|----------------------------------|
| templateId | string | Reference image id requested; same identifier detect-all reports as `imageId` |
| matchedReferenceId | string | Unchanged (described by feature 097) |
| score, confidence | number | Unchanged (0..1 match confidence) |
| bbox | NormalizedRect | Repeats this match's top-level `x`/`y`/`width`/`height`, same normalised unit |
| x | number | Left edge as a fraction 0..1 of the capture frame width, from the frame's left |
| y | number | Top edge as a fraction 0..1 of the capture frame height, from the frame's top |
| width | number | Match width as a fraction 0..1 of the capture frame width |
| height | number | Match height as a fraction 0..1 of the capture frame height |
| overlap | number | Unchanged |

Values are clamped to 0..1. Pixels = fraction × capture width (x, width) or height (y, height). Not pixels — detect-all
reports the same box in pixels.

## NormalizedRect (`MatchResult.bbox`)

`x`, `y`, `width`, `height`: same descriptions as the `MatchResult` coordinate fields.

## DetectAllMatch (one match of `POST /api/images/detect-all`)

| Field | Type | Unit / meaning (to be published) |
|-------|------|----------------------------------|
| imageId | string | Reference image id; same identifier detect reports as `templateId` |
| imageName | string | Unchanged (not described by this feature) |
| x | integer | Left edge in pixels of the capture frame, from the frame's left |
| y | integer | Top edge in pixels of the capture frame, from the frame's top |
| width | integer | Match width in pixels |
| height | integer | Match height in pixels |
| confidence | number | Unchanged |

Not fractions — detect reports the same box as fractions of the frame.

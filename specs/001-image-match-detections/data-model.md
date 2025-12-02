# Data Model: Image Match Detections

Date: 2025-12-02

## Request

- `referenceImageId` (string): Existing stored image ID.
- `threshold` (number [0..1], default 0.8): Minimum confidence to include.
- `maxResults` (int, default 10, 1..100): Maximum matches to return.
- `overlap` (number [0..1], default 0.3): IoU threshold for NMS suppression.

## Response

- `matches` (array of `MatchResult`)
- `limitsHit` (boolean, optional): True if truncated by `maxResults` or timeout.

### MatchResult

- `bbox` (object): Normalized rectangle relative to screenshot
  - `x` (number [0..1])
  - `y` (number [0..1])
  - `width` (number [0..1])
  - `height` (number [0..1])
- `confidence` (number [0..1])

## Errors

- `invalid_request`: Missing fields or invalid ranges.
- `not_found`: Unknown `referenceImageId`.
- `timeout`: Operation exceeded configured limit (200 OK with `limitsHit=true` preferred; TBD during implementation).

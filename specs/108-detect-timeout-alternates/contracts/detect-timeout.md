# Contract change: `POST /api/images/detect`

## New failure response

When the match does not complete in the time limit of the call:

```http
HTTP/1.1 504 Gateway Timeout
Content-Type: application/json

{
  "code": "detection_timeout",
  "message": "The detection did not complete in its time limit, so the service did not measure the screen. Send the request again."
}
```

The body has no `matches`, `limitsHit`, `masked` or `retainedPixelCount` fields.

## Time limit of a call

`Service:Detections:TimeoutMs` (default 500) for each reference. The service scores the named image and each alternate that loaded. Thus the limit is `max(1, TimeoutMs) × (1 + loaded alternates)`.

## Meaning of `limitsHit` in a `200`

`limitsHit: true` means only that `maxResults` cut the list of matches. A `200` with an empty `matches` array is a real absence.

## Unchanged

- The `200` response shape and all its fields.
- `400`, `404`, `409` and `503` responses.
- `POST /api/images/detect-all`.

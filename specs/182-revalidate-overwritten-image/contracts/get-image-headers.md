# Contract: GET /api/images/{id} cache headers

## Request

`GET /api/images/{id}` with optional `If-None-Match` or `If-Modified-Since`.

## Responses

| Status | When | Headers |
|--------|------|---------|
| 200 | Image found, no precondition or precondition not met | `Content-Type`, `Last-Modified`, `ETag`, `Cache-Control: no-cache`, `Accept-Ranges: bytes` |
| 304 | `If-None-Match` matches the ETag, or `If-Modified-Since` is not older than `Last-Modified` | `ETag`, `Last-Modified`, `Cache-Control: no-cache`; no body |
| 404 | Image not found | Unchanged (no new headers) |

## Rules

- The ETag is strong (no `W/` prefix). It changes when the stored bytes change.
- `If-None-Match` has priority over `If-Modified-Since`.
- After `PUT /api/images/{id}` with new bytes, a `GET` with the old ETag in `If-None-Match` returns 200 with the new bytes and a new ETag.
- The `PUT` request and response do not change.

# Contract: coordinate units in the published OpenAPI document

Document: `GET /swagger/v1/swagger.json`. Schemas are located by following each operation's `200` response
`application/json` schema → `matches.items` (`$ref`).

## `POST /api/images/detect`

| Location | Must contain (case-sensitive substrings) | FR |
|----------|------------------------------------------|----|
| operation `description` | `fraction`, `pixels`, `detect-all` | FR-004 |
| match schema `x`, `width` `description` | `fraction`, `width`, `not pixels`, `clamped` (and `left` on `x`) | FR-001, edge case |
| match schema `y`, `height` `description` | `fraction`, `height`, `not pixels`, `clamped` (and `top` on `y`) | FR-001, edge case |
| `NormalizedRect` `x`/`y`/`width`/`height` `description` | same as the match field of the same name | FR-002 |
| match schema `bbox` `description` (on the property or its `allOf` wrapper) | `x/y/width/height` | FR-002 |
| match schema `templateId` `description` | `imageId`, `detect-all` | FR-006 |
| 200 example `matches[0]` `x`, `y`, `width`, `height` and `bbox.*` | numbers in 0..1 | FR-007 |

The existing detect description text (features 085, 089, 097) is kept; the units sentence is appended.

## `POST /api/images/detect-all`

| Location | Must contain | FR |
|----------|--------------|----|
| operation `description` | `pixels`, `fraction`, `/api/images/detect` | FR-005 |
| match schema `x`, `y`, `width`, `height` `description` | `pixels`, `not fractions` (and `left` on `x`, `top` on `y`) | FR-003 |
| match schema `imageId` `description` | `templateId`, `/api/images/detect` | FR-006 |
| 200 example `matches[0]` `x`, `y`, `width`, `height` | integers | FR-007 |

## Unchanged (FR-008)

Routes, request/response JSON bodies, field names, field types, required/nullable flags and status codes. The existing
image detection contract tests must pass unchanged.

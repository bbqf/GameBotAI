# Contract: reference-image upload validation

**Feature**: 089-reference-image-alpha-mask
**Applies to**: `POST /api/images`, `PUT /api/images/{id}`
**Change type**: one new rejection case for a class of image that was previously accepted
and then failed silently at detection time.

## New rule

An uploaded image that **has an alpha channel** and whose retained region (pixels with
`alpha >= 128`) contains **fewer than 16 pixels** is rejected.

Images with no alpha channel are not inspected for this rule at all, so the existing upload
path is untouched (FR-008).

### Rationale

A mask leaving a handful of pixels correlates strongly with almost any patch of screen. Such
a template would pass its gate constantly, which is worse than not matching. Refusing it at
authoring time is the only point where the operator can act on it; refusing it at detection
time would reproduce the silent failure this feature exists to remove.

## Response

```http
HTTP/1.1 400 Bad Request
Content-Type: application/json
```

```json
{
  "error": {
    "code": "invalid_image",
    "message": "Reference image mask retains too few pixels to match reliably",
    "hint": "The transparency mask leaves 3 opaque pixels; at least 16 are required. Erase less of the image, or upload it without transparency."
  }
}
```

Reuses the existing `invalid_image` error envelope and code — no new error code, consistent
with the endpoint's established shape (constitution III).

## Accepted, unchanged

| Upload | Outcome |
|---|---|
| PNG with no alpha channel | accepted, as today, unexamined by this rule |
| PNG with an all-opaque alpha channel | accepted; matches identically to the same image without alpha (FR-005) |
| PNG with alpha retaining >= 16 pixels | accepted; matched on the retained pixels |
| PNG with alpha retaining 1–15 pixels | **rejected**, 400 `invalid_image` |
| PNG fully transparent (0 retained) | **rejected**, 400 `invalid_image` |
| JPEG and other alpha-less formats | accepted, as today |

Size, content-type, and id validation are unchanged and are evaluated before this rule.

## Round-trip guarantee

An accepted image's transparency is preserved byte-for-byte: `GET /api/images/{id}` returns
the stored bytes, and the detection paths decode them without flattening alpha against a
background colour (FR-006).

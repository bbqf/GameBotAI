# Contract: Unknown field error

Applies to `POST /api/commands` and `PATCH /api/commands/{id}`. No other endpoint changes.
The OpenAPI document does not change.

## Request (example)

```json
{
  "name": "Tap test",
  "steps": [
    { "type": "PrimitiveTap", "order": 0,
      "primitiveTap": { "detectionTarget": { "referenceImageId": "img1" }, "bogusField": 1 } }
  ]
}
```

## Response

Status `400`. Body uses the existing error shape:

```json
{ "error": { "code": "invalid_request",
             "message": "Unknown field(s): steps[0].primitiveTap.bogusField",
             "hint": "Remove the field or check its name in the OpenAPI schema." } }
```

- `hint` is always a non-null text.
- The root alias `detectionTarget` on the create body is accepted and not listed.
- PATCH for a missing command gives 404 before the scan.

- `message` starts with `Unknown field(s): ` and then lists every unknown path, separated by `, `.
- Each path starts at the body root, uses `.` between names and `[i]` for an array index.
- The path keeps the letter case that the client sent.

## Guarantees

- No command is created (POST) and no stored command changes (PATCH) when the answer is 400.
- A valid request gets the same status and body as before.
- Field names that differ from the schema only in letter case are accepted.

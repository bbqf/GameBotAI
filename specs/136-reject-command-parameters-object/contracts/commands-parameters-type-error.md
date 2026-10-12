# Contract: parameters type error

Applies to `POST /api/commands` and `PATCH /api/commands/{id}`.

## Request

`parameters` has a JSON type that is not a list of objects.

```json
{ "name": "x", "steps": [ ], "parameters": { "name": "p", "type": "text", "required": true } }
```

## Response

HTTP 400.

```json
{
  "error": {
    "code": "invalid_request",
    "message": "parameters must be a list of parameter declarations",
    "hint": "Send an array, even for one declaration."
  }
}
```

## Rules

- The message contains `parameters`.
- No command is created or changed.
- PATCH answers before the stored-command lookup, so an unknown id with this body also gives 400.
- Requests with `parameters` absent, `null`, or a valid list keep their current answers.
- The OpenAPI schema does not change.

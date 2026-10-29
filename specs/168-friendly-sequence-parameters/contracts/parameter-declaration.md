# Contract: Parameter declaration (friendly fields)

This contract extends the `parameters` member of commands and sequences (feature 078). It applies to
`GET`, `POST`, and `PUT` on `/api/commands` and `/api/sequences`, and to the declaration lists in the
`parameter-scope` responses. All new members are optional. A request without them is valid.

## Declaration object

```json
{
  "name": "difficulty",
  "type": "number",
  "default": "1",
  "required": false,
  "description": "Old text. Still valid.",
  "label": "Difficulty",
  "helpText": "Pick how hard the run is.",
  "kind": "choice",
  "options": [
    { "label": "Easy", "value": "1" },
    { "label": "Hard mode", "value": "3" }
  ]
}
```

```json
{
  "name": "port",
  "type": "number",
  "label": "Emulator number",
  "kind": "number",
  "min": 1,
  "max": 10,
  "unit": "no.",
  "pattern": "emulator-{value}"
}
```

```json
{
  "name": "targetImage",
  "type": "text",
  "label": "Button to tap",
  "kind": "image",
  "allowedImages": ["btn-start", "btn-claim", "btn-close"]
}
```

## Members

| Member | Type | Notes |
|--------|------|-------|
| `label` | string | 1 to 80 characters |
| `helpText` | string | up to 300 characters |
| `kind` | `"text"`, `"number"`, `"choice"`, `"image"` | empty: derived from `type` |
| `options` | array of `{ label, value }` | `kind` = choice only; 1 to 50 items |
| `allowedImages` | array of strings | `kind` = image only; 1 to 50 image IDs, no duplicates |
| `min`, `max` | integer | `kind` = number only |
| `unit` | string | `kind` = number only; up to 20 characters |
| `pattern` | string | `kind` = number only; one `{value}` marker |

## Binding object (unchanged)

```json
{ "name": "difficulty", "value": "3" }
```

The value is the technical value (the option `value`, the image ID, or the whole number).

## Errors

The service returns the standard parameter error body (`error`, `message`, `details[]`). New `code` values:

| Code | When |
|------|------|
| `invalid_parameter_declaration` | (existing code) a member does not fit the `kind`; wrong option count; duplicate option label or value; bad `pattern`; `min` above `max` |
| `invalid_parameter_default` | (existing code) the default does not fit the kind |
| `text_parameter_in_numeric_field` | (new) a text-typed parameter (a text choice, or a number with a pattern) is used in a numeric field of a command step |

Each message names the parameter and the problem in plain words, and gives one way to fix it.

## Warnings

| Code | When |
|------|------|
| `unknown_allowed_image` | (new) an ID in `allowedImages` has no stored image now |
| `invalid_parameter_value` | (new) a binding value of a template entry, a sequence step, or a command step is not a whole number, is out of range, is not an option value, is not in `allowedImages`, or is not a stored image. The save works. A value that is a `{{name}}` placeholder is not checked. |

## Execution log

A resolved parameter entry in the execution log gets an optional `label` member.

```json
{ "name": "difficulty", "value": "3", "origin": "entry", "label": "Hard mode" }
```

## Compatibility

- Old clients that send no new member get the same behavior as before.
- Old clients that read a response ignore the new members.
- The OpenAPI document (Swagger) describes every new member (FR-023).

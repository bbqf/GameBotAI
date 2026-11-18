# Contract: TextInput Command Step

**Feature**: 192-text-input-step | **Date**: 2026-11-18

## 1. Command step (in `POST /api/commands`, `PATCH /api/commands/{id}`, `GET /api/commands[/{id}]`, `POST /api/steps/execute`)

```json
{
  "type": "TextInput",
  "order": 2,
  "textInput": { "text": "250" }
}
```

With a parameter:

```json
{
  "type": "TextInput",
  "order": 2,
  "textInput": { "text": "" },
  "fieldTemplates": { "textInput.text": "{{fighterBatch}}" }
}
```

With an inline placeholder (text around a placeholder is permitted inline):

```json
{ "type": "TextInput", "order": 2, "textInput": { "text": "{{fighterBatch}}" } }
```

Rules:

- `textInput` is required for a `TextInput` step.
- `textInput.text` is a string. It is required. It can be empty only when `fieldTemplates` has the key `textInput.text`.
- A literal `textInput.text` (no placeholder) must have 1 to 500 characters, each from U+0020 to U+007E.
- The value of the `fieldTemplates` key `textInput.text` must be one whole placeholder, for example `{{fighterBatch}}`. When the key is present, its value replaces the inline text at run time.

Save errors (HTTP 400):

| Case | Body |
|------|------|
| No `textInput` | `{ "error": { "code": "invalid_request", "message": "textInput.text is required for TextInput steps" } }` |
| Literal text empty | `{ "error": { "code": "invalid_request", "message": "textInput.text is required for TextInput steps" } }` |
| Literal text too long | `{ "error": { "code": "invalid_request", "message": "textInput.text must have 500 characters or fewer" } }` |
| Literal text with a character that is not permitted | `{ "error": { "code": "invalid_request", "message": "textInput.text can have only the printable ASCII characters from space to ~" } }` |
| Overlay value not one whole placeholder | parameter error body, code `invalid_field_template_value`, field path `textInput.text` |
| Placeholder of a name that the command does not declare | parameter error body, code `unresolvable_parameter_reference`, field path `textInput.text` |

## 2. `dryRun` on the command requests

Request: `POST /api/commands` or `PATCH /api/commands/{id}` with `"dryRun": true` in the body.

- All checks pass: HTTP 200 with `{ "valid": true, "dryRun": true, "errors": [] }`. The service stores nothing.
- A check fails: the same HTTP 400 (or 404, 409) as the same request without `dryRun`.
- `"dryRun": false` or no `dryRun`: the current behaviour.

## 3. Step result (in `stepOutcomes` of the command run routes and the steps route)

```json
{
  "stepOrder": 2,
  "status": "executed",
  "stepType": "text-input",
  "reason": null,
  "typedLength": 3
}
```

| `status` | `reason` | `typedLength` |
|----------|----------|---------------|
| `executed` | null | text length |
| `not_accepted` | `text_input_empty_text` | 0 |
| `not_accepted` | `text_input_too_long` | 0 |
| `not_accepted` | `text_input_unsupported_character` | 0 |
| `not_accepted` | `text_input_not_accepted` | characters typed before the failure |
| `skipped_invalid_config` | `text_input_missing_config` | absent |

`typedLength` is absent for other step types.

## 4. Execution log

A `TextInput` step adds a detail item: kind `text-input`, text `Text input typed 3 characters.`, attribute `typedLength`. The log keeps the resolved parameters (name, value, origin layer) as for other steps. The log does not hold the typed text in the detail item.

## 5. OpenAPI

- `CommandStepTypeDto` lists `TextInput`.
- `TextInputConfigDto.text` has a description: the text to type, the permitted characters (U+0020 to U+007E), the maximum length (500), the inline placeholder, the `fieldTemplates` key, and the reason codes.
- The `fieldTemplates` description names the key `textInput.text`.
- `StepExecutionOutcomeDto.typedLength` has a description.
- `CreateCommandRequest.dryRun` and `UpdateCommandRequest.dryRun` have a description.

## 6. Device command

For each chunk: `adb -s <serial> shell "input text '<chunk>'"` (with `'` changed to `'\''`). For each space: `adb -s <serial> shell input keyevent 62`.

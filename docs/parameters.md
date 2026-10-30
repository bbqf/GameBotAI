# Parameters for automation authors

This guide shows how to declare a parameter, use it in a step, and give it a value. It covers the plain
parameters (features 078 and 115) and the typed parameters (feature 168): image, choice, and number.
The field list of each request is in the Swagger (`/swagger`). This guide shows how the parts fit together.

Last reviewed: 2026-10-30

## How it works

1. A **command** or a **sequence** declares a parameter (`parameters`).
2. A step uses the parameter as a placeholder `{{name}}`.
3. A value comes from a **binding**: a queue-template entry (`parameterValues`), or the step of a
   sequence that calls a command (`parameterBindings`).
4. A person fills the value in the web UI. The sequence gets the **technical value**. A binding always stores
   the technical value, never a label.

A name must match `^[A-Za-z_]\w*$`. A value is found in this order: the innermost binding first, then the
default of the innermost declaration. When no layer gives a value, the step does not run (reason
`skipped_parameter_unresolved`).

## Declare a parameter

All members except `name` are optional. A declaration with only `name` and `type` works as before.

| Member | Use |
|---|---|
| `name`, `type` (`text` or `number`), `default`, `required`, `description` | Plain parameter |
| `label`, `helpText` | Readable text for the person (label up to 80 characters, help up to 300) |
| `kind` | `text`, `number`, `choice`, or `image`. When empty, the kind follows from `type` |
| `options` | For `choice`: 1 to 50 items of `label` and `value`. Each label is unique |
| `allowedImages` | For `image`: 1 to 50 image IDs. The person can select only these images |
| `min`, `max`, `unit` | For `number`: the range and a unit text (up to 20 characters) |
| `pattern` | For `number`: technical text with one `{value}` marker |

Example of three typed parameters in a sequence:

```json
"parameters": [
  { "name": "buttonToTap", "label": "Button to tap", "type": "text", "kind": "image",
    "allowedImages": ["pns-alliance-tech-donate-exhausted", "pns-alliance-tech-donate-button"] },
  { "name": "difficulty", "label": "Difficulty", "type": "number", "kind": "choice",
    "options": [ { "label": "Easy", "value": "1" }, { "label": "Hard mode", "value": "3" } ] },
  { "name": "emulatorNumber", "label": "Emulator number", "type": "number", "kind": "number",
    "min": 1, "max": 10, "pattern": "emulator-{value}" }
]
```

- **Image:** the person selects a thumbnail. The value is the image ID.
- **Choice:** the person selects a label. The value is the `value` of the option. A choice value is plain
  text, so `{{` in it is not resolved.
- **Number with pattern:** the person enters a number. The run puts it in `{value}`. Entering 5 gives
  `emulator-5`. The result is text, so you cannot use it in a numeric field.

## Use a parameter in a step

- **Text field:** write `{{name}}` in the field, alone or with other text.
- **Numeric field or image field of a command step:** the field cannot hold text, so the step has
  `fieldTemplates`. The key is the field path and the value is the placeholder:

  ```json
  { "type": "Swipe", "order": 1,
    "swipe": { "startX": 100, "startY": 400, "endX": 100, "endY": 200 },
    "fieldTemplates": { "swipe.durationMs": "{{difficulty}}" } }
  ```

  ```json
  { "type": "PrimitiveTap", "order": 0,
    "primitiveTap": { "detectionTarget": { "referenceImageId": "any-image-id" } },
    "fieldTemplates": { "primitiveTap.detectionTarget.referenceImageId": "{{buttonToTap}}" } }
  ```

  An image key must hold one whole placeholder. The supported keys are in `CommandStepFieldPaths`
  (`swipe.startX`, `swipe.startY`, `swipe.endX`, `swipe.endY`, `swipe.durationMs`, `waitForImage.timeoutMs`,
  `primitiveTap.detectionTarget.confidence`, `.offsetX`, `.offsetY`, `ensureGameRunning.*`,
  `ensureEmulatorRunning.instanceIndex`, and the two `referenceImageId` keys). The save refuses an unknown
  key. The web UI cannot set `fieldTemplates` yet ([issue 358](https://github.com/bbqf/GameBotAI/issues/358)),
  so use the API.

## Give a value

**Sequence step that calls a command:** `parameterBindings` on the step (a step of type `Action` with a
`primitiveAction` of type `command`). A value can be a literal, text with placeholders, or one whole
placeholder of a sequence parameter (pass-through). A row with no value inherits.

```json
"parameterBindings": [
  { "name": "buttonToTap", "value": "{{buttonToTap}}" },
  { "name": "difficulty", "value": "{{difficulty}}" }
]
```

**Queue-template entry:** `parameterValues` on the entry. Each item has `name` and the technical `value`.

```json
"parameterValues": [ { "name": "difficulty", "value": "3" } ]
```

Give the sequence parameter a `default`, or set a value on the entry. A row with neither fails at run time.

## Warnings and errors

| Code | Kind | Meaning |
|---|---|---|
| `invalid_parameter_value` | Warning | A saved value does not fit its declaration (for example 11 when `max` is 10, or an option that was removed). It never stops a queue start or a run |
| `unknown_allowed_image` | Warning | An allowed image ID has no stored image |
| `text_parameter_in_numeric_field` | Error | A text parameter that is friendly (pattern, image, or text choice) is in a numeric field |
| `invalid_parameter_declaration` | Error | A bad member, for example a duplicate option label or `kind` `choice` with no `options`. The message has a hint |

Warnings are in the `warnings` member of the response of a save.

## Execution log

The log shows each value that a step used, in the info text of the step: `Parameters: difficulty = 3 (Hard
mode)`. A choice shows its label after the value. An image and a number show the technical value. A name
that contains `token`, `password`, `secret`, `apikey`, `api_key`, or `authorization` is masked.

## Where to find more

- The Swagger shows the schemas `ParameterDeclarationDto`, `ParameterOptionDto`, `ParameterBindingDto`, and
  `ParameterWarningDto`, with a description for each member.
- `specs/168-friendly-sequence-parameters/demo-data.ps1` makes a working command and sequence through the API.
- `docs/architecture.md` has the design notes. `specs/078-*` and `specs/168-friendly-sequence-parameters/`
  have the full rules.

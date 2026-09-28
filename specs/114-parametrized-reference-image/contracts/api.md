# Contract: parametrized reference image (feature 114)

All changes are additive. A request that the service accepted before this feature gets the same result, except the message text of `unknown_field_template_path`.

## 1. `POST /api/commands` and `PATCH /api/commands/{id}`: image keys in `fieldTemplates`

Request:

```json
{
  "name": "tap-selected-option",
  "parameters": [ { "name": "novaOption", "type": "text", "required": true } ],
  "steps": [
    {
      "type": "PrimitiveTap",
      "order": 0,
      "primitiveTap": { "detectionTarget": { "referenceImageId": "option-a", "confidence": 0.85 } },
      "fieldTemplates": { "primitiveTap.detectionTarget.referenceImageId": "{{novaOption}}" }
    },
    {
      "type": "WaitForImage",
      "order": 1,
      "waitForImage": { "detectionTarget": { "referenceImageId": "option-a" }, "timeoutMs": 3000 },
      "fieldTemplates": { "waitForImage.detectionTarget.referenceImageId": "{{novaOption}}" }
    }
  ]
}
```

Response: `201 Created`. The body is the current command response. `fieldTemplates` comes back as sent. `warnings` holds one `static_check_skipped` item for each image key:

```json
{
  "warnings": [
    {
      "code": "static_check_skipped",
      "message": "Step '0': 'primitiveTap.detectionTarget.referenceImageId' is parametrized, so its target is checked at run time instead of now.",
      "fieldPath": "primitiveTap.detectionTarget.referenceImageId",
      "parameterName": "novaOption"
    }
  ]
}
```

(The warning text is the current text of feature 078. This feature does not change it.)

### 1a. Value that is not one whole placeholder

`"fieldTemplates": { "primitiveTap.detectionTarget.referenceImageId": "nova-{{option}}" }` or `"option-a"`:

`400 Bad Request`

```json
{
  "error": "invalid_field_template_value",
  "message": "Step 0: the value of 'primitiveTap.detectionTarget.referenceImageId' must be one whole placeholder, for example {{name}}.",
  "details": [ { "code": "invalid_field_template_value", "message": "…", "fieldPath": "primitiveTap.detectionTarget.referenceImageId", "parameterName": null } ]
}
```

### 1b. Key that is not supported

`"fieldTemplates": { "ensureGameRunning.readinessImage.referenceImageId": "{{x}}" }`:

`400 Bad Request`, `error: "unknown_field_template_path"`, message `Step 0: 'ensureGameRunning.readinessImage.referenceImageId' is not a parametrizable field.`

### 1c. Name that is not declared

`"fieldTemplates": { "primitiveTap.detectionTarget.referenceImageId": "{{undeclared}}" }` with no declaration `undeclared`: `400`, `error: "unresolvable_parameter_reference"` (current rule).

### 1d. Numeric keys

No change (FR-013).

## 2. Sequence writes: placeholder in `imageVisible.imageId`

Routes: `POST /api/sequences`, `PUT /api/sequences/{sequenceId}`, `PATCH /api/sequences/{sequenceId}`.

Request (per-step body):

```json
{
  "name": "daily-option",
  "parameters": [ { "name": "novaOption", "type": "text", "required": true } ],
  "steps": [
    {
      "stepId": "tap-option",
      "primitiveAction": { "type": "tap", "schemaVersion": "v1", "payload": { "x": 50, "y": 50 } },
      "condition": { "type": "imageVisible", "imageId": "{{novaOption}}" }
    }
  ]
}
```

Response: `201 Created` (or `200 OK` for `PUT` and `PATCH`). The body is the current sequence response plus a new optional member `warnings`:

```json
{
  "warnings": [
    {
      "code": "static_check_skipped",
      "message": "Step 'tap-option': 'condition.imageId' is parametrized, so its target is checked at run time instead of now.",
      "fieldPath": "condition.imageId",
      "parameterName": "novaOption"
    }
  ]
}
```

`warnings` is not present when there are no warnings.

A dry run (`"dryRun": true` in the request body) does not change: the response is `200 OK` with `{ "valid": true, "dryRun": true, "errors": [] }` and has no `warnings` member. Only a save that stores the sequence returns `warnings`.

The same rule applies to these field paths: `if.condition.imageId`, `loop.condition.imageId` (while and repeat-until), `breakCondition.imageId`, and each composite child, for example `condition.children[1].imageId`.

### 2a. Name that is not declared

The same body without the `novaOption` declaration: `400 Bad Request`, `error: "unresolvable_parameter_reference"`, `fieldPath: "condition.imageId"`.

### 2b. Literal image id

A literal `imageId` in a step condition keeps the current existence check ("Image reference '<id>' does not exist (used by: …)").

## 3. Run time

- A tap or wait step with the image key uses the resolved value as the image id. The execution-log step detail has the item `parameters` with `novaOption = <value>` and its scope layer (current feature 078 format). The step detail of a `WaitForImage` step also has `referenceImageId` with the resolved id. A `PrimitiveTap` step detail does not always name the image id (for example, the `skipped_invalid_config` outcome has no `referenceImageId`). For a tap step, the `parameters` item holds the value.
- An `imageVisible` leaf with a placeholder uses the resolved id. The scope of each position: a step guard and an `If` condition use the scope of the step; a while condition uses the scope of the iteration that is about to run (`{{iteration}}` is 1 before the first iteration); a repeat-until condition uses the scope of the iteration that just ran; a break condition uses the scope of the current iteration. The log text of the condition shows the resolved id, for example `imageVisible(imageId=option-b, minSimilarity=default)`.
- A name with no value in scope:
  - command step: outcome `skipped_parameter_unresolved` with the message `Step '<order>': parameter 'novaOption' used by field 'primitiveTap.detectionTarget.referenceImageId' could not be resolved from any scope.` No input goes to the device.
  - step guard (of an action step or a loop step): the step fails, and the sequence stops. The message is exactly `Step '<stepKey>': parameter 'novaOption' used by field 'condition.imageId' could not be resolved from any scope.`
  - while, repeat-until or `If` condition: the step fails, and the sequence stops. The message keeps the current prefix of the position, then the resolution message:
    - while: `Loop '<key>' condition evaluation failed: Step '<key>': parameter 'novaOption' used by field 'loop.condition.imageId' could not be resolved from any scope.`
    - repeat-until: `Loop '<key>' exit condition evaluation failed: Step '<key>': parameter 'novaOption' used by field 'loop.condition.imageId' could not be resolved from any scope.`
    - `If`: `If '<key>' condition evaluation failed: Step '<key>': parameter 'novaOption' used by field 'if.condition.imageId' could not be resolved from any scope.`
  - break condition: "No break" with the same message as the error detail (feature 066 FR-002a and FR-010).
- An id with no image: the current missing-image result of each step type or position, the same as for a literal id. No device input occurs in each case.
  - `WaitForImage` step: fails with `image_unavailable`.
  - `PrimitiveTap` step: the current `skipped_invalid_config` result, with `template_not_found` on Windows and `primitive_tap_detection_windows_only` on other hosts.
  - step guard, `If` condition, while or repeat-until condition: the `imageVisible` leaf fails with `image_unavailable`.
  - break condition: "No break" with the error detail in the log, and the run does not fail (feature 066 FR-002a and FR-010).

## 4. `POST /api/queue-templates`: check of known image values

Request:

```json
{
  "name": "four-accounts",
  "overwrite": true,
  "entries": [
    { "sequenceId": "<daily-option id>", "scheduleType": "OncePerRun", "parameterValues": [ { "name": "novaOption", "value": "no-such-image" } ] }
  ]
}
```

Response: `400 Bad Request`. The service saves nothing.

```json
{
  "error": "unknown_image_reference",
  "message": "Entry 0: parameter 'novaOption' gives the image id 'no-such-image' to field 'condition.imageId', but no image has that id.",
  "details": [
    {
      "code": "unknown_image_reference",
      "message": "Entry 0: parameter 'novaOption' gives the image id 'no-such-image' to field 'condition.imageId', but no image has that id.",
      "fieldPath": "condition.imageId",
      "parameterName": "novaOption"
    }
  ]
}
```

Rules:
- The check looks at each image field that the reference scanner marks `DefeatsStaticCheck` in the sequence and in each command that the sequence can reach: `imageVisible.imageId` in each condition position, the inline image fields of command steps (`primitiveTap.detectionTarget.referenceImageId`, `waitForImage.detectionTarget.referenceImageId`, and an inline `ensureGameRunning.readinessImage.referenceImageId` placeholder), the two image keys of `fieldTemplates`, and the inline `detection.referenceImageId` of a command.
- The check finds the known value of a name for each call path: sequence step, then command, then each nested command step. A field in the sequence has the path "sequence" only.
  1. Binding rule: at each call site on the path (the sequence step and each nested command step above the field), a non-null `parameterBindings` entry for the name covers it. The path then gives no value to check for that name. The binding outranks the entry at run time. A literal binding is not checked. A binding to `{{otherName}}` is not followed. This is a known limit; the run-time check applies (section 3). Example: an entry supplies `novaOption = no-such-image` and the step binds the `novaOption` of the command to `option-b`: the save does not reject the entry value for the image fields of that command.
  2. Else, if the entry supplies the name, the check uses the entry value.
  3. Else, the check uses the default of the innermost declaration layer outward along the path: the nested command, then the command that calls it, and so on, and then the sequence.
- A field gets one check for each path that reaches it. Duplicate values are checked one time. Example: two sequence steps call the same command; one step binds `novaOption` and the other does not. The check uses the path of the step with no binding.
- A field whose text has a name with no known value (for example a queue built-in) is not checked.
- The check applies to each entry, also a disabled entry.
- An entry whose sequence does not exist is not checked.
- A value that names an image that exists: the save continues as today.

## 5. OpenAPI

- `CommandStepDto.fieldTemplates`: description lists the numeric keys, the two image keys, the whole-placeholder rule and the codes `unknown_field_template_path` and `invalid_field_template_value`.
- `ImageVisibleCondition.imageId` and `ImageVisibleConditionContract.imageId`: description tells that the field accepts a placeholder in each condition position, that the save skips the existence check with `static_check_skipped`, and that the run resolves the id.
- `SaveQueueTemplateRequest` / `TemplateEntrySaveRequest.parameterValues`: description tells the `unknown_image_reference` check.
- `specs/openapi.json` gets the same text.

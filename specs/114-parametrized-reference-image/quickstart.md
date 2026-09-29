# Quickstart: Let a parameter choose the reference image

This guide shows one sequence that serves many accounts. Each account taps a different option on the same screen. The template entry of each account selects the image of the option.

## 1. Upload the option images

Upload one reference image for each option, for example `option-a`, `option-b` and `option-c` (`POST /api/images`).

## 2. Make the command

Declare a text parameter. Put the image key into `fieldTemplates`. Keep a fallback id in the inline `referenceImageId`, because a `PrimitiveTap` step must have one.

```json
{
  "name": "tap-selected-option",
  "parameters": [ { "name": "novaOption", "type": "text", "required": true } ],
  "steps": [
    {
      "type": "PrimitiveTap",
      "order": 0,
      "primitiveTap": { "detectionTarget": { "referenceImageId": "option-a" } },
      "fieldTemplates": { "primitiveTap.detectionTarget.referenceImageId": "{{novaOption}}" }
    }
  ]
}
```

Send it with `POST /api/commands`. The response is `201` and has the warning `static_check_skipped`. The service checks the image at run time.

The value of an image key must be one whole placeholder. `"nova-{{option}}"` gets `400 invalid_field_template_value`. To build an id from text and a parameter, use the inline form: `"referenceImageId": "nova-{{option}}"`.

## 3. Make the sequence with a guard

The guard checks that the selected option is on the screen before the tap.

```json
{
  "name": "daily-option",
  "parameters": [ { "name": "novaOption", "type": "text", "required": true } ],
  "steps": [
    {
      "stepId": "tap-option",
      "primitiveAction": { "type": "command", "schemaVersion": "v1", "payload": { "commandId": "<tap-selected-option id>" } },
      "condition": { "type": "imageVisible", "imageId": "{{novaOption}}" }
    }
  ]
}
```

Send it with `POST /api/sequences`. The response is `201` and has `warnings` with `static_check_skipped` for `condition.imageId`. Without the `novaOption` declaration, the response is `400 unresolvable_parameter_reference`.

You can use the placeholder in each condition position: a step condition, an `If` condition, a while or repeat-until condition, a break condition, and a child of `all`, `any` or `none`.

## 4. Give each account its value

```json
{
  "name": "four-accounts",
  "overwrite": true,
  "entries": [
    { "sequenceId": "<daily-option id>", "parameterValues": [ { "name": "novaOption", "value": "option-a" } ] },
    { "sequenceId": "<daily-option id>", "parameterValues": [ { "name": "novaOption", "value": "option-b" } ] },
    { "sequenceId": "<daily-option id>", "parameterValues": [ { "name": "novaOption", "value": "option-c" } ] },
    { "sequenceId": "<daily-option id>", "parameterValues": [ { "name": "novaOption", "value": "option-b" } ] }
  ]
}
```

Send it with `POST /api/queue-templates`. Change one value to `no-such-image` and send it again. The response is `400 unknown_image_reference`, and the message names the entry, the parameter and the image id. The service does not save the template.

## 5. Run and read the log

Link a queue to the template and start it. For each entry:
- The guard looks for the image of the entry. When the image is not on the screen, the step is skipped.
- The tap looks for the same image. The execution-log step detail has the item `parameters` with `novaOption` and its value.

When an image is deleted after the save, the step gets the current missing-image result of its step type, the same as a literal id: the guard and a `waitForImage` step give `image_unavailable`, and the tap gets `skipped_invalid_config`. No device input occurs.

## 6. Check the change

```bash
dotnet build -c Debug
dotnet test tests/unit -c Debug --filter "FullyQualifiedName~Parameters|FullyQualifiedName~ConditionScope"
dotnet test tests/contract -c Debug --filter "FullyQualifiedName~ParametrizedReferenceImage|FullyQualifiedName~TemplateImageReference"
dotnet test tests/integration -c Debug --filter "FullyQualifiedName~ParametrizedReferenceImage"
```

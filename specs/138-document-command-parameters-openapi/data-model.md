# Data Model: Document the command "parameters" list in OpenAPI

This feature adds no stored data. It changes the shape of the generated OpenAPI document only.

## Command create request schema (`CommandCreateSchema`)

| Field | Type | Required | Note |
|-------|------|----------|------|
| name | string | yes | Existing |
| triggerId | string | no | Existing |
| steps | list of CommandStepDto | no | Existing |
| parameters | list of ParameterDeclarationDto | no | NEW. Absent means the command has no parameters. |

## Parameter declaration (`ParameterDeclarationDto`, existing)

| Field | Type | Required | Note |
|-------|------|----------|------|
| name | string | no (in schema) | The name used in `{{name}}` |
| type | string | no (in schema) | `text` or `number`; the description names both |
| default | string | no | Can be absent; not in the `required` list |
| required | boolean | no | |
| description | string | no | |

## fieldTemplates description (`CommandStepDto.fieldTemplates`)

Text extended with: each `{{placeholder}}` is declared in the top-level `parameters` list of the command, or is a queue built-in; a step-level `parameterBindings` does not satisfy the check; the error is `unresolvable_parameter_reference`.

## State transitions

None.

# Data Model: Document the command "parameters" list in OpenAPI

This feature adds no stored data. It changes the shape of the generated OpenAPI document only.

## Command create request schema (`CommandCreateSchema`)

| Field | Type | Required | Note |
|-------|------|----------|------|
| name | string | yes | Existing |
| triggerId | string | no | Existing |
| steps | list of CommandStepDto | no | Existing |
| parameters | list of ParameterDeclarationDto | no | NEW. If absent, the command has no parameters. |

## Parameter declaration (`ParameterDeclarationDto`, existing)

| Field | Type | Required | Note |
|-------|------|----------|------|
| name | string | no (in schema) | The name that you use in `{{name}}` |
| type | string | no (in schema) | The description names `text` and `number` |
| default | string | no | The value can be absent. It is not in the `required` list. |
| required | boolean | no | |
| description | string | no | |

## fieldTemplates description (`CommandStepDto.fieldTemplates`)

The description now gives these rules. Declare each `{{placeholder}}` in the top-level `parameters` list of the command, or use a queue built-in. A step-level `parameterBindings` does not satisfy the check. The error is `unresolvable_parameter_reference`.

## State transitions

There are no state transitions.

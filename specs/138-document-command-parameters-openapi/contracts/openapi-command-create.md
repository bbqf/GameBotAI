# Contract: OpenAPI document for command create

The contract is the generated document at `/swagger/v1/swagger.json`. The contract test checks these points.

1. `POST /api/commands` request body schema (`CommandCreateSchema`) has an optional property `parameters`. It is an array. Its items reference `ParameterDeclarationDto`. The property is not in `required`.
2. `ParameterDeclarationDto` has properties `name`, `type`, `default`, `required`, `description`. `default` is not in the `required` list of the item schema. The `type` property (enum or description) names `text` and `number`.
3. The schemas for command create, command update (`UpdateCommandRequest`) and command response (`CommandResponse`) each have a `parameters` property.
4. The description of `CommandStepDto.fieldTemplates` contains: `parameters`, `queue built-in`, `parameterBindings`, `unresolvable_parameter_reference`.
5. The create request example has a `parameters` list. Each `{{placeholder}}` in the example is declared in it.

No change to request or response behavior. No new endpoint.

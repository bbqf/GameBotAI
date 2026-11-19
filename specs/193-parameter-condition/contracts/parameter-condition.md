# Contract: `parameter` sequence condition (193)

## Request and response shape

The condition is permitted in each slot that takes a sequence step condition: `condition`, `if.condition`, `loop.condition`, `breakCondition`, and `children` of `all`, `any`, and `none`.

```json
{ "type": "parameter", "name": "fighterBatch", "operator": "equals", "value": 0 }
{ "type": "parameter", "name": "fighterTier", "operator": "equals", "value": "max", "negate": true }
{
  "type": "all",
  "children": [
    { "type": "parameter", "name": "fighterBatch", "operator": "greaterThan", "value": 0 },
    { "type": "imageVisible", "imageId": "camp-fighter" }
  ]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | `"parameter"` | yes | The discriminator. |
| `name` | string | yes | The name of a declared parameter of the sequence. Case-sensitive. No `{{ }}`. |
| `operator` | string | yes | `equals`, `notEquals`, `greaterThan`, or `lessThan`. |
| `value` | string or number | yes | The fixed value. A read returns the same JSON kind. |
| `negate` | boolean | no | When true, the result is inverted. |

A `GET` of the sequence returns the condition with the same fields and the same JSON kind of `value`.

## Compare rules

- A `number` parameter: the run value and `value` compare as whole numbers.
- A `text` parameter (input kinds `text`, `choice`, `image`): the run value and the text of `value` compare as text, ordinal, case-insensitive. A JSON number `value` compares as its text.
- A `number` parameter with a `pattern`: the compare uses the number that the person entered, before the pattern.

## Save and `dryRun` errors (HTTP 400)

The shape errors use the `$`-rooted path of the condition (the same as the other condition kinds). The declaration errors (with a code) use the slot field path (the same as the other parameter issues).

The sequence create (`POST /api/sequences`), update (`PUT /api/sequences/{id}` and `PATCH /api/sequences/{id}`), and their `"dryRun": true` forms give a 400 with the error list. Examples:

| Cause | Message (example) | Code (parameter issues) |
|-------|-------------------|-------------------------|
| Empty `name` | `Step 'train' condition at $: parameter condition requires name.` | — |
| Unknown `operator` | `Step 'train' condition at $: parameter operator must be one of equals, notEquals, greaterThan, lessThan.` | — |
| No `value` | `Step 'train' condition at $: parameter condition requires value.` | — |
| `value` is not a string or a number | `Step 'train' condition at $: parameter value must be a string or a number.` | — |
| Unknown `name` | `Step 'train' condition at if.condition.children[0]: parameter 'fighterBach' is not a parameter of this sequence.` | `unknown_condition_parameter` |
| `number` parameter and `value` not a whole number | `Step 'train' condition at if.condition: parameter 'fighterBatch' is a number parameter, and value 'abc' is not a whole number.` | `invalid_condition_value` |
| Multi-value parameter | `Step 'train' condition at condition: parameter 'tiers' holds a list, and a parameter condition needs one value.` | `list_in_single_field` |

## Run-time failures

| Cause | Step result |
|-------|-------------|
| No run value (no binding, no default) | The step fails with the parameter resolution failure (`ParameterUnresolved`). The message names the parameter and the field path. |
| A `number` parameter with a run value that is not a whole number | The step fails with `ParameterValueInvalid` (reason `not_a_number`). The message names the parameter. |

## Execution log

One line for each evaluation, for example:

```text
parameter(name=fighterBatch, operator=equals, value=0) actual=0 compare=number
```

The text of the composite child that decided the result: `parameter(name=fighterBatch, operator=equals, value=0)`, with the `NOT ` prefix when `negate` is true.

## OpenAPI

The document has the schema `ParameterCondition` in the condition `oneOf` list, with the discriminator value `parameter`, the fields above, an `enum` of the four operators, and `value` as string or integer. The schema description states the compare rules and the save rules.

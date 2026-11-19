# Data Model: Parameter Condition (193)

## ParameterStepCondition (domain, new)

File: `src/GameBot.Domain/Commands/SequenceStepCondition.cs`. JSON discriminator: `parameter`.

| Field | Type | Stored | Rules |
|-------|------|--------|-------|
| `type` | `"parameter"` | yes | The discriminator. |
| `name` | string | yes | Required. Not empty. The bare name of a declared parameter of the sequence. Case-sensitive. |
| `operator` | string | yes | Required. One of `equals`, `notEquals`, `greaterThan`, `lessThan`. Case-sensitive. |
| `value` | `ParameterConditionValue` | yes | Required. A JSON string or a JSON number. A read gives the same JSON kind. |
| `negate` | bool | yes | Optional. Default `false`. Applied after the compare. |
| `ResolvedValue` | string? | no (`[JsonIgnore]`) | Set by `SequenceStepConditionResolver` at run time. The run value of the parameter. |
| `CompareAsNumber` | bool | no (`[JsonIgnore]`) | Set by the resolver. True when the declaration has the type `number`. |

## ParameterConditionValue (domain, new)

File: `src/GameBot.Domain/Commands/ParameterConditionRules.cs`.

| Field | Type | Rules |
|-------|------|-------|
| `Text` | string | The text of the value. For a JSON number, the raw number text (for example `0`, `250`, `-5`). |
| `IsNumber` | bool | True when the author wrote a JSON number. |

A `JsonConverter` reads a JSON string or a JSON number and writes the same kind. For `IsNumber`, it writes the raw text as a JSON number. Other JSON kinds give a `JsonException` (the endpoint refuses them with a 400 before this point).

## ParameterConditionRules (domain, new)

File: `src/GameBot.Domain/Commands/ParameterConditionRules.cs`.

- `Operators`: the four operator names.
- `ValidateShape(condition)`: gives the shape problems (empty `name`, unknown `operator`, no `value`).
- `TryParseWhole(text, out long)`: trim, `long.TryParse` with `NumberStyles.AllowLeadingSign` and `CultureInfo.InvariantCulture`.
- `Compare(condition)`: needs `ResolvedValue`. Gives the result before `negate`.

## Compare table

| Declaration | Compare | Example |
|-------------|---------|---------|
| `type: number` (any kind: `number`, or `choice` of the type `number`; also with `pattern`) | whole number (`long`) | run value `10`, `greaterThan` `9` → true |
| `type: text`, kind `text` | ordinal, case-insensitive | run value `MAX`, `equals` `max` → true |
| `type: text`, kind `choice` (of the type `text`) | the technical option value, ordinal, case-insensitive | run value `t7`, `notEquals` `max` → true |
| `type: text`, kind `image` | the image id, ordinal, case-insensitive | |
| `multiple: true` | refused at save (`list_in_single_field`) | |

## Run-time states

| State | Result |
|-------|--------|
| Run value found and valid | `true` or `false` from the compare, then `negate`. |
| No value and no default | `ConditionEvaluationException` with `ParameterUnresolved`. The step fails. |
| `number` declaration and the run value is not a whole number | `ConditionEvaluationException` with `ParameterValueInvalid` (reason `not_a_number`). The step fails. |
| The run value came from a parent binding that a parent `pattern` changed | The text after that pattern is the run value. The rules above apply. |

## New validation codes

File: `src/GameBot.Domain/Services/ParameterValidationService.cs` (`ParameterValidationCodes`).

| Code | When |
|------|------|
| `unknown_condition_parameter` | `name` is not a declared parameter of the sequence. |
| `invalid_condition_value` | The declaration has the type `number` and `value` is not a whole number. |
| `list_in_single_field` (exists) | The declaration has `multiple: true`. |

## ParameterScope (domain, changed)

New method `TryResolveEntered(string name, out ParameterValue value)`: the same layers as `TryResolve`, but it does not apply the declaration `pattern`. A bound value that is already `Mapped` stays as it is.

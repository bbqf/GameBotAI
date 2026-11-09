# Contract: list-valued parameters

## ParameterDeclarationDto (new members)

```json
{
  "name": "quests",
  "kind": "image",
  "multiple": true,
  "minItems": 1,
  "maxItems": 3,
  "allowedImages": ["quest_a", "quest_b", "quest_c"]
}
```

- `multiple`: boolean. Optional. Valid for kind `image` and `choice`.
- `minItems`, `maxItems`: integer. Optional. Valid only with `multiple: true`.
- A response omits the three members when they are not set.

## ParameterBindingDto (new member)

```json
{ "name": "quests", "values": ["quest_b", "quest_a"] }
```

- `values`: array of text. Optional. Ordered. The system removes duplicates and keeps the first occurrence.
- A request may send `value` (text) for a multi-value parameter. The system reads it as a list of one.
- A response for a list binding returns `values` and `value` (items joined by `|`).
- If a request sends both, `values` wins.

## Errors

A save that breaks a declaration or list rule returns the standard error body (`error`, `message`, `details`). The codes are in data-model.md.

## Resolution rules for `{{name}}`

| Place of the placeholder | List of one item | List of 2+ items |
|--------------------------|------------------|------------------|
| Text field (text, pattern) | The item | Items joined by `|`, in order |
| Whole value of a one-value field (image id, number) | The item | The step fails with a message that names the parameter, the field, and the count |
| Binding to a multi-value callee parameter | Full list passes | Full list passes |
| Binding to a non-multi-value callee parameter | The item | Warning at save, step failure in a one-value field |

## Swagger

The published schema shows the new members on `ParameterDeclarationDto` and `ParameterBindingDto` (and the queue template entry DTOs that hold parameter values), with descriptions in STE.

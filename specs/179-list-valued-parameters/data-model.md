# Data Model: List-valued sequence parameters

## ParameterDeclaration (changed)

| Field | Type | Rule |
|-------|------|------|
| `multiple` | bool, default false | Valid only for kind `image` or `choice`. Omitted from output when false or null. |
| `minItems` | int, optional | Valid only when `multiple` is true. 1 or more. At most `maxItems`. At most the count of allowed items. |
| `maxItems` | int, optional | Valid only when `multiple` is true. 1 or more. |

Extra rule: when `multiple` is true, no allowed image id and no option value holds `|`.

## ParameterBinding (changed)

| Field | Type | Rule |
|-------|------|------|
| `Name` | string | Unchanged. |
| `Value` | string, optional | Unchanged. For a list binding it holds the items joined by `|`. |
| `Values` | list of string, optional | Ordered, unique, no empty item. When set, `Value` equals the joined text. |

Inherit rule: `Value` null and `Values` null mean inherit.

Empty list rule (analyze F1): an empty `Values` list sets `Value` to null. The empty text `""` is a real value that stops the scope walk, so the system MUST NOT produce it from an empty list. The system treats an empty list as "no value".

Default rule (analyze U1): the `Default` of a multi-value declaration is text. It may hold several items joined by `|`. Each item MUST be allowed. A default with one item is a list of one.

## Read rule (scalar as list)

`ReadAsList(declaration, binding)`:

1. If `Values` is set, return it.
2. Else if the declaration is multi-value and `Value` is not null, return a list with `Value` alone.
3. Else return `null`.

## Validation rules

| Rule | Level | Code |
|------|-------|------|
| `multiple` on kind `text` or `number` | error | `invalid_parameter_declaration` |
| `minItems` or `maxItems` without `multiple`, below 1, `minItems` > `maxItems`, `minItems` > allowed count | error | `invalid_parameter_declaration` |
| Item with `|` in a multi-value declaration | error | `invalid_parameter_declaration` |
| Empty list for a required multi-value parameter (known value) | error | `empty_parameter_list` |
| List count below `minItems` or above `maxItems` (known value) | error | `parameter_list_out_of_limits` |
| Item not in `allowedImages` or `options` | warning | `invalid_parameter_value` |
| List of 2+ items bound to a non-multi-value parameter | warning | `invalid_parameter_value` |
| List of 2+ items in a one-value field at run time | step failure | reason `list_in_single_field` |

## State

No state transition. A list is replaced as a whole when saved.

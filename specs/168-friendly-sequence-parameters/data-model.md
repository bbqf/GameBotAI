# Data Model: Friendly Sequence Parameters

## ParameterDeclaration (extended)

Existing members stay as they are: `name`, `type` (text or number), `default`, `required`, `description`.

| Member | Type | Rule |
|--------|------|------|
| `label` | text, optional | 1 to 80 characters when set. The UI shows `name` when empty. |
| `helpText` | text, optional | Up to 300 characters. |
| `kind` | `text`, `number`, `choice`, `image`; optional | Empty means: derive from `type`. `text` needs `type` = text. `number` needs `type` = number. `choice` (type text or number) and `image` (type text) are valid. The pair `kind` = text with `type` = number is refused. |
| `options` | list of Option, optional | Only for `choice`. 1 to 50 items. |
| `allowedImages` | list of image IDs, optional | Only for `image`. 1 to 50 items, no duplicates. An ID with no stored image is a warning at save. |
| `min`, `max` | whole number, optional | Only for `number`. `min` is not more than `max`. |
| `unit` | text, optional | Only for `number`. Up to 20 characters. |
| `pattern` | text, optional | Only for `number`. Has exactly one `{value}` marker. The result is text. |

### Option

| Member | Type | Rule |
|--------|------|------|
| `label` | text | 1 to 80 characters. Unique in the list, without case. |
| `value` | text | Not empty. Unique in the list. When the declaration `type` is number, it is a whole number. It is not exactly one placeholder such as `{{a}}`. `{{` inside longer text is allowed. |

### Validation rules (declaration)

1. A member that does not fit the `kind` is an error (for example `options` on a `number`).
2. `default`, when set, satisfies the kind: it is in `options`, in the range, or in `allowedImages`.
3. A `choice` with `type` number needs whole-number option values.
4. A `pattern` is refused if its `type` is not number.
5. Old rules stay: name rules, duplicate names, numeric default is a whole number.

## ParameterBinding (unchanged)

`name` and `value`. The value is the technical value:

| Kind | Stored value |
|------|--------------|
| text | the typed text |
| number | the entered whole number, as text (not the pattern result) |
| choice | the option `value` |
| image | the image ID |

`null` still means inherit.

### Validation rules (binding value, at save of a template entry, a sequence step, or a command step)

The service gives a **warning** (code `invalid_parameter_value`) when:

| Kind | Warning when |
|------|--------------|
| number | not a whole number, or below `min`, or above `max` |
| choice | not equal to the `value` of any option |
| image | the ID is not in `allowedImages`, or no stored image has this ID |

A value that is a placeholder (`{{other}}`) is not checked. The run resolves it. The form does not accept a new invalid value while the person edits.

## ParameterValue (domain, runtime)

Gets one new flag, `Mapped` (default false). `ParameterScope.TryResolve` sets it to true after it applies a
`pattern`. A value with `Mapped` = true does not get the pattern again. A binding value that equals an option
value of the callee declaration is also marked, so `TryBindChild` does not resolve `{{` in it (FR-027).

## ResolvedParameter (log)

Gets one optional member, `label`: the option label of a choice. It is empty for all other kinds and for masked names.

## State changes

No new state machine. A declaration is saved with its command or sequence. Changes to `options`,
`min`, `max`, or `pattern` do not change saved bindings. A binding that no longer fits shows a clear
state in the form (FR-013). A run with such a binding starts (FR-016).

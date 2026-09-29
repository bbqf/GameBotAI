# Contract: Run-time resolution of a parameterBindings value

**Feature**: 115-binding-placeholder-scope | **Date**: 2026-09-29

This contract applies to a `parameterBindings` entry on a sequence step that runs a command, and on a nested command step (`type: Command`) inside a command. The REST request and response shapes do not change. The save rules do not change.

## C1. Scope of the resolution

- The service resolves a placeholder in a binding value against the scope outside the binding layer.
- For a sequence step, this is the step scope: queue built-ins, entry values, sequence declarations, and the `loop` layer in a loop body. An `imageVisible {{name}}` guard of the same step uses the same scope.
- For a nested command step, this is the scope of the calling command, which includes the declarations of that command.
- A binding value never resolves against a binding of the same step.
- The declarations of the called command are not in this scope. When a placeholder is unresolved and the called command declares a default for the bound name, the result of C3 applies, and the service does not use that default. To use it, remove the binding or set its value to `null` (inherit).

## C2. Value forms

| Stored value | Command receives | `originLayer` in the log |
|--------------|------------------|--------------------------|
| `null` | The outer value (inherit) | Layer of the outer value |
| No `{{name}}` (for example `pns-todo-radar`, `${x}`, `""`) | The stored text | `command` |
| Exactly `{{name}}` | The resolved value of `name` | The layer that supplied `name` |
| Text with one or more `{{name}}` | The text with each placeholder replaced | `command`, and one more item for each placeholder name with its own layer |

`${name}` is not a placeholder. Only `{{name}}` with a dotted identifier is a placeholder.

## C3. Unresolved placeholder

Error: `ParameterResolutionError(ParameterName = <name>, FieldPath = "parameterBindings.<bindingName>", Reason = "unresolved")`.

Message (the current fixed form, plus a remediation hint for the binding case only):

```text
Step '<stepKey>': parameter '<name>' used by field 'parameterBindings.<bindingName>' could not be resolved from any scope. Do one of these to supply a value for '<name>'. Supply the value in the queue template entry or in the run request. Give '<name>' a default value in the sequence or in the calling command. Bind a literal value, or bind a value in the calling command.
```

The hint is the same at the two call sites. The default must be on a declaration of `<name>` in the sequence (sequence step) or in the calling command (nested command step). The last part is for a nested command step, where the usual fix is a binding or a declaration in the calling command.

`ParameterResolutionError.ToMessage` adds the hint sentence only when `Reason` is `unresolved` and `FieldPath` starts with `parameterBindings.`. The message of each other error (an unresolved placeholder in a command step field, `not_a_number`) does not change (Constitution Principle III, FR-004).

Sequence step:

- The step result has `status` `Failed`, `actionOutcome` `failed`, and the message above.
- The run fails with the same message, and the later steps do not run.
- The command does not run. No device input occurs. A dry run gives the same result.

Nested command step:

- The step outcome has the status `skipped_parameter_unresolved` and the message above, with the step order as the step label.
- The nested command does not run. The next step of the calling command runs.

## C4. Execution log

Example for the reproduction of issue #246 (entry value `pns-alliance-nav-button`, binding `{{novaOptionImage}}`):

```json
{
  "kind": "parameters",
  "message": "Step 0 resolved 1 parameter(s): novaOptionImage=pns-alliance-nav-button",
  "attributes": {
    "stepOrder": 0,
    "resolvedParameters": [
      { "name": "novaOptionImage", "value": "pns-alliance-nav-button", "originLayer": "entry" }
    ]
  }
}
```

Example for a binding `novaOptionImage` = `nova-{{option}}` with entry value `option = b`:

```json
{
  "kind": "parameters",
  "message": "Step 0 resolved 2 parameter(s): novaOptionImage=nova-b, option=b",
  "attributes": {
    "stepOrder": 0,
    "resolvedParameters": [
      { "name": "novaOptionImage", "value": "nova-b", "originLayer": "command" },
      { "name": "option", "value": "b", "originLayer": "entry" }
    ]
  }
}
```

The count K in "Step N resolved K parameter(s)" is the number of items in `resolvedParameters`. Thus K includes the source items of a mixed value: one for the composed name, plus one for each placeholder name that the value used. The log service does not change: it already counts the items of the list.

A source item is not added when `resolvedParameters` already has an item with the same name (for example, a different field of the step uses `option` directly). Thus a source item never gives a duplicate item, and K does not count that name two times. This rule applies only to source items. The current items for a name that a field uses directly do not change.

No log item shows the text of a `{{name}}` placeholder as a resolved value.

## C5. Invariants

- A step with no `parameterBindings` gives the same result as before.
- A literal binding value gives the same result as before.
- The layer precedence rules do not change.
- The effect of a `not_executed` tap outcome on the sequence status does not change.

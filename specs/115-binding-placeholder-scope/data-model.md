# Data Model: Resolve a placeholder in a step parameterBindings value

**Feature**: 115-binding-placeholder-scope | **Date**: 2026-09-29

This feature changes no stored data and no API shape. It changes two run-time types in `GameBot.Domain.Parameters`.

## ParameterBinding (stored, no change to the shape)

| Field | Type | Rule |
|-------|------|------|
| `Name` | string | The parameter name that the binding supplies. |
| `Value` | string? | `null` means "inherit". Other values are text. A `{{name}}` placeholder in the text resolves at run time against the scope outside the binding (this feature). `${name}` is literal text. |

Value forms and their run-time result:

| Form | Example | Command receives | Origin layer |
|------|---------|------------------|--------------|
| Inherit | `null` | The value of the outer scope, as before | Layer of the outer value |
| Literal | `pns-todo-radar`, `${x}`, `""` | The text as it is | `command` |
| Whole placeholder | `{{novaOptionImage}}` | The resolved value | Layer that supplied it (`queue`, `entry`, `sequence`, `command`, `loop`, `default`) |
| Text with placeholders | `nova-{{option}}` | The text with each placeholder replaced | `command`, plus `Sources` |
| Unresolved placeholder | `{{missing}}` | Nothing: the step fails | Not applicable |

## ParameterValue (run time, one optional member added)

| Field | Type | Rule |
|-------|------|------|
| `Text` | string | The resolved value. |
| `OriginLayer` | string | One of `ParameterScopeLayers`. |
| `Sources` | `IReadOnlyList<ResolvedParameter>?` | NEW, optional, default `null`. Set only for a binding value with text around one or more placeholders. One entry for each placeholder name, with its value and origin layer. |

## ParameterScope (run time, immutable)

| Member | Change |
|--------|--------|
| `_values` | Type changes from `Dictionary<string, string>` to `Dictionary<string, ParameterValue>`. |
| `Child(layerName, bindings, declarations)` | No behavior change. Stores `ParameterValue(value, layerName)`. |
| `FromQueue`, `WithIteration` | No behavior change. |
| `TryResolve(name, out value)` | Returns the stored `ParameterValue`. For the current layers, the result does not change. |
| `TryBindChild(layerName, bindings, out child, out error)` | NEW. Resolves each non-null binding value against the receiver, then makes the new layer. Returns `false` with a `ParameterResolutionError` when a placeholder does not resolve. |

Relationships: a `ParameterScope` node has one `Parent`. `TryBindChild` returns a new node whose `Parent` is the receiver. The receiver does not change.

## ParameterResolutionError (no change)

For a binding placeholder that does not resolve:

| Field | Value |
|-------|-------|
| `ParameterName` | The placeholder name, for example `novaOptionImage` |
| `FieldPath` | `parameterBindings.<bindingName>`, for example `parameterBindings.novaOptionImage` |
| `Reason` | `unresolved` |
| `OffendingValue` | `null` |

## Execution log detail `parameters` (no change to the shape)

Each `resolvedParameters` item keeps the members `name`, `value`, `originLayer`. For a whole-placeholder binding, the item shows the resolved value and the true origin layer. For a text value with placeholders, the item of the composed value has the layer `command`, and one item follows for each source placeholder.

## State transitions

Not applicable. The scope exists only during one firing.

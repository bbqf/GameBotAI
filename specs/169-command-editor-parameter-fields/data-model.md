# Data Model: Command Editor Parameter Fields

No service entity changes. No new persisted data. This file lists the UI types.

## StepEntry (changed)

File: `src/web-ui/src/components/commands/CommandForm.tsx`

| Field | Type | Note |
|-------|------|------|
| `fieldTemplates` | `Record<string, string> \| undefined` | NEW. Key is a path from `CommandStepFieldPaths`. Value is `{{name}}`. |

Rules:
- `stepsFromDto` copies `fieldTemplates` from the DTO without change.
- `stepsToDto` writes `fieldTemplates` only when the map has one entry or more.
- A panel changes only the keys that it has a control for. It keeps all other keys (FR-008).
- **Type change rule (one rule)**: When the step type changes, the editor drops each key that does not belong to the new step type and keeps all other keys. When the type stays the same, the editor keeps all keys, also keys with no control. A new step starts with no templates.

## Command parameter (UI view of the existing declaration)

| Field | Type | Note |
|-------|------|------|
| `name` | `string` | Name used in `{{name}}`. |
| `type` | `'text' \| 'number'` | Declared type. |
| `inputKind` | optional: image picker, choice list, or number with pattern | Since feature 168. A choice list also has the type of its option values (text or number). It decides the fit (R5). |

## Service error mapping (UI)

| Field | Type | Note |
|-------|------|------|
| `stepOrder` | `number \| undefined` | Read from the `Step {order}:` prefix of the service message. |
| `fieldPath` | `string` | From the issue details. |
| `parameterName` | `string` | From the issue details. |

The editor passes the message to the field of the step with this order. If `stepOrder` is missing or no step matches, the message shows in the form summary only.

## ParametrizablePath (new)

File: `src/web-ui/src/components/parameters/fieldTemplatePaths.ts`

| Field | Type | Note |
|-------|------|------|
| `path` | `string` | For example `swipe.durationMs`. |
| `kind` | `'integer' \| 'decimal' \| 'image'` | `integer` and `decimal` are numeric. |

Content: 14 numeric paths and 2 image paths. The list equals `CommandStepFieldPaths` (in `src/GameBot.Domain/Commands/CommandStep.cs`). The drift test checks this. A path gets a control only if the editor has a field for it. The 4 `ensureGameRunning` numeric paths have no field, so they get no control and their templates stay unchanged on save.

Functions:
- `isImagePath(path)`: true for a path of kind `image`.
- `isParametrizablePath(path)`: true for any path in the list.
- `parameterFit(declaration, path)`: returns `{ fits: true }` or `{ fits: false, hint: string }`. A choice parameter fits by the type of its option values. A queue built-in has no declaration and gets no fit result.
- `templateParameterName(template)`: returns `name` from a whole placeholder `{{name}}`, or `undefined`.

## ParameterSourceControl props (new)

| Prop | Type | Note |
|------|------|------|
| `path` | `string` | Field path. |
| `label` | `string` | Visible field label. Used in the ARIA names. |
| `parameters` | `ParameterDeclaration[]` | Declared parameters of the command. |
| `template` | `string \| undefined` | Current placeholder for the path. |
| `onChange` | `(template: string \| undefined) => void` | `undefined` removes the key. |
| `disabled` | `boolean` | Same as the field. |
| `error` | `string \| undefined` | Service message for this path. |
| `children` | `ReactNode` | The plain input. Hidden while a template is set. |

States:
- plain: `template` is `undefined`. The child input shows.
- parameter: `template` is set. A chip shows `{name}`. The child input does not show. The plain value stays in the panel state.
- missing: `template` names a parameter that is not declared and is not a queue built-in. The chip shows the placeholder name and the text "Parameter not found". The person can remove it. The save is not blocked.

Kept plain value: each panel keeps the plain value in local state while a template is active. On save, the panel sends the kept value, or `0` for an empty required whole-number field. The service ignores it when a template exists.

State transitions: plain -> parameter on select. Parameter -> plain on remove. Parameter -> parameter on select of another parameter.

## Panel outputs (changed)

Each of `SwipePanel`, `TapPanel`, `WaitForImagePanel`, and `EnsureEmulatorRunningPanel` gets these props and results:
- Prop `parameters: ParameterDeclaration[]`.
- Prop `fieldTemplates?: Record<string, string>` (initial value).
- Prop `fieldErrors?: Record<string, string>` (service messages by path, for this step only; the page picks the step by order).
- The confirm value includes `fieldTemplates`.

## Wire format (unchanged)

```json
{
  "type": "Swipe",
  "order": 0,
  "swipe": { "startX": 10, "startY": 10, "endX": 50, "endY": 50, "durationMs": 300 },
  "fieldTemplates": { "swipe.durationMs": "{{delay}}" }
}
```

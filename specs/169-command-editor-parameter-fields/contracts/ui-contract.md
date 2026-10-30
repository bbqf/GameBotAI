# UI Contract: "Use a parameter" control

The feature adds no API. The wire format of `fieldTemplates` stays as it is. This file states the contract of the new UI control.

## Visible behavior

| Situation | What the person sees |
|-----------|----------------------|
| Field in plain mode | The normal input. A button "Use a parameter for {label}". |
| Control opened | A list titled "Parameters of this command". A second group "Queue values". |
| Command has no parameters | The text "This command has no parameters. Add one in the Parameters section." The list has no selectable rows for command parameters. |
| Parameter that fits | A row with the label and the name. |
| Parameter that does not fit | The same row plus a hint. The row stays selectable. |
| Field in parameter mode | A chip with the parameter name. No plain input. A button "Remove parameter from {label}". |
| Parameter not declared | The chip with the placeholder name and the text "Parameter not found". The person can remove it. The save is not blocked. |
| Queue value | A row in the group "Queue values", below the parameters. No hint. |
| Parameter selected, field had a plain value | The editor keeps the plain value. It saves the kept value (0 for an empty required whole-number field). |
| Image field, service refuses an empty inline image | The control asks the person to select a stored image first, then the parameter. |
| Save refused by the service | The service message at the field of the step that the message names, and in the form summary. |
| Service message names a step that the editor cannot find | The message shows in the form summary only. |
| Step type changes | The editor drops each template key that does not belong to the new type. It keeps all other keys. |
| Step type stays the same | The editor keeps all keys, also keys with no control. |

## Hint texts (STE)

- Numeric field, text parameter: "This parameter gives text. A numeric field needs a number. The save will fail."
- Image field, other parameter: "This parameter is not an image picker."
- Fit rule: a number or a choice with number options fits a numeric field. An image picker parameter fits an image field. A choice with text options fits neither field type. A queue value shows no hint.
- Fields: the control shows only on a field that exists in the editor (12 fields). The 4 `ensureGameRunning` numeric paths get no control.

## Keyboard and screen reader (FR-011)

- The open button has `aria-label="Use a parameter for {label}"`, `aria-haspopup="listbox"`, and `aria-expanded`.
- The list has `role="listbox"` and `aria-label="Parameters of this command"`. Each row is `role="option"`.
- Tab reaches the button. Enter or Space opens the list. Arrow keys move. Enter selects. Escape closes and returns focus to the button.
- The remove button has `aria-label="Remove parameter from {label}"`.
- The hint is linked to its row with `aria-describedby`.
- A service error uses `role="alert"` and `aria-describedby` on the control.
- Performance: the list opens in under 100 ms with up to 50 parameters.

## Written template

| Action | Result in `fieldTemplates` |
|--------|----------------------------|
| Select `delay` for `swipe.durationMs` | `"swipe.durationMs": "{{delay}}"` |
| Select `buttonToTap` for `primitiveTap.detectionTarget.referenceImageId` | `"primitiveTap.detectionTarget.referenceImageId": "{{buttonToTap}}"` |
| Remove the parameter | The key is deleted. An empty map is not sent. |
| Load a command with a template | The chip shows. The template is saved again without change. |

## Phone support level

Desktop-only. No phone layout is added. The page keeps the notice "Use a larger screen for this area."

## Service behavior that does not change

- Unknown path: `unknown_field_template_path`.
- Image path value that is not one whole placeholder: `invalid_field_template_value`.
- Text parameter in a numeric field: `text_parameter_in_numeric_field`. The message has the form `Step {order}: parameter '{name}' gives text, but field '{path}' needs a number. ...`. The editor reads the step order from it (see research.md R7).
- No file of feature 168 changes.

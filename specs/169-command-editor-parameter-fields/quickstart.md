# Quickstart: Command Editor Parameter Fields

Use this guide to check the feature by hand. No API script is needed (SC-003).

## Prepare

1. Start the service and the web UI.
2. Open the Commands page. Select "Create Command".
3. In "Parameters", add a number parameter `delay`, a text parameter `note`, and an image parameter `buttonToTap` (image picker kind).

## Check 1: number parameter in a numeric field (US1)

1. Select the action type "Swipe". Fill Start X, Start Y, End X, and End Y.
2. Next to "Duration (ms)", select "Use a parameter for Duration (ms)". Select `delay`.
3. Expect: the field shows the chip `delay`. The number input is gone.
4. Select "Add". Select "Save". Open the command from the service. Expect `fieldTemplates` with `swipe.durationMs` = `{{delay}}`.
5. Open the command again and select "Remove parameter from Duration (ms)". Expect the number input back. Save. Expect no `fieldTemplates` key.

## Check 2: image parameter in a tap step (US2)

1. Select the action type "Tap".
2. Next to "Reference image", select "Use a parameter for Reference image". Select `buttonToTap`.
3. Save the step and the command. Expect `fieldTemplates` with `primitiveTap.detectionTarget.referenceImageId` = `{{buttonToTap}}`.

The service refuses an empty inline image. So the button "Use a parameter" stays disabled until you select a stored image. The text "Select a stored image first. Then select the parameter." shows under the field. Select a stored image in the image chooser, then select the parameter. Expect the image list to fill the width of the field and to show aligned rows.

## Check 3: hint and service error (US3)

The service gives the error only for a parameter that gives a friendly text value: an image parameter, a text parameter with a pattern, or a choice with text options. A plain text parameter keeps its old behavior and gives no error (feature 168, FR-018). So use `buttonToTap` for this check.

1. Open the control on the Swipe duration field. Expect `buttonToTap` with the hint "This parameter gives text...".
2. Select `buttonToTap`. Add the step. Save the command.
3. Expect the error `text_parameter_in_numeric_field`. The message names `swipe.durationMs` and `buttonToTap`. Expect the error at the duration field of the step that the message names. If the command has two Swipe steps, only the named step shows the error at its field. If the editor cannot find the step, the form summary shows the error alone.
4. Remove all parameters from the command. Open a control. Expect the text "This command has no parameters".

5. Add a choice parameter with number options and one with text options. Expect no hint for the number choice in the Swipe duration control. Expect the hint for the text choice. Expect the group "Queue values" with no hint.
6. Edit the command. Delete the parameter `delay` that a field uses. Expect the chip `delay` with the text "Parameter not found". Expect a Remove button. Save is not blocked by the editor.

## Check 4: round trip from the API (SC-005)

1. Create a command with `fieldTemplates` through the API.
2. Open it in the editor. Expect the chip on each field with a template.
3. Save without change. Read the command again. Expect the same `fieldTemplates`.
4. Include a template for `ensureGameRunning.readinessTimeoutMs`, which has no editor field. Expect the template unchanged after the save.

## Check 5: step type change (FR-008)

1. Open a command with a Swipe step that has a template for `swipe.durationMs`.
2. Change the type of the step to "Tap". Expect the template for `swipe.durationMs` to be gone. Expect other keys, for example a key with no control, to stay.
3. Change a step but keep its type. Expect all keys to stay, also keys with no control.

## Check 6: feature 168 flows with no API script (SC-003)

1. Do the Check 1 and the Check 5a Part B flows of feature 168 in the UI alone. Use the new control to set the step fields.
2. Expect that you do not need `specs/168-friendly-sequence-parameters/demo-data.ps1`.
3. Expect that no file of feature 168 changed in this feature (`git diff origin/master -- specs/168-friendly-sequence-parameters` is empty).
4. Use a timer for Check 1 of this file. Expect under 1 minute (SC-001).

## Automated checks

- Web UI: run the Vitest suite for `src/web-ui` (control, helper, panels, page round trip).
- Service: run `dotnet test` for the drift test `FieldTemplatePathMirrorTests`.
- Service check (run first for the image story): find out if the service accepts or refuses an empty inline image together with an image template. The test asserts the real behavior. Record the result in a code comment and in the PR description. The result decides the image control flow.
- Error mapping: a page test with two steps of the same type checks that only the named step shows the error, and that an unknown step gives the form summary only.
- Performance: a component test or a reasoned target shows that the control opens in under 100 ms with 50 parameters.
- Keyboard: the component test opens the control with Enter, moves with arrow keys, selects, and closes with Escape.

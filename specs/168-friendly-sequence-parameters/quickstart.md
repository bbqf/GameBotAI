# Quickstart: Friendly Sequence Parameters

Use these steps to check the feature by hand after the implementation.

## Set up

1. Start the service and the web UI. Open the Authoring area on a desktop-size window.
2. Make sure at least two images exist (Images page).
3. Create a sequence "Friendly demo" with one command step that taps an image field and uses a numeric field.

## Check 1: Image parameter (US1)

1. In the sequence, add a parameter. Type the label "Button to tap". Check that the name `buttonToTap` is suggested.
2. Set the kind to Image. Choose two of the stored images as allowed images. Check that the live preview shows a picker with only these two. Check that a save with no allowed image is refused.
3. Put `{{buttonToTap}}` in the image field of the step. Save.
4. Open a queue template. Add the sequence. Open Parameters.
5. Select an image by its thumbnail. Check that no text box for an ID shows and that no other image shows.
6. Save and run. Check in the execution log that the step used the ID of the selected image, and the log shows the image name.

## Check 2: Choice parameter (US2)

1. Add a parameter "Difficulty" with the kind Choice and the options Easy = 1, Hard mode = 3 (type Number).
2. Use `{{difficulty}}` in a numeric field. Save.
3. In the queue template, select "Hard mode". Check that the form shows labels only.
4. Run. Check that the log shows value 3 and label "Hard mode".
5. Try to add two options with the same label. Check that the save is refused with a clear message.

## Check 3: Number with pattern (US3)

1. Add a parameter "Emulator number" with the kind Number, min 1, max 10, pattern `emulator-{value}`.
2. Use `{{emulatorNumber}}` in a text field. Save.
3. In the queue template, enter 11. Check that the form shows a range error with a hint and does not accept the value.
4. Enter 5. Run. Check that the field got `emulator-5`.

## Check 4: Editor (US4)

1. Add one parameter of each kind. Check that the preview matches the value form.
2. Move a card up. Check that the preview order changes.

## Check 5: Phone size (US5)

1. Set the window to 375 px wide.
2. Open the declaration editor. Check the "Use a larger screen" notice.
3. In a place that shows the value form, check that all four kinds work with touch-size controls and no horizontal scroll.

## Check 5a: Pass-through and numeric field

1. In a sequence with the parameter "Difficulty", open a step that calls a command with a choice parameter. Check that the form offers the options and "Use a parameter of this sequence".
2. Choose the sequence parameter. Run. Check that the command got the value of the sequence parameter.
3. In a command, use a text-typed parameter in a numeric field. Check that the save is refused with a message that names the field and the parameter.

## Check 6: Old data

1. Open a command and a sequence that were saved before this feature. Check that their parameters show as plain text or number inputs.
2. Run a queue that uses them. Check that the result is the same as before.
3. Time Check 1 (image, under 10 s) and the author task of Check 2 (under 2 minutes).

## Automated checks

```powershell
dotnet test "C:\src\GameBot\GameBot.sln"
```

Run the web UI tests with `npm test` and `npm run lint` in `C:\src\GameBot\src\web-ui`.

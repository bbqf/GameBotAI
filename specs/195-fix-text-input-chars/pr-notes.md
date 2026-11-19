# PR notes: TextInput step types all characters (195)

## Performance note (Constitution IV, SC-004)

A text of n characters takes n ADB commands and (n - 1) pauses. For `250` with the default pause: 3 x (50 to 150 ms) + 2 x 100 ms = 350 to 650 ms. Before the fix: 1 command (50 to 150 ms). The increase is less than 1 second (SC-004). The change has no effect on other step types.

## Device check (SC-001, SC-002)

The device check is open. No device with the game was available in the implementation run. The reporter of issue #404 must do quickstart.md section 2 (10 tries of the text `250` on the game input field) and record the result in the issue.

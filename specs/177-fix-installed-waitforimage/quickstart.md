# Quickstart: Fix inline WaitForImage in installed gamepack sequences

Use absolute paths. Run all commands from PowerShell with full paths. Do not use a `cd <dir>;` prefix.

## 1. Check the runtime type (before the mapper is final)

1. Write a unit test in `C:\src\GameBot\tests\unit\Gamepacks`. It writes a real pack JSON file with an inline WaitForImage step in the stored form (`action.parameters`) and one in the API form (`primitiveAction.payload`). It reads the file with `GamepackFileSerializer` and checks the runtime type of `detectionTarget`, `timeoutMs`, `until`, and `absentCaptures`.
2. Note the result. The mapper MUST accept this type.

## 2. Reproduce the bug (before the fix)

1. Write the contract test in `C:\src\GameBot\tests\contract\Gamepacks`. It writes a real pack JSON file with a sequence that has one inline WaitForImage step: `timeoutMs` 3000, `detectionTarget.referenceImageId` `pns-city-anchor`, `confidence` 0.85. Add a second step in a loop `Body` and a third in an if `ElseBody`.
2. Install the pack through the install endpoint. Read the stored sequence.
3. Expect `waitForImage.timeoutMs` 3000 and the image id on every step.
4. Run the test. It must FAIL before the fix (`waitForImage` is null). Keep the failing output as evidence.

## 3. Apply the fix

1. Add `WaitForImageConfigMapper` in `C:\src\GameBot\src\GameBot.Domain\Commands`. Move `TryReadInt32` and `TryReadRegion` into it from `SequencesEndpoints`.
2. Change `SequencesEndpoints`: call `WaitForImageConfigMapper.Map` for top-level and nested steps. Call `WaitForImageConfigMapper.TryReadRegion` in the save-time region check. Remove the private code that Domain replaces.
3. Add `GamepackWaitForImageMapping` in `C:\src\GameBot\src\GameBot.Domain\Gamepacks`.
4. Call it in `GamepackValidator` after `RewriteCommandIds` and before the `PlannedObject` is added.

## 4. Check

- Run the new unit tests and the new contract tests. All must pass.
- Parity: the contract test that compares the stored step of an install with the stored step of `POST /api/sequences` for the same payload must pass.
- Missing reference image: a pack with a WaitForImage step that names a missing image gives the same install result as before the fix.
- Run the existing gamepack, sequence, command, WaitForStill, and feature 130 region tests. All must pass (SC-005).
- Take-over: install with takeOver. The stored hash must equal the hash of the stored content. A second dry-run install must report no change.
- Update: install an old-form sequence (no `waitForImage`), then update the pack. The stored steps must hold the configuration.
- Optional manual run: install the pack, run the sequence with the image on screen. The step log shows `image_detected`, `effectiveTimeoutMs` 3000, and the image id.

## 5. Finish

- Add row 177 to `C:\src\GameBot\specs\STATUS.md`. Set the spec `Status` line.
- Review `C:\src\GameBot\docs\architecture.md`. Refresh "Last reviewed" if the text changes.
- Bump `patch` in `C:\src\GameBot\installer\versioning\version.override.json` before the push (see CLAUDE.md).
- Check the shard balance rule if a test shard list changes.

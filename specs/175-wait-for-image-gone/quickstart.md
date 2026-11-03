# Quickstart: Wait Until an Image Is Gone

## Use the option in the editor

1. Open the Sequences page. Add a `WaitForImage` step after the tap that shows the toast.
2. Pick the toast image. Set "Wait until" to "The image is gone".
3. Keep "Absent captures" empty (2) or set a number of 1 or more. Set the timeout.
4. Add an arrival check after the step if the next screen must be proven. Save the sequence.

The phone support level of this form is `desktop-only`. On a phone the Sequences page shows the
desktop-only notice.

## Use the option in the API

Send the step JSON from `contracts/wait-for-image-until.md` in the `steps` list of `POST /api/sequences`
or `PUT /api/sequences/{id}`. Run the sequence with `POST /api/sequences/{id}/execute`.

## Check the feature

1. Run the unit tests: `dotnet test` on `tests/unit/GameBot.UnitTests.csproj` with the filter
   `FullyQualifiedName~WaitForImage`.
2. Run the contract and integration tests with the same filter.
3. Run the web UI tests: `npm test` in `src/web-ui`, filter `WaitForImageUntil`.
4. Run a sequence on a session: tap, then the step. Check in the execution log that the step ends with
   `image_absent` soon after the toast clears.
5. Use an image that never leaves. Check that the step ends with `timeout_elapsed` at `timeoutMs`.
6. Run an old sequence with a present-mode `WaitForImage`. Check that nothing changed.

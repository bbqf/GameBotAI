# Quickstart: Wait for Still Step

## Use the step in the editor (desktop)

1. Open the Sequences page on a desktop screen. Phone support level of this form: desktop-only.
2. Click "Add wait for still step".
3. Set the region if one part of the screen always moves. Leave it empty to watch the whole screen.
4. Keep the defaults or set `stableMs`, `maxDiff`, and `timeoutMs`.
5. Put the step after a pan, scroll, or swipe step. Save the sequence.

## Use the step in the API

Send the step JSON from `contracts/wait-for-still-step.md` in the `steps` list of `POST /api/sequences`
or `PUT /api/sequences/{id}`. Then run the sequence with `POST /api/sequences/{id}/execute`.

## Branch on the outcome

Add an `If` step after the wait. Use a `commandOutcome` condition with `stepRef` set to the wait step and
`expectedState` set to `still` or `timeout_elapsed`.

## Check the feature

1. Run the unit tests: `dotnet test` on `tests/unit/GameBot.UnitTests.csproj` with the filter
   `FullyQualifiedName~WaitForStill`.
2. Run the contract and integration tests with the same filter.
3. Run the web UI tests: `npm test` in `src/web-ui`, filter `WaitForStill`.
4. Run a sequence on a session with a pan followed by the step. Check in the execution log that the step
   ends with `still` soon after the pan stops.
5. Make a screen that never stops. Check that the step ends with `timeout_elapsed` at `timeoutMs`.

# Quickstart: check the fix

1. Create a sequence with two image parameters: `q` (multi-value, `pns-showdown-tile` and `pns-city-anchor`) and `s` (one value).
2. Add three inline WaitForImage steps with `timeoutMs` 2300: one with `{{s}}`, one with `{{q}}`, and one with `referenceImageIds` that lists both ids.
3. Run the sequence from a queue entry with `q` = both images and `s` = `pns-city-anchor`. Show `pns-city-anchor` on the screen.
4. Read the execution log of the run.
5. Expect: all three steps end with `image_detected`. Each `detailAttributes` has `matchedImageId` = `pns-city-anchor`. No `referenceImageId` shows `{{...}}`.
6. Remove the value of `s`. Run again. Expect: the step with `{{s}}` fails with the unresolved-parameter message, not with `image_unavailable`.

Run the tests:

- `dotnet test` for the unit tests in `tests/unit` that match `SequenceRunnerWaitForImage` and the resolver.
- `dotnet test` for the integration tests in `tests/integration/Sequences` that match `WaitForImageSequenceExecution`.
- `npm test` in `src/web-ui` for `executionLogsApi`.

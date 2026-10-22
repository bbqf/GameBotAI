# Red-first evidence (FR-013)

Test: `GameBot.UnitTests.Sequences.NotifyStepTargetDeliveryTests.MessageWithoutUrl_IsSentToTheEnabledTarget`

## Before the fix (T004): RED

Command: `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~NotifyStepTargetDeliveryTests"`

Result: `Failed: 1, Passed: 0, Total: 1`

Message: `Expected harness.Channel.Sent.Select(s => s.Text) to contain a single item, but the collection is empty.`

The notify step had one enabled target and no `url`. The channel did not get the message.

## After the fix (T009): GREEN

Result: the test passes. All 8 tests of `NotifyStepTargetDeliveryTests` pass: `Failed: 0, Passed: 8, Total: 8`.

Step-through note (T014): the dry-run path is `StepThroughActionPreview.Describe` and its outcome is `previewed` (`SequenceStepper.PreviewOutcome`), not `preview`.

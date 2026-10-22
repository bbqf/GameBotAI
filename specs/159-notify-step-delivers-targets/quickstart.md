# Quickstart: Notify Step Delivers to the Notification Targets

## Check the change by hand

1. Start the service.
2. Create one enabled notification target: `POST /api/notifications/targets` with a `telegram` target (test bot token and chat id).
3. Save a sequence with one `notify` step: `message` "Quest A is ready", no `url`.
4. Run the sequence (queue run or ad-hoc).
5. Check that the Telegram chat gets "Quest A is ready" one time.
6. Check that the execution log of the step says "notification delivered" and that the service log does not say "no destination configured".
7. Disable the target and run again. Check that the chat gets nothing and the step is still `success`.
8. Set `url` on the step and run again. Check that only the `url` gets the message.

9. Run the sequence in step-through (dry run) mode. Check that the chat gets nothing (FR-014).

## Test-first check (FR-013)

Before the fix, write the regression test in `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs` (no `url`, one enabled target, the message reaches the target). Run it on the current code and check that it fails. Then make the fix and check that it passes.

## Extra checks

- Time limit (FR-016): a channel that does not answer gives "not delivered" after 35 s at most, and the step is `success`.
- Partial failure (FR-017): one good and one failing target give "delivered", and the log names 1 failed target.
- Before the dry-run test, read `StepThroughActionPreview` and confirm the path and outcome name (FR-019).

## Close the work (FR-015, FR-018)

Set the Status of spec.md to Implemented. Update `specs\STATUS.md`. Set spec 137 to "Implemented (iterated by 159)" and update its row.

## Run the tests

1. Build: `dotnet build C:\src\GameBot\GameBot.sln`
2. Test: `dotnet test C:\src\GameBot\GameBot.sln`

New test files:

- `tests/unit/Notifications/QueueNotificationWorkerTextSendTests.cs`
- `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`
- `tests/contract/Sequences/NotifyActionOpenApiTextTests.cs`
- `tests/integration/Notifications/NotifyStepToTargetsTests.cs`

Existing queue notification tests MUST pass with no change (SC-005).

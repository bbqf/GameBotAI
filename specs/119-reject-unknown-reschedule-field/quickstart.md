# Quickstart: verify the unknown-field check

1. Run the unit tests for the step validation:
   `dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~Sequences"`
2. Run the contract tests for reschedule-self:
   `dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~SelfRescheduleActionContractTests"`
3. Manual check with the API: post a sequence with `dryRun: true` and a `reschedule-self` step with the payload `{ "option": "Timer", "timerTimeOfDay": "11:00", "nextDay": true }`. Expect 400 and an error that names `nextDay`.
4. Post the same step without `nextDay`. Expect success.
5. Post the step with `option: "Bogus"`. Expect 400 with the unchanged message.

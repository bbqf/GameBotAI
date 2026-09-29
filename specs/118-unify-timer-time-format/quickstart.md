# Quickstart: check the timerTimeOfDay rule

**Feature**: 118-unify-timer-time-format

## Run the tests

1. Build: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`
2. Run the new and changed tests:
   - `dotnet test` on the unit project with filter `TimerTimeOfDayFormatTests|SelfReschedulePayloadTests`
   - `dotnet test` on the integration project with filter `QueueTemplatesScheduleTypeTests|TimerTimeOfDayParityTests`
   - `dotnet test` on the contract project with filter `PrimitiveActionTypesOpenApiTests|TemplateTimerTimeOfDayOpenApiTests`
3. Before the fix, these tests fail: the `HH:mm:ss` template tests, the strict-reject sequence tests, and the same-result test. After the fix, all tests pass.

## Check by hand (service on port 8080)

1. Save a template with a Timer entry and `timerTimeOfDay` `"15:30:45"`. Expect 201, and the response shows `15:30:45`.
2. Save the same with `"15:30:00"`. Expect 201, and the response shows `15:30`.
3. Save with `"11:00 PM"`. Expect 400, and the message names `HH:mm or HH:mm:ss`.
4. Send a sequence with a `reschedule-self` Timer step and `timerTimeOfDay` `"11:00 PM"` and `dryRun: true`. Expect a validation error with the same phrase.
5. Send the same step with `"15:30:45"`. Expect a valid result.
6. Open `/swagger/v1/swagger.json`. The `timerTimeOfDay` text for the template entry and for `reschedule-self` names `HH:mm or HH:mm:ss`.

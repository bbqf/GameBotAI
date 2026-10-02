# Quickstart: Reschedule-Self Keep Earliest

## Use

Add `"keep": "earliest"` to each `reschedule-self` Timer step of a sequence that books more than one timer in one run:

```json
{ "option": "Timer", "timerRelativeOffset": "00:50:00", "keep": "earliest" }
```

If a run books 15, 50, 30, and 40 minutes, the one pending booking is 15 minutes ahead. Without `keep`, it is 40 minutes ahead.

A pending booking that an earlier run or the engine made (also a booking that the engine puts back after a hold) does not block the new run. The first booking of the new run replaces it.

## Verify

1. Run the unit tests: `dotnet test` with the filters `SelfRescheduleCoordinator`, `QueueRunHandle`, and `RescheduleSelfKeep`.
2. Save a step with `keep: latest`. Expect a 400 that names `earliest`.
3. Save a step with option `OncePerRun` and `keep: earliest`. Expect a 400 that says `keep` is only valid when option is Timer.
4. Save a step with `keep: earliest` and read it back. Expect the same value.
5. Read the OpenAPI document. The `reschedule-self` description names `keep`.
6. Run a queue with the sequence. In the execution log, a losing booking shows the message "kept the earlier pending booking" with both fire times.
7. Check that `docs/architecture.md`, `specs/STATUS.md`, and `CHANGELOG.md` describe the feature.

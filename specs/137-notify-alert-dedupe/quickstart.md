# Quickstart: Notify alert dedupe window

## Use it

1. Add a `notify` step to a sequence.
2. Set `dedupeKey` to a short name, for example `daily-timeout`.
3. Set `since` to a time of day (`02:05`) or `within` to a duration (`00:30:00`).
4. Save the sequence. The service returns 400 if a field is wrong.
5. Run the sequence two times in the same queue and window.
6. Read the execution log. The first step outcome is `sent`. The second is `suppressed`.

## Check the main rules

- Run the sequence in a second queue. The step sends (scope is queue and key).
- Change the key. The step sends.
- Restart the service inside the window. The next run is `suppressed`.
- Run the step 10 times in one window. One run is `sent` and nine are `suppressed`.
- Delete the queue. Its records are gone.
- Remove all three new fields. The step sends on every run.

## Build and test

Run the unit and contract tests for the notify step:

```text
dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Notify"
```

# Quickstart: check `onTimeLimit`

1. Create a sequence with a short limit and a long step:
   `POST /api/sequences` with `onTimeLimit` = `end` and `watchdogTimeoutMs` = 2000. Add a step that waits 10 s.
2. Read it: `GET /api/sequences/{id}` shows `"onTimeLimit": "end"`.
3. Put the sequence in a queue and start the queue. Wait for the first firing to pass the limit.
4. Read the log: `GET /api/execution-logs`. The sequence entry has `status` = `success`,
   `cancellationReason` = `sequence_time_limit`, and `timeLimitMs` = 2000. The queue gets no failure message.
5. Set `onTimeLimit` to `fail` (PATCH). Run again. The entry has `status` = `failure` and the same reason.
5a. Remove `watchdogTimeoutMs` and set `onTimeLimit` to `end`. A run that passes 240000 ms ends with `success`,
   the reason `sequence_time_limit`, and `timeLimitMs` = 240000.
6. Send `"onTimeLimit": "stop"`. The service answers 400 and the error lists `fail, end`.
7. In the web UI, open the sequence form. The select "When the time limit is reached" shows the saved value. Check
   it at phone width (single column, no horizontal scroll, select hit area at least 44 x 44 px, font at least
   16 px).
8. Run the tests:
   `dotnet test` (unit, integration, contract filters for `SequenceTimeLimit` and `OnTimeLimit`) and
   `npm test` in `src/web-ui`.

## Result of the check

The check ran as automated tests. No live queue ran on an emulator, and no browser check ran at phone width.

- Steps 1, 2, 5, 5a, 6: `SequenceOnTimeLimitApiIntegrationTests`, `SequenceTimeLimitEndLogIntegrationTests`,
  `SequenceTimeLimitEndDefaultLimitTests`, and `SequenceTimeLimitFailRegressionTests` pass.
- Steps 3 and 4: `QueueExecutionServiceTests` (end and fail at the limit) pass. They show the `Success` record,
  no failure message, and no daily retry for `end`.
- Step 7: the Jest tests `SequencesPage.onTimeLimit` and `SequencesPage.onTimeLimitLayout` pass. They check the
  select, the saved value, and the style values (44 px and 16 px).

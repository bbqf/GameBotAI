# Quickstart: Per-Sequence Run Statistics per Queue

**Feature**: 105-sequence-run-statistics

This page shows how to check the feature by hand against a local service on port 8080. The automated tests are in the plan (section "Test plan").

## 1. Build and test

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\GameBot.sln" -c Debug --no-build
```

The web UI does not change, so `npm` steps are not necessary.

## 2. Save a daily task with a `lastRun` guard

Put the guard in the first step of a count loop of one iteration. A Break step is valid only in a loop body, so a Break step at the top level gets a 400. The guard breaks the loop, and thus stops the work, when the task already succeeded in the current "training day" (11:00 to 11:00 on the next day).

```json
{
  "name": "Daily Training",
  "steps": [
    {
      "stepId": "daily",
      "stepType": "Loop",
      "loop": { "loopType": "count", "count": 1 },
      "body": [
        {
          "stepId": "done-today",
          "stepType": "Break",
          "breakCondition": { "type": "lastRun", "sequence": "self", "status": "success", "since": "11:00" }
        },
        { "stepId": "do-work", "stepType": "Action", "commandReference": { "commandId": "<command id of the real work>" } }
      ]
    }
  ]
}
```

A second form, with no loop: put the guard on the work step as a step condition with `"negate": true`. The work step is then skipped when the task already succeeded in the window.

Check it first with `dryRun`:

```powershell
$body = Get-Content -Raw "C:\path\to\daily-training.json" | ConvertFrom-Json
$body | Add-Member dryRun $true
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/sequences" -ContentType "application/json" -Body ($body | ConvertTo-Json -Depth 20)
```

Expected: `{ "valid": true, "dryRun": true, "errors": [] }`.

## 3. See a validation error

Send the same body with both `since` and `within`:

```json
{ "type": "lastRun", "sequence": "self", "status": "success", "since": "11:00", "within": "24:00:00" }
```

Expected: HTTP 400. The message contains `lastRun condition accepts only one of since or within, not both.`

Other bad values to try: `"status": "failed"`, `"since": "9:00"`, `"within": "00:00:00"`, `"within": "400.00:00:00"`, no `sequence`. Each gets a 400 that names the field.

## 4. Run the task in a queue two times

1. Add the sequence to a queue and start the queue after 11:00 local time.
2. Let the task fire. It does the work, because no success exists in the window.
3. Fire it again (for example with `POST /api/queues/{id}/live-schedule`, offset `00:00:05`).
4. The second run stops at the `done-today` Break. The execution log shows `lastRun(sequence=self, status=success, since=11:00)` as the break reason.

## 5. Read the statistics

```powershell
(Invoke-RestMethod "http://localhost:8080/api/queues/<queueId>").sequenceStats | ConvertTo-Json -Depth 5
```

Expected: one entry for the sequence, with `successCount: 2`, `lastRunStatus: "success"`, and `lastSuccessAt` equal to the end of the second run. The second run counts as a success because a Break is a normal end.

## 6. Restart and read again

1. `POST /api/queues/{id}/stop`, then `POST /api/queues/{id}/start`. Read the queue. The `sequenceStats` values are the same.
2. Restart the GameBot service. Read the queue. The values are the same.
3. After the restart, the task fires again (template schedule). It stops at the guard, because the success before the restart is still in the window.

## 7. Ad-hoc run

Run the sequence with `POST /api/sequences/{id}/execute`. The guard is `false` (no queue), so the work runs. The queue `sequenceStats` does not change.

## 8. OpenAPI

Open `http://localhost:8080/swagger/v1/swagger.json`. Find:

- the schema `LastRunCondition`. Its description names the discriminator value `type: "lastRun"`. The document does not publish a `discriminator` block on `SequenceStepCondition` for any condition type, so the schema description carries the value;
- the schema `QueueSequenceStatsResponse`, and the property `sequenceStats` on `QueueDetailResponse`.

## Known limit

The web UI does not know the `lastRun` type. If you open a sequence with a `lastRun` condition in the web UI and save it there, the condition can be lost. Write `lastRun` conditions through the API.

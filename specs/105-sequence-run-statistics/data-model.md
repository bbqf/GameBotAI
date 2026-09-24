# Data Model: Per-Sequence Run Statistics per Queue

**Feature**: 105-sequence-run-statistics | **Date**: 2026-09-24

## 1. `SequenceRunStatus` (enum, Domain)

File: `src/GameBot.Domain/Queues/SequenceRunStatus.cs`

| Value | JSON text | Description |
|---|---|---|
| `Success` | `success` | The run completed without a failure. A run that a Break step ends is a success. |
| `Failure` | `failure` | The run ended with an error or a failed step. |
| `Cancelled` | `cancelled` | The queue stopped the run: a stop by hand, a failure-policy stop, or the sequence time limit (watchdog). |

The store and the API write the lower-case JSON text. Parsers accept the text without case (research R-013).

## 2. `SequenceRunRecord` (Domain)

File: `src/GameBot.Domain/Queues/SequenceRunStatistics.cs`

One completed run of one sequence in one queue.

| Field | Type | Rule |
|---|---|---|
| `StartedAt` | `DateTimeOffset` | Service-local time with offset. Taken just before the sequence starts. |
| `EndedAt` | `DateTimeOffset` | Service-local time with offset. `EndedAt >= StartedAt`. |
| `Status` | `SequenceRunStatus` | See section 1. |

A run in progress has no record. A run that the service stop interrupts has no record. Only a run that the queue started has a record. A nested run is not possible at this time. A later nested run gets no record (spec edge case "Nested sequence run").

## 3. `SequenceRunStatistics` (Domain)

File: `src/GameBot.Domain/Queues/SequenceRunStatistics.cs`

One entry for each (queue, sequence) pair.

| Field | Type | Rule |
|---|---|---|
| `LastRunStartedAt` | `DateTimeOffset?` | `StartedAt` of the newest record. Null before the first record. |
| `LastRunEndedAt` | `DateTimeOffset?` | `EndedAt` of the newest record. |
| `LastRunStatus` | `SequenceRunStatus?` | `Status` of the newest record. |
| `LastSuccessAt` | `DateTimeOffset?` | `EndedAt` of the newest `success` record. Does not change on a `failure` or `cancelled` record. |
| `SuccessCount` | `long` | Total since the first record. Never decreases. |
| `FailureCount` | `long` | Same. |
| `CancelledCount` | `long` | Same. |
| `RecentRuns` | `List<SequenceRunRecord>` | Oldest first. At most `MaxRecentRuns` = 100 records. |

**Method** `Apply(SequenceRunRecord record)`:

1. Add the record at the end of `RecentRuns`. If the count is more than 100, remove the first record.
2. Set `LastRunStartedAt`, `LastRunEndedAt`, `LastRunStatus` from the record.
3. Add one to the counter for the status.
4. If the status is `success`, set `LastSuccessAt = record.EndedAt`.

**Method** `HasRunInWindow(SequenceRunStatus status, DateTimeOffset from, DateTimeOffset to)`: true when a record in `RecentRuns` has the given status and `from <= EndedAt <= to`.

## 4. `QueueSequenceStatisticsDocument` (persisted file)

File on disk: `<data root>/queue-sequence-stats/<queueId>.json`. One file for each queue.

```json
{
  "schemaVersion": 1,
  "queueId": "q-7f3c",
  "sequences": {
    "seq-daily-train": {
      "lastRunStartedAt": "2026-09-24T12:00:03+02:00",
      "lastRunEndedAt": "2026-09-24T12:01:10+02:00",
      "lastRunStatus": "success",
      "lastSuccessAt": "2026-09-24T12:01:10+02:00",
      "successCount": 2,
      "failureCount": 1,
      "cancelledCount": 0,
      "recentRuns": [
        { "startedAt": "2026-09-23T12:00:02+02:00", "endedAt": "2026-09-23T12:01:05+02:00", "status": "success" },
        { "startedAt": "2026-09-24T11:00:01+02:00", "endedAt": "2026-09-24T11:00:40+02:00", "status": "failure" },
        { "startedAt": "2026-09-24T12:00:03+02:00", "endedAt": "2026-09-24T12:01:10+02:00", "status": "success" }
      ]
    }
  }
}
```

Rules:

- The key of `sequences` is the sequence ID (ordinal compare). Two template entries of the same sequence share one key.
- A file with a higher `schemaVersion` than the service knows is read as damaged (research R-004).
- An absent file means "no record". A file that does not parse means "no record" plus one Warning log.
- Writes go to `<file>.tmp` first and then replace the file.

## 5. `ISequenceRunStatisticsStore` (Domain interface)

File: `src/GameBot.Domain/Queues/ISequenceRunStatisticsStore.cs`

| Member | Description |
|---|---|
| `Task RecordAsync(string queueId, string sequenceId, SequenceRunRecord record, CancellationToken ct = default)` | Applies the record to the entry of the pair, and writes the queue file. Creates the entry and the file when they do not exist. |
| `Task<IReadOnlyDictionary<string, SequenceRunStatistics>> GetForQueueAsync(string queueId, CancellationToken ct = default)` | Returns a copy of all entries of the queue. Empty when there is no file. |
| `Task<SequenceRunStatistics?> GetAsync(string queueId, string sequenceId, CancellationToken ct = default)` | Returns a copy of one entry, or null. |
| `Task DeleteQueueAsync(string queueId, CancellationToken ct = default)` | Deletes the queue file and the in-memory copy. No error when there is no file. |

Implementation: `FileSequenceRunStatisticsStore(string dataRoot, ILogger<FileSequenceRunStatisticsStore>? logger = null)`, singleton, `IDisposable`. It keeps an in-memory copy of each queue document that it read. One `SemaphoreSlim` serializes all reads from disk and all writes. The return values are copies, so a caller cannot change the cache.

## 6. `LastRunStepCondition` (Domain, sequence model)

File: `src/GameBot.Domain/Commands/SequenceStepCondition.cs`. Discriminator `lastRun`.

| Field | Type | Rule |
|---|---|---|
| `Negate` | `bool` | Inherited. Inverts the result. |
| `Sequence` | `string` | Required. `self` or a sequence ID. Not checked against the sequence store. |
| `Status` | `string` | Required. `success`, `failure` or `cancelled` (case-insensitive). |
| `Since` | `string?` | `HH:mm`, 00:00 to 23:59. |
| `Within` | `string?` | `hh:mm:ss` or `d.hh:mm:ss`, hours may be 24 or more, more than zero, not more than 366 days. |

Exactly one of `Since` and `Within` is set. The strings stay as the author wrote them, so a read returns the same text (round trip).

`LastRunConditionRules` (static, same folder) holds `Validate(LastRunStepCondition) -> IReadOnlyList<string>` (message tails, research R-011), `TryParseSince(string, out TimeOnly)` and `TryParseWithin(string, out TimeSpan)`.

## 7. `LastRunConditionContract` (Service, API)

File: `src/GameBot.Service/Models/SequenceStepContracts.cs`. Discriminator `lastRun`.

| JSON field | Type | Required |
|---|---|---|
| `type` | `"lastRun"` | yes |
| `sequence` | string | yes (the contract uses `string?`, so an absent value gives a 400 from validation, not from the JSON reader) |
| `status` | string | yes (same) |
| `since` | string | one of `since` / `within` |
| `within` | string | one of `since` / `within` |
| `negate` | boolean | no, default `false` |

`SequencesEndpoints.MapPerStepCondition` maps the contract to the domain type. `MapPerStepConditionToDto` maps it back, and leaves out `since` or `within` when it is null.

## 8. `SequenceRunContext` (Domain, ambient)

File: `src/GameBot.Domain/Services/SequenceRunContext.cs`

| Member | Description |
|---|---|
| `string QueueId` | The queue of the run. |
| `string SequenceId` | The sequence of the run. `self` resolves to this value. A nested run is not possible at this time. A later nested run pushes its own context, and this value is then the ID of the nested sequence. |
| `Func<LastRunStepCondition, CancellationToken, Task<bool>> LastRunEvaluator` | Evaluates a `lastRun` leaf for this run. |
| `static SequenceRunContext? Current` | The context of the current async flow, or null (ad-hoc run). |
| `static IDisposable Push(SequenceRunContext context)` | Sets `Current`. `Dispose` puts back the earlier value. |

## 9. `LastRunConditionEvaluator` (Domain)

File: `src/GameBot.Domain/Services/LastRunConditionEvaluator.cs`. Singleton. Depends on `ISequenceRunStatisticsStore` and `TimeProvider`.

`Task<bool> EvaluateAsync(string queueId, string ownSequenceId, LastRunStepCondition condition, CancellationToken ct)`:

1. `target = condition.Sequence == "self" ? ownSequenceId : condition.Sequence`.
2. `now = timeProvider.GetLocalNow()`.
3. `from = Since is set ? LastRunWindow.SinceStart(now, since, timeProvider.LocalTimeZone) : now - within`.
4. `stats = await store.GetAsync(queueId, target)`. If null, return `false`.
5. Return `stats.HasRunInWindow(status, from, now)`.

The evaluator does not apply `Negate`. `SequenceStepConditionEvaluator` applies it, the same as for the other leaves. A stored value that does not parse is not possible, because the save path prevents it. If it occurs, the evaluator throws `ConditionEvaluationException` with kind `UnsupportedCondition`. The step then fails with a clear message.

## 10. `LastRunWindow` (Domain, pure)

File: `src/GameBot.Domain/Services/LastRunWindow.cs`. `static DateTimeOffset SinceStart(DateTimeOffset now, TimeOnly since, TimeZoneInfo zone)`. Algorithm in research R-007.

## 11. `QueueSequenceStatsResponse` (Service, API)

File: `src/GameBot.Service/Contracts/Queues/QueueSequenceStatsResponse.cs`

| JSON field | Type | Description |
|---|---|---|
| `sequenceName` | string or null | From the sequence store. Null when the sequence no longer exists. |
| `lastRunStartedAt` | date-time or null | Start of the last completed run. |
| `lastRunEndedAt` | date-time or null | End of the last completed run. |
| `lastRunStatus` | `success` \| `failure` \| `cancelled` \| null | Status of the last completed run. |
| `lastSuccessAt` | date-time or null | End of the last successful run. |
| `successCount` | integer | Total successful runs. |
| `failureCount` | integer | Total failed runs. |
| `cancelledCount` | integer | Total cancelled runs. |

`QueueDetailResponse.SequenceStats`: `SortedDictionary<string, QueueSequenceStatsResponse>` (ordinal), JSON name `sequenceStats`. Never null.

## State changes

```text
(no entry) --first completed run--> entry with 1 record
entry --completed run--> entry with +1 record (oldest dropped after 100), counters +1
entry --queue deleted--> (no file)
queue stop / start, service restart --> no change
queue template change (sequence removed) --> no change
```
